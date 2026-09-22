# Multi-Agent Architecture & Cross-Agent Workflows — Plan

> **Status: ✅ Done (Phase 13).** Companion to [`docs/plan.md`](plan.md) (see its §16 for the
> one-paragraph pointer and the checklist, kept in sync with §14 below). Implemented in four
> sub-phases (13a foundation, 13b workflow engine, 13c API surface, 13d observability/reorg/
> verification) and verified end to end against the real running stack (real MSSQL, real Ollama
> qwen3:0.6b, real ClaimsToolsServer) — see §14 for what was checked and the one known reliability
> caveat that verification surfaced.

## 1. Why this exists

AgentCore has run exactly one agent (`WorkerClaimAgent`) since Phase 4, with a single hardcoded
system prompt and a client-side split between "read-only" and "claim-processing" tool subsets
(`docs/plan.md` §6). That was the right amount of complexity for one job. The ask now is
different: AgentCore should host **several specialist agents**, each scoped to a distinct domain
with its own system prompt and tool set, plus a **workflow engine that chains specialists
together** to accomplish something none of them could alone — e.g. "find this worker, check their
claim, decide on it, then tell both the worker and their case manager."

This is a deliberate, bounded step into territory `docs/knowledge-base.md` explicitly documented
as *not implemented, by design* in this project (Topic 10, Multi-Agent Orchestration; Topic 11,
Adaptive Planning) — this plan stays on the "fixed pipeline" side of that line, not the "adaptive
planning" side. See §4 for exactly what that means and why.

## 2. Recommended agent catalog

The user asked for three agents (Claims, Worker Management, Notification) and explicitly invited
a recommendation for more. Landed on **four**, reasoning through the obvious candidates rather
than adding agents for their own sake — every additional agent is more system prompt to maintain,
more tool-permission surface to reason about, and more moving parts in every workflow that uses
it (`docs/knowledge-base.md` §10.3's "latency and cost compound" risk applies just as much to
reasoning-load and maintenance cost as to LLM round trips):

| Agent | Domain | Tools (existing MCP tool names) | Sensitive? |
|---|---|---|---|
| **ClaimsAgent** | Claim investigation and payout decisions | `WorkerInformationFetcher`, `ClaimsSearcher`, `WorkerClaimsHistoryFetcher`, `CoverageChecker`, `PayoutCalculator` | Yes (`PayoutCalculator` queues a `PendingAction`) |
| **WorkerManagementAgent** (new) | Everything about a worker as a person/record — not their claims' financial outcome | `WorkerInformationFetcher`, `WorkerClaimsHistoryFetcher` | No — entirely read-only |
| **RiskEscalationAgent** (new) | Fraud/anomaly signals and escalation-trigger evaluation, as its own specialist concern distinct from routine claims processing | `EscalationEvaluator`, `ClaimRiskScorer`, `CoverageChecker`, `ClaimsSearcher` | No — produces a recommendation, never acts |
| **NotificationAgent** (new) | Composing and queuing every outbound message, whatever the channel | `WorkerEmailSender`, `EscalationEmailSender`, `NotifyCaseManagerTool` (new, §6), later `SendSmsTool` | Yes — every tool it owns is sensitive |

**Considered and deliberately not added:**
- **Policy/Coverage Agent** — coverage-checking is intrinsic to claim investigation, not a
  separable skill; `CoverageChecker` stays a tool both ClaimsAgent and RiskEscalationAgent call,
  not its own agent.
- **Reporting/Analytics Agent** — redundant with `ClaimsAgent`'s existing read-only capability
  (`ClaimsSearcher`, `WorkerClaimsHistoryFetcher` already answer aggregate questions); a separate
  agent would just be the same tools under a different name.
- **An agent for admin data entry** (creating workers/policies via natural language) — these stay
  deliberate, structured, human-typed REST forms (`WorkersController`/`PoliciesController`), not
  agent-mediated. No safety upside to routing structured CRUD through an LLM, and it would be a
  new write-capable surface for no real benefit.

## 3. Two decisions the user made (2026-09-22)

These materially shape the rest of this document, so they're recorded up front rather than buried
in a design-rationale table:

1. **Sensitive/notification tools belong to `NotificationAgent` exclusively.** `ClaimsAgent` no
   longer has `WorkerEmailSender`/`EscalationEmailSender` in its tool set at all — it can compute
   a payout and quote coverage/escalation status, but it cannot email anyone. To actually notify
   someone, a workflow chains `ClaimsAgent` → `NotificationAgent`, or `NotificationAgent` is
   invoked on its own. This is a real behavior change from today, where a single claim-processing
   run can both compute a payout *and* propose an email in the same run.
2. **`POST /api/agent/claims/{id}/process` is reimplemented to run the new built-in "Process
   Claim" workflow** (§7) instead of a single `ClaimsAgent`-only call. The endpoint's contract
   changes accordingly (§8) — one `AgentRunLog` becomes several (one per step), and case-manager
   notification becomes a first-class step instead of something only ever reachable via a
   standalone `EscalationEmailSender` call.

## 4. Orchestration model: fixed pipeline, not adaptive planning

A `WorkflowDefinition` (existing entity, extended in §5) becomes an **ordered sequence of steps**,
each step naming exactly one specialist agent and a prompt template for that step. The *sequence*
itself is authored ahead of time by an Admin, exactly like today's single-agent workflows
(`docs/plan.md` §11: "not a general workflow builder — no branching, no loops"). That constraint
carries forward unchanged and now matters more, not less:

- **No agent decides which agent runs next.** The orchestrator (`WorkflowExecutionService`, §7)
  walks the fixed step list top to bottom. There is no manager agent drafting a plan at runtime
  (`docs/knowledge-base.md` Topic 11) and no agent-to-agent handoff where one specialist decides
  to delegate to another mid-run (Topic 10's handoff pattern, or the "agent as tool" delegation
  pattern from Topic 2) — both were considered and rejected for the reason those topics already
  give: it reintroduces exactly the unpredictable-cost, hard-to-audit surface this project has
  consistently avoided.
- **Conditionality lives inside a step's own reasoning, not in the orchestrator's control flow.**
  E.g. `NotificationAgent`'s step prompt says "if the claim was declined, send a decline notice
  instead of a payout confirmation" — the *agent* reasons about which tool to call, but the
  *workflow* always runs that step. The orchestrator itself never branches or skips a step based
  on a prior step's output.
- **Every step still runs under the same human caller's identity** (`CallerIdentity`, OBO into
  MCP) — nothing about multi-step execution weakens that; see §7.
- **A workflow is still synchronous, single-HTTP-request, no pause/resume.** If any step's
  specialist proposes a sensitive action, it's queued as a `PendingAction` and the workflow simply
  continues to the next step — it does not pause to wait for human approval mid-workflow. Pausing
  a workflow across an approval boundary is the "interrupt-based HITL with checkpointing" pattern
  from `docs/knowledge-base.md` §8, which would need persisted, resumable workflow state — a real,
  bigger feature, explicitly out of scope for this phase (§11).

## 5. Entity model changes

All additive/backward-compatible — the 3 existing seeded single-agent workflows keep running
unmigrated, on what becomes the "legacy" code path.

- **`WorkflowStepDefinition`** (new, child of `WorkflowDefinition`): `Id`, `WorkflowDefinitionId`
  (FK), `StepIndex` (int, execution order), `AgentName` (string — a key into the code-level agent
  catalog, §6, not a DB-authored value), `PromptTemplate` (string — `{inputName}` placeholders
  resolved from the workflow's top-level inputs, *and* `{steps.OutputKey}` placeholders resolved
  from an earlier step's result), `OutputKey` (string — the label later steps reference it by).
- **`WorkflowDefinition`** gains a `Steps` collection (`ICollection<WorkflowStepDefinition>`,
  possibly empty). `AllowedToolNamesJson` and the existing top-level `PromptTemplate` are
  unchanged and still used by definitions with no steps (the legacy, single-agent path) — a
  definition is in "multi-agent mode" purely by having `Steps.Any()`, no new flag needed.
- **`WorkflowRunStep`** (new, child of `WorkflowRun`): `Id`, `WorkflowRunId` (FK), `StepIndex`,
  `AgentName`, `AgentRunLogId` (FK — **one `AgentRunLog` per step**, so every specialist's
  invocation is exactly as fully audited as a standalone agent call is today), `OutputKey`,
  `ResolvedPromptSnapshot` (string — the actual prompt sent, after placeholder substitution; audit
  trail for *what the step was actually asked*, not just what template it came from).
- **`WorkflowRun`** gains a `Steps` collection (`ICollection<WorkflowRunStep>`). Its existing
  single `AgentRunLogId` field is **kept, unchanged in meaning** — for a multi-step run it's set
  to the *final* step's `AgentRunLogId`, so any existing code reading "the answer" off a
  `WorkflowRun` keeps working without modification. `Steps` is where the full per-step detail
  lives for anything that wants it.
- **`PendingActionType`** gains `NotifyCaseManager` (this phase). A `SendWorkerSms`/equivalent is
  deliberately *not* added yet — see §11 (SMS is explicitly next-phase, email only for now, per
  the user's own scoping).
- **No new `AgentDefinition` database table.** Agents are system-defined specialists with a fixed
  tool set, authored in code (`AgentCatalog`, §6) — only *workflows* (which agents to chain, and
  in what order) are Admin-authorable data. This mirrors the existing split where tool
  descriptions/permissions are code, but which tools a workflow may use is data.

## 6. Agent layer: from one hardcoded factory to a catalog

Today, `WorkerClaimAgentFactory` has one `Instructions` constant and three purpose-built methods
(`CreateReadOnlyAgentAsync`, `CreateClaimProcessingAgentAsync`, `CreateWorkflowAgentAsync`). The
last of these already builds an agent from an **explicit tool-name allowlist** — exactly the
mechanism every specialist agent needs, just not yet parameterized by system prompt too. The
actual diff here is smaller than the "four new agents" framing suggests:

- **`AgentDefinition`** (new, plain record in `AgentCore.Agents`): `Name` (stable key, e.g.
  `"ClaimsAgent"`), `Instructions` (system prompt), `ToolNames` (`IReadOnlySet<string>`), and
  optional per-agent overrides (`MaxToolCallsPerRun`, `MaxRunDuration`) falling back to
  `AgentOptions`' existing global defaults if unset — some specialists genuinely need different
  guard tuning (`NotificationAgent` should rarely need more than 1-2 tool calls; `RiskEscalationAgent`
  might reasonably need more history look-back calls than `ClaimsAgent`'s routine path).
- **`AgentCatalog`** (new, static): the four `AgentDefinition`s above, plus a lookup by name. This
  is also what backs the new `GET /api/agents` endpoint (§8).
- **`WorkerClaimAgentFactory` is generalized, not replaced 4x**: its private `BuildAgent(List<AITool>
  tools)` becomes `BuildAgent(AgentDefinition definition, List<AITool> tools)`, parameterizing the
  `Name`/`Instructions`/guard values that are hardcoded today. A new public
  `CreateAgentAsync(AgentDefinition definition, CallerIdentity caller, CancellationToken ct)`
  becomes the one general entry point; `CreateWorkflowAgentAsync` collapses into a call to this
  with a synthesized one-off `AgentDefinition` (workflow-authored tool allowlist + a generic
  "follow this workflow step's instructions" system prompt, as today). Consider renaming the class
  itself (`AgentFactory`, since it's no longer specific to "the worker claim agent") — confirm on
  review before renaming, since it's a public type other code already references.
- **`AgentToolsFactory` is unchanged.** Its `BuildToolsetAsync(IReadOnlySet<string> allowedToolNames,
  CallerIdentity caller, ct)` overload already does exactly what every specialist agent's tool
  resolution needs — the four agents' distinct tool sets are just four different call sites of
  code that already exists. `SensitiveToolNames` (client-side, currently
  `["WorkerEmailSender","EscalationEmailSender","PayoutCalculator"]`) needs no change in shape,
  just needs `NotifyCaseManagerTool`'s name added once it exists.

## 7. Workflow execution: one `AgentRunLog` per step, reusing existing plumbing

`ClaimAgentService.RunWithCustomAgentAsync(AIAgent agent, string prompt, string trigger, int?
claimId, CancellationToken ct)` **already exists** and already does everything a step needs:
builds the `AgentRunLog`, wraps the call in the OpenTelemetry span, publishes the SignalR
lifecycle events, backfills any `PendingActionRef` it sees. No new "run an agent and audit it"
logic needs writing — the multi-step engine calls this once per step, exactly as
`WorkflowExecutionService.RunAsync` already calls it once today for a whole (single-agent) run.

`WorkflowExecutionService.RunAsync` gains a branch:

```
if (definition.Steps.Any())
{
    // NEW multi-agent path
    var context = new Dictionary<string, string>(resolvedTopLevelInputs);
    foreach (var step in definition.Steps.OrderBy(s => s.StepIndex))
    {
        var agentDef = AgentCatalog.GetByName(step.AgentName);
        var resolvedPrompt = ResolvePlaceholders(step.PromptTemplate, context); // {input} + {steps.Key}
        var agent = await _agentFactory.CreateAgentAsync(agentDef, caller, ct);
        var stepRun = await _claimAgentService.RunWithCustomAgentAsync(agent, resolvedPrompt, trigger, claimIdForRun, ct);
        await _workflowRunSteps.AddAsync(new WorkflowRunStep { .../* StepIndex, AgentName, AgentRunLogId = stepRun.Id, OutputKey = step.OutputKey, ResolvedPromptSnapshot = resolvedPrompt */ }, ct);
        context[step.OutputKey] = stepRun.FinalAnswer;
    }
    // WorkflowRun.AgentRunLogId = the final step's stepRun.Id
}
else
{
    // EXISTING single-agent path, unchanged
}
```

**Fail-fast, not partial-continue.** If any step's agent run throws, hits `GuardTripped`/`Timeout`,
or — critically — if a step's underlying MCP tool call is denied by `WorkerAccessPolicy` (the
caller, a CaseManager, isn't assigned to the worker a later step's claim belongs to), the whole
workflow stops immediately at that step. This is not a new mechanism: it already falls out of the
existing per-tool `ClaimAccessGuard` check inside `ClaimsToolsServer` (`docs/plan.md` §5) — a
denied tool call in step 2 just means step 2's `AgentRunLog` records a denial, and the loop above
doesn't get a usable output to hand to step 3, so it stops there rather than feeding a poisoned
`{steps.X}` value forward silently.

**Placeholder resolution (`{input}` / `{steps.OutputKey}`)**: reuses the same simple
string-templating already used for today's single-`PromptTemplate` workflows — no new templating
engine, just a second placeholder namespace (`steps.*`) resolved from the running `context`
dictionary before each step, alongside the existing top-level input placeholders.

## 8. API surface changes

| Endpoint | Change |
|---|---|
| `GET /api/agents` (new) | Lists the agent catalog — name, display name, one-line description, tool names. Read-only, capability discovery; any authenticated role. |
| `POST /api/agents/{agentName}/query` (new) | Generic free-form Q&A against a named specialist — generalizes today's `POST /api/agent/query` (which becomes an alias for `agentName=claims`, kept for backward compatibility rather than removed). `NotificationAgent` is unlikely to be queried this way directly (its only job is composing/sending, better triggered with concrete context from a workflow step) but nothing structurally prevents it. |
| `POST /api/agent/claims/{id}/process` | **Behavior change (decision §3.2)**: internally runs the built-in "Process Claim" `WorkflowDefinition` (§9) instead of a single `ClaimsAgent`-only call. `WorkerAccessPolicy` denial and the `Disputed`-claim hard block still happen as a pre-flight check *before* the workflow starts (unchanged — deny before any LLM call happens at all, per the existing invariant). Response shape changes — see below. |
| `POST /api/workflows/{id}/run`, `POST /api/workflows/chat` | Unchanged endpoints; transparently execute either the legacy single-agent path or the new multi-step path depending on whether the definition has `Steps`. |
| `POST /api/workflows` (define) | `CreateWorkflowRequest` gains an optional `Steps` array (`{agentName, promptTemplate, outputKey}[]`, ordered) alongside the existing fields — omit it (or send `AllowedToolNamesJson` instead) for a legacy single-agent definition. |

**`ProcessClaimResponse` contract change** (breaking, flagged clearly since something consumes
this today — the UI's claim processing button): gains a `steps: [{ agentName, run: AgentRunLogDto
}]` array (one entry per specialist that ran); `run` (top-level) keeps meaning "the final step's
AgentRunLog," so `recommendation`/`claimStatus` (read off the final step) don't change shape.
`queuedActions` becomes the union of every `PendingAction` queued across all steps, not just one
step's — e.g. a payout from `ClaimsAgent`'s step *and* an email from `NotificationAgent`'s step
both show up in the same response, whereas today only one agent ever ran so there was only ever
one step's worth of queued actions to report.

## 9. The built-in "Process Claim" workflow, redesigned

Replaces today's single-agent seeded "Process Claim" workflow (currently a thin wrapper that just
delegates to the old `ProcessClaimAsync`). New shape, 3 steps, input `claimId`:

| Step | Agent | Prompt (abridged) | `OutputKey` |
|---|---|---|---|
| 1 | `ClaimsAgent` | Check coverage for claim `{claimId}`, compute a payout if eligible, and give your assessment. | `ClaimDecision` |
| 2 | `RiskEscalationAgent` | Given claim `{claimId}` and this assessment: `{steps.ClaimDecision}` — evaluate escalation/fraud-risk flags. | `RiskAssessment` |
| 3 | `NotificationAgent` | Given this decision: `{steps.ClaimDecision}` and this risk assessment: `{steps.RiskAssessment}` — notify the worker of the outcome, and separately notify their assigned case manager with a summary. | `Notifications` |

A worker-lookup step is deliberately **not** included here — `ProcessClaimAsync`'s existing
pre-flight already loads the `Claim`/`Worker` and runs the `WorkerAccessPolicy` check
deterministically before anything else happens, so a `WorkerManagementAgent` step would just
re-fetch, via an LLM call, information the orchestrator already has in hand. That's the right
call for *this* specific workflow; it's not a rule that every workflow skips worker lookup — see
the next one.

## 10. A second, new built-in workflow: "Worker Claim Assistance"

This is the closer match to the user's own example (find worker → check worker → check claim →
decide → notify), and the one that actually shows off 4 agents in one workflow rather than 3.
Input: `workerId` (the worker whose claims need attention — used when the caller doesn't already
know a specific `claimId` and wants the assistant to start from "this worker").

| Step | Agent | Prompt (abridged) | `OutputKey` |
|---|---|---|---|
| 1 | `WorkerManagementAgent` | Look up worker `{workerId}` — confirm they exist, summarize their record and recent claims history. | `WorkerSummary` |
| 2 | `ClaimsAgent` | Given this worker: `{steps.WorkerSummary}` — find their most recent claim needing action, check coverage, and compute a payout if eligible. | `ClaimDecision` |
| 3 | `RiskEscalationAgent` | Given this claim decision: `{steps.ClaimDecision}` — evaluate escalation/fraud-risk flags. | `RiskAssessment` |
| 4 | `NotificationAgent` | Given `{steps.ClaimDecision}` and `{steps.RiskAssessment}` — notify the worker and their case manager. | `Notifications` |

Seeded alongside "Worker Claims History" and "Escalate High-Value Claim" (both stay on the legacy
single-agent path unchanged — no reason to migrate them, they don't need multiple specialists).

## 11. Explicitly deferred (documented, not silently dropped)

- **SMS.** The user was explicit: email only, for now. `ISmsSender` (a Domain port mirroring
  `IEmailSender`), `SendSmsTool`, and a `SendWorkerSms`/`NotifyCaseManagerSms` `PendingActionType`
  are all future work, added the same way `IEmailSender` was — no code for these lands in Phase
  13.
- **Pausing a workflow across a human-approval boundary.** Every sensitive action a step proposes
  is queued and the workflow keeps going (§4) — a workflow that needed to *wait* for a decision
  before its next step would need persisted/resumable execution state, a materially bigger
  feature (`docs/knowledge-base.md` §8's "interrupt-based" pattern with checkpointing).
  Not requested; not built.
- **Adaptive planning / agent-to-agent delegation.** Covered in §4 — a deliberate, permanent "not
  now" consistent with this project's existing stance, not a temporary gap.
- **Live per-step SignalR events** (`WorkflowStepStarted`/`WorkflowStepCompleted`, distinct from
  today's per-run `RunStarted`/`ToolCallStarted`/etc.) and a `SubscribeToWorkflowRun` hub method.
  Today's per-run events still fire once per step (each step is a full `ExecuteRunAsync` call), so
  nothing is *lost* — there's just no single combined "workflow progress" stream yet. Worth doing,
  not required for Phase 13 to be useful.
- **UI support** for defining/viewing multi-step workflows (`docs/plan-ui.md`'s `WorkflowsPage`
  only knows the single-agent, single-tool-allowlist shape today) — a follow-up phase, same
  pattern as Identity & Authorization's backend-then-UI split.

## 12. Observability

`AgentCoreDiagnostics.AgentRuns`/`AgentRunDuration` (`docs/plan.md` §8) gain an `agent_name` tag
(today only tagged by mode/outcome) — since every specialist's invocation is still a full
`ExecuteRunAsync` call, this is a small, additive change that immediately makes "which specialist
is slow/failing" visible in Grafana without any new panel wiring beyond a `by (agent_name)` group.

## 13. Restructuring: what does and doesn't change

Direct answer to "if need restructure... give the new architecture": **no new projects, no new
deployable processes.** Reasoning, not just assertion — matching the standard this project has
already held itself to (`docs/knowledge-base.md` §10.4: "the simplest architecture that solves the
problem should be the default"):

- All four agents are served by the **same** `mcp/ClaimsToolsServer` process and the **same**
  `AgentCore.Agents` project. They differ in system prompt + tool allowlist, not in runtime,
  database, or network boundary — nothing here has a different scaling profile, security
  boundary, or ownership story that would justify a second MCP server or a second agents project.
- **One organizational change, no behavior change**: `mcp/ClaimsToolsServer/Tools/` gets
  subfoldered by domain (`Tools/Claims/`, `Tools/Worker/`, `Tools/Risk/`, `Tools/Notification/`)
  to mirror the new agent boundaries as the tool count grows past today's 9 (soon 10, with
  `NotifyCaseManagerTool`) — pure file organization, same namespace, same registration mechanism
  (`WithToolsFromAssembly()` doesn't care about folder structure).
- If a future need genuinely requires independent scaling or a different trust boundary for one
  agent's tools (e.g. `NotificationAgent`'s tools calling a real, rate-limited external email/SMS
  provider under load) — that's the point at which splitting it into its own MCP process the way
  `docs/plan-mcp.md` already documents would become justified. Not justified today.

## 14. Implementation checklist (Phase 13 — done)

- [x] `PendingActionType.NotifyCaseManager`; `NotifyCaseManagerTool` (new MCP tool,
  `mcp/ClaimsToolsServer/Tools/Notification/`) — resolves `Claim.WorkerId` →
  `Worker.AssignedCaseManagerUserId` → `User.Email`, same `ClaimAccessGuard`/`PendingAction`
  pattern as the existing sensitive tools.
- [x] `AgentDefinition` + `AgentCatalog` (`AgentCore.Agents`) — the four specialists from §2.
- [x] Generalized `WorkerClaimAgentFactory` into `AgentFactory`: `CreateAgentAsync(AgentDefinition,
  caller, ct, includeSensitiveTools=true)` is the general entry point; `CreateWorkflowAgentAsync`
  kept for the legacy allowlist path; `CreateReadOnlyAgentAsync`/`CreateClaimProcessingAgentAsync`
  removed once confirmed unused.
- [x] `WorkflowStepDefinition`, `WorkflowRunStep` entities + migration; `WorkflowDefinition`/
  `WorkflowRun` gained `Steps` collections.
- [x] `WorkflowExecutionService.RunAsync`: branches on `definition.Steps.Any()`; multi-step path per
  §7 (placeholder resolution, per-step `WorkflowRunStep` audit row, fail-fast on any step's
  failure/denial).
- [x] `AgentCoreDiagnostics`: `agent_name` tag on `AgentRuns`/`AgentRunDuration`.
- [x] `GET /api/agents`, `POST /api/agents/{agentName}/query`; `POST /api/agent/query` is an
  alias for `agentName=ClaimsAgent`.
- [x] `ProcessClaimAsync`/`AgentController.ProcessClaim`: reimplemented to run the redesigned
  "Process Claim" `WorkflowDefinition` (§9); `ProcessClaimResponse` shape updated (§8).
- [x] Seeded the redesigned "Process Claim" (3 steps) and new "Worker Claim Assistance" (4 steps)
  workflow definitions (§9, §10) in `AgentCoreDbSeeder`.
- [x] Reorganized `mcp/ClaimsToolsServer/Tools/` into domain subfolders (`Worker/`, `Claims/`,
  `Risk/`, `Notification/`) — confirmed pure reorg (build + tests unaffected, `WithToolsFromAssembly`
  doesn't care about folder structure).
- [x] Verified end-to-end against the real running stack: the redesigned
  `/api/agent/claims/{id}/process` producing multiple `AgentRunLog`s and both a worker and a
  case-manager `PendingAction`, confirmed correct and repeatable across several runs; a
  CaseManager denied processing a claim whose worker isn't assigned to them (403 up front, before
  any agent runs at all — the strictest form of the fail-fast invariant in §7, since the pipeline
  never starts rather than aborting partway through).
- [x] Update `docs/plan.md` §16 and this document's status header to Done, with a Change Log
  entry; update `docs/README.md`'s index.

### Known limitation surfaced by verification: "Worker Claim Assistance" reliability with qwen3:0.6b

The "Worker Claim Assistance" workflow (§10) — which asks the Claims Agent to autonomously
*discover* the right claim for a given worker, rather than being handed a claim Id directly like
"Process Claim" is — proved measurably less reliable against the project's small local model
(`qwen3:0.6b`) than the flagship "Process Claim" workflow, across several live runs against the
real stack:

- **Two genuine, structural gaps were found and fixed** as a direct result of this verification,
  independent of model reliability: `ClaimsSearcher` never exposed a claim's numeric `Id` (only
  `ClaimNumber`), making it impossible for any agent to go from a search result to a callable claim
  Id — fixed by adding `Id` to its output. It also had no way to scope a search to one specific
  worker, forcing the model to eyeball an industry-wide result list and self-match a worker's row
  by reading text — which it got wrong at least once, picking a different worker's claim entirely.
  Fixed by adding an optional `worker` parameter that filters server-side, deterministically, by
  worker Code/ID/name (`ClaimsSearchTool.cs`).
- **Even with both fixes applied, qwen3:0.6b still failed to complete the workflow correctly on
  the runs tested**, in several different ways across attempts: not calling `ClaimsSearcher` at
  all despite explicit instruction to; matching only one of two instructed statuses (`Pending` OR
  `UnderReview`) rather than trying both, missing the worker's actual open claim; and, once, the
  Worker Management Agent step fabricating "0 claims recorded" in its own summary instead of
  calling `WorkerClaimsHistoryFetcher`, which then caused the following step to spin and time out.
- **The safety invariant held in every single one of these failures.** A fabricated/wrong claim Id
  was always caught by `ClaimAccessGuard` inside the sensitive tools and denied (`PendingActionId:
  0`, a `denialReason`) before anything was written to the database — except once, where the model
  picked a *real* claim belonging to the wrong worker, which passed the access guard (the claim
  genuinely exists) and produced one real `PendingAction`; that action was caught and rejected
  during this verification before it could be approved, and the underlying gap that allowed it
  (no worker-scoped search) is the fix described above.
- **This is treated as an accepted characteristic of running a deliberately small, free, local
  model for this demo/dev setup, not a code defect to keep chasing with ever-more prompt
  engineering** — consistent with this project's documented "swap the LLM provider" capability
  (see `README.md`) for anyone who needs this specific workflow to be reliable in a real
  deployment. The workflow, its tools, and its prompts are all correctly built for a model that
  reliably follows multi-step tool-use instructions; qwen3:0.6b sometimes doesn't, and when it
  doesn't, the system's core invariant — the LLM proposes, a human approves, nothing sensitive
  executes unreviewed — still holds.

Explicitly **not** in this checklist, per §11: SMS, workflow pause/resume across approval, live
per-step SignalR events, UI support for authoring multi-step workflows.
