# AgentCore V2.0 — Plan

> This document is the living source of truth for AgentCore V2.0. Whenever a requirement
> changes or a new requirement is requested, this file (architecture sections + checklist)
> must be updated in the same turn. Whenever an implementation step is completed, its
> checklist item is marked done (`[x]`) with a one-line note on what/where. See
> [Change Log](#change-log) at the bottom for a running history of requirement changes.

## Contents

1. [Overview](#1-overview)
2. [Key decisions](#2-key-decisions)
3. [Solution structure](#3-solution-structure)
4. [Domain model (MSSQL via EF Core)](#4-domain-model-mssql-via-ef-core)
5. [Auth: JWT + user management](#5-auth-jwt--user-management)
6. [Agent tool classification](#6-agent-tool-classification)
7. [REST API surface (Swagger-documented)](#7-rest-api-surface-swagger-documented)
8. [Observability (LGTM-style stack)](#8-observability-lgtm-style-stack)
9. [Real-time agent visibility (SignalR)](#9-real-time-agent-visibility-signalr)
10. [Docker Compose](#10-docker-compose)
11. [Workflows (playbooks)](#11-workflows-playbooks)
12. [UI](#12-ui-see-docsplan-uimd) (see `docs/plan-ui.md`)
13. [Agent hardening & reliability (gap remediation)](#13-agent-hardening--reliability-gap-remediation)
14. [Conversation sessions (multi-turn chat)](#14-conversation-sessions-multi-turn-chat)
15. [Implementation checklist](#15-implementation-checklist) — phase-by-phase build history
- [Change Log](#change-log) — dated log of every requirement/architecture change

## 1. Overview

AgentCore V2.0 lives entirely in the `agent-core/` folder as a **new, standalone project**.
It does not reuse code, tools, models, or seed data from `first-agent/` or any other folder
in this repository — `first-agent` remains untouched as a separate, unrelated demo. AgentCore
V2.0 covers the same conceptual domain (workers and claims) but every model, tool, and piece
of data is authored fresh inside `agent-core/`. The system:

- Persisted data in **MSSQL**, run via **Docker Compose**.
- A **RESTful API** (ASP.NET Core) with **Swagger** for manual testing.
- **SuperAdmin**, **Admin**, and **CaseManager** roles (a strict hierarchy - see section 5) that
  can view data and run an agent against claims; a `CaseManager` is scoped to only the `Worker`s
  assigned to them.
- An agent that can freely read data, but must queue any sensitive/side-effecting action
  (send email, escalate, calculate payout) for **human approval** before it executes.

## 2. Key decisions

| Decision | Choice | Notes |
|---|---|---|
| Claim workflow | Hybrid | Read/query actions (worker lookup, claims history, search) execute automatically. Sensitive actions (email worker, escalate to higher level, calculate payout) are queued as a `PendingAction` and require Admin+ approval before they run. |
| Auth | Real JWT (Phase 12) | `POST /api/auth/login` issues an HMAC-SHA256 JWT; every other call sends `Authorization: Bearer <token>`. Replaces the original `X-Role` header design entirely - see section 5. |
| LLM provider | Local Ollama | Runs as a Docker Compose service (`ollama`) reached at `http://ollama:11434` over the compose network — see the Docker Compose section for why this reverses an earlier decision to use a native host install. Not connected to or shared with `first-agent` in any way. |
| Existing `first-agent` | Untouched, unrelated | `agent-core/` is a brand-new, standalone project. No code, models, tools, or data are reused or referenced from `first-agent/` (or any other folder) — everything is authored fresh inside `agent-core/`. |
| Observability | LGTM-style stack | Loki (logs), Grafana (dashboards), Tempo (traces) via OpenTelemetry. **Prometheus** substitutes for Mimir (the "M" in LGTM) — same Grafana Cloud-style stack, but a single-container setup instead of Mimir's storage/ring config, which isn't worth the complexity for a demo. |
| Live agent visibility | SignalR | A hub broadcasts agent run progress (run started, each tool call, final answer) in real time, so a UI can watch an agent "think" as it works, instead of only seeing the final `AgentRunLog` after the fact. |

## 3. Solution structure

Everything below lives inside `agent-core/` (sibling to, and fully independent of,
`first-agent/`):

```
agent-core/
├── AgentCore.sln
├── src/
│   ├── AgentCore.Api            # ASP.NET Core Web API + Swagger, controllers, role-header middleware
│   ├── AgentCore.Domain         # Entities/enums, no dependencies
│   ├── AgentCore.Infrastructure # EF Core DbContext, MSSQL migrations, repositories, seed data
│   ├── AgentCore.Agents         # Microsoft.Agents.AI setup, tools, tool classification, Ollama client
│   └── AgentCore.Application    # Services orchestrating domain + agents (ClaimService, ApprovalService, WorkerService)
├── compose.yaml
└── docs/plan.md                 # this document
```

## 4. Domain model (MSSQL via EF Core)

All entities below are authored fresh in `AgentCore.Domain` (conceptually similar to
`first-agent`'s models, but not copied or referenced from it):

- **Worker** — Id, Code, Name, Role, Location, Email, PhoneNumber, HourlyRate,
  YearsOfExperience, IsAvailable, `AssignedCaseManagerUserId` (nullable FK → `User`, `SetNull` on
  delete) - the row-level permission boundary a `CaseManager` is scoped by (see section 5). Not
  to be confused with `User.Role = CaseManager`, the AgentCore staff account type - a `Worker` is
  the injured employee a claim is about, unrelated to system accounts.
- **InsurancePolicy** — FK to Worker; PolicyNumber, Provider, CoverageType, CoverageAmount,
  StartDate, EndDate, IsActive.
- **Claim** — FK to Worker; ClaimNumber, ClaimDate, ClaimType, Amount, Status, Description,
  `ReviewedBy`, `ReviewedAtUtc`, `AgentRecommendation` (nullable text), `AgentConfidence`
  (nullable), plus (Phase 10, per `docs/business-logic.md`) `IncidentDate`, `ReportedDate`,
  `Jurisdiction`, and the expanded lifecycle status set including `Disputed`.
- **PendingAction** (new) — approval-gate table: `Id`, `ClaimId`, `ActionType` (enum:
  `SendWorkerEmail`, `SendEscalationEmail`, `CalculatePayout`, …), `Payload` (JSON),
  `ProposedByAgentRunId`, `Status` (`AwaitingApproval` / `Approved` / `Rejected` /
  `Executed` / `ExecutionFailed`), `RequestedAtUtc`, `DecidedByRole`, `DecidedAtUtc`,
  `ExpiresAt`, `IdempotencyKey` (Phase 9 - a post-approval side effect that throws sets
  `ExecutionFailed` instead of leaving the caller with an unhandled 500).
- **AgentRunLog** (new) — audit trail of every agent invocation: `Id`, `ClaimId` (nullable),
  `Trigger` (role + endpoint), `Prompt`, `ToolCallsJson`, `FinalAnswer`, `CreatedAtUtc`, plus
  (added after all 7 phases were done - see Change Log) `ModelId`, `ToolCallCount`,
  `ReasoningText` (nullable - the model's "thinking" output, confirmed present for qwen3's
  reasoning mode), `InputTokenCount`/`OutputTokenCount`/`TotalTokenCount` (nullable - from
  `response.Usage`, confirmed populated by OllamaSharp), `InputCost`/`OutputCost`/`TotalCost`
  (nullable decimal(18,6) - tokens × `AgentOptions`' configurable per-million-token price,
  defaulting to 0 for the local Ollama setup).
- **WorkflowDefinition** (new, Phase 8) — a named, reusable playbook admins configure ahead of
  time: `Id`, `Name`, `Description`, `InputSchemaJson` (ordered list of `{name, type, required,
  description}`, e.g. `workerId:int`, `claimId:int`, `dateFrom`/`dateTo:date`),
  `PromptTemplate` (string with `{placeholder}` slots filled from resolved inputs),
  `AllowedToolNamesJson` (restricts the agent's tool set for this workflow to a named subset of
  the existing auto/sensitive tools), `IsChatTriggerable` (bool), `ChatTriggerHintsJson`
  (example phrases/keywords used by the chat intent-matcher), `IsActive`, `CreatedByRole`,
  `CreatedAtUtc`. Deliberately *not* a graph/DAG - no branching, looping, or multi-step chains;
  a different tool set or prompt is a new row, not a new engine feature.
- **WorkflowRun** (new, Phase 8) — thin metadata wrapper recording how/why one execution was
  launched: `Id`, `WorkflowDefinitionId`, `AgentRunLogId` (FK - the actual execution is still a
  single `AgentRunLog`, unchanged), `InputValuesJson` (the resolved inputs actually used),
  `TriggerSource` (enum: `Structured` / `Chat`), `RawChatInput` (nullable - the original free
  text, kept for audit when triggered via chat), `MatchConfidence` (nullable - the intent-
  matcher's confidence when triggered via chat), `CreatedAtUtc`.

- **User** (new, Phase 12) — a real AgentCore staff/system account, **not** to be confused with
  `Worker` (the injured employee a claim is about - a completely separate concept that already
  existed and is unchanged). `Id`, `Email` (unique), `Name`, `PasswordHash`, `Role` (enum
  `UserRole`: `SuperAdmin` / `Admin` / `CaseManager`), `IsActive` (soft-disable, never hard-delete
  a user - preserves the audit trail anything they did still points to), `CreatedByUserId`
  (nullable, self-referencing FK - who created this account; null for the seeded SuperAdmin),
  `CreatedAtUtc`, `LastLoginAtUtc` (nullable).

## 5. Auth: JWT + user management

**Replaces the header-trust model entirely** (not additive/coexisting - if `X-Role` still worked
alongside real login, anyone could bypass authentication just by setting a header, defeating the
entire point of adding it). Prompted by the user pointing out that the original "simplified role
header, no login" design (Topic 4 of `docs/knowledge-base.md`, "Identity & Authorization") was
always a known, deliberate gap for this demo - now being closed for real.

### Why this replaces, not extends, section 5's original design

The old model had two problems this fixes: **(1)** any caller could claim to be `Admin` and any
name via `X-Actor-Name` - there was no real identity behind a role, just an asserted string.
**(2)** only two tiers existed. Real per-user accounts with hashed passwords and issued tokens
close both at once.

### Role hierarchy

Three roles, strictly nested (each higher role can do everything a lower one can, plus more):

```
SuperAdmin ⊃ Admin ⊃ CaseManager
```

| Role | Capability |
|---|---|
| **CaseManager** | Exactly today's `Manager` role, renamed to the real industry term (a case manager is the actual job title for day-to-day workers'-comp claims handling): read everything, run the agent, approve `PendingAction`s tied to claims, create/read/update claims. Cannot delete Workers, edit Policies, or touch user management. |
| **Admin** | Exactly today's `Admin` role (full CRUD on Workers/Policies/Claims, approve/reject any `PendingAction`, define Workflows) **plus** can create/manage `CaseManager` user accounts only - not `Admin` or `SuperAdmin` accounts, so a compromised Admin account can't mint itself more power. |
| **SuperAdmin** | Everything Admin can do, **plus** full user management: create/deactivate/change the role of *any* user, including other Admins. Seeded once at startup (see below) - this is the account the user will personally use to create the first real Admin/CaseManager accounts. |

**Mechanism, not just policy**: a JWT's `role` claims include every role the holder's tier
*inherits*, not just their own - a `SuperAdmin` token carries `["SuperAdmin","Admin",
"CaseManager"]`, an `Admin` token carries `["Admin","CaseManager"]`, a `CaseManager` token carries
just `["CaseManager"]`. This means every existing `[Authorize(Roles = "Admin,Manager")]` /
`[Authorize(Roles = "Admin")]` attribute across every controller needs only its literal strings
updated (`Manager` → `CaseManager`) - `SuperAdmin` is automatically covered by every check that
already accepts `Admin`, with no controller logic changes and no custom policy handler needed.

### What's being removed

- `HeaderRoleAuthenticationHandler` and the whole `X-Role`/`X-Actor-Name` header scheme.
- The `X-Role` Swagger `ApiKey` security definition and the `ActorNameHeaderFilter`
  (`X-Actor-Name` as a per-operation header parameter).
- The UI's `RoleGate` role-picker dropdown (today: pick `Admin` or `Manager` from a `<select>` and
  type any name, no credentials at all - replaced with a real login form).

### What's added

- **`AuthController`**: `POST /api/auth/login` (email + password → `{ accessToken, expiresAtUtc,
  user: { id, name, email, role } }`), `GET /api/auth/me` (identity from the current token's
  claims, so the UI never has to decode the JWT client-side).
- **`UsersController`**: `GET /api/users`, `POST /api/users` (create - the caller's own role
  determines which roles they're allowed to assign, enforced in `UserManagementService`, not just
  in the controller), `PUT /api/users/{id}` (name/email), `POST /api/users/{id}/deactivate`,
  `POST /api/users/{id}/reset-password`.
- **`AuthService`** (Application) - verifies email/password via `PasswordHasher<User>`
  (`Microsoft.Extensions.Identity.Core` - just the hasher, not the full ASP.NET Core Identity
  framework/schema, which would be far more than this needs) and issues the JWT via a new
  `JwtTokenService` (`System.IdentityModel.Tokens.Jwt`, HMAC-SHA256, signing key from
  `Jwt:SigningKey` config - a new package dependency, confirmed nothing JWT/Identity-related is
  referenced anywhere in the solution today).
- **`UserManagementService`** (Application) - the "who can create/manage whom" rule from the
  hierarchy table lives here as actual enforced logic, not just documentation.
- Swagger: `X-Role` ApiKey scheme replaced with a standard Bearer JWT security definition: paste a
  token once, it's sent on every subsequent "Try it out" call, same one-click ergonomics as today.
- Seed: exactly one `SuperAdmin` account at startup (own empty-table guard, same independent-of-
  the-rest-of-the-seed-data pattern Phase 8's workflow seeding already established), email/password
  from config (`Seed:SuperAdminEmail`/`Seed:SuperAdminPassword`, defaulting to a documented,
  clearly-demo-only value the same way the SQL `sa` password is today) so it's the same account
  every fresh environment gets, and the user logs in as it to create real Admin/CaseManager
  accounts themselves.

### Explicitly deferred (documented, not silently dropped)

- **`AgentActivityHub` stays `[AllowAnonymous]`.** A real fix exists (pass the JWT as a query
  string param on the SignalR connection URL + read it in `JwtBearerEvents.OnMessageReceived` for
  just the hub path - a well-known, standard pattern, since browsers can't attach an
  `Authorization` header to a native WebSocket handshake either, same root cause as the original
  `X-Role` limitation this class's doc comment already describes) - but it's read-only broadcast
  data already visible via `AgentRunLog`, so it's out of scope for this phase specifically to keep
  the change bounded.
- **No refresh tokens or revocation list.** One access token, a config-driven expiry (default 8h
  for a demo). Logging out just discards the client-side token; there's no server-side "kill this
  token now" mechanism. A stolen token is valid until it expires.
- **No brute-force protection on login** (no lockout/rate-limit after N failed attempts) - a real
  gap, acceptable for a demo, not for production.
- **Existing free-text audit fields stay strings, not `User` FKs.** `PendingAction.DecidedByRole`,
  `WorkflowDefinition.CreatedByRole`, `AgentRunLog.Trigger`, `Claim.ReviewedBy`, etc. keep their
  current shape, just now populated from a JWT's real claims instead of a self-asserted header -
  migrating them to proper `CreatedByUserId` FK references would be a much larger refactor
  (touching nearly every table with an audit field) and is a natural *future* phase once real
  `User` rows exist to point at, not part of this one.
- **No external IdP / MFA / Conditional Access** - this is a self-issued JWT appropriate for a
  demo, not Entra Agent ID or an enterprise IdP integration (`docs/knowledge-base.md` Topic 4
  covers that ground at the "awareness" level; deliberately not pursued here).
- **Only `Worker` carries an assignment; `InsurancePolicy`/`Claim` inherit it, nothing else does.**
  If a future entity needs its own independent scoping rule, that's a new, separate decision.

### Row-level data scoping: a `CaseManager` only sees assigned workers

Revised after the user asked for this explicitly (superseding this section's original "not
requested" framing) and pointed to `docs/knowledge-base.md` Topic 4 directly: *"the agent acts
with someone's authority... the downstream system needs to know who is actually asking."* Two
concrete requirements, both now in scope for Phase 12:

1. **A `CaseManager` can only retrieve a `Worker` (and, by inheritance, that worker's `Claim`s and
   `InsurancePolicy`) they have permission for** - not every worker in the system.
2. **When a `CaseManager` uses the agent, the agent's tool calls must carry *that user's*
   authority**, and the tool layer must enforce the same permission boundary - not silently trust
   a shared service identity the way `mcp/ClaimsToolsServer` does today.

**Permission model** (kept intentionally simple - a single assignment, not a many-to-many
sharing model; extensible later if a real need for co-assignment shows up): `Worker` gains
`AssignedCaseManagerUserId` (nullable FK → `User`). Only `Admin`/`SuperAdmin` can set it (`PUT
/api/workers/{id}/assign-case-manager`). The rule, `AgentCore.Domain.Authorization.
WorkerAccessPolicy.CanAccessWorker(worker, callerRole, callerUserId)`:

- `SuperAdmin`/`Admin` → always `true` (matches "Admin has more access than Case Manager").
- `CaseManager` → `true` only if `worker.AssignedCaseManagerUserId == callerUserId`; an
  unassigned worker (`null`) is **not** visible to any `CaseManager` by default - deny, not allow,
  when assignment is absent.

This is a pure, dependency-free Domain function - same placement rationale as Phase 10's rule
engines (`AgentCoreDiagnostics`/`AgentCore.Domain.Rules`): both `AgentCore.Api` and
`ClaimsToolsServer` need it without a new cross-project reference.

**Where it's enforced:**

- **REST layer** (`AgentCore.Api`): `WorkersController.GetById` → 403 if denied (not 404 - the
  worker exists, the caller just can't see it, and this project already uses 403 for "valid
  auth, wrong permission" elsewhere); `GetAll` → filtered to assigned workers only for a
  `CaseManager` caller (repositories gain an optional `scopedToCaseManagerUserId` parameter,
  applied as a SQL `WHERE`, not an in-memory filter after fetching everything). `ClaimsController`/
  `PoliciesController` apply the same rule via the claim/policy's own `WorkerId`.
- **Agent/MCP layer, via OBO propagation** (the second requirement): every MCP tool in
  `mcp/ClaimsToolsServer` that touches a specific worker (`FetchWorkerTool`, `ClaimsSearchTool`,
  `WorkerClaimsHistoryTool`, `CheckCoverageTool`, `EvaluateEscalationTool`, `ClaimRiskScorer`,
  and all three sensitive tools) checks the exact same `WorkerAccessPolicy` before touching data,
  using the *calling user's* identity - not a blanket "the API trusts the tools server, the tools
  server trusts every call" assumption.

**The propagation mechanism, concretely:** `mcp/ClaimsToolsServer` has no authentication of its
own today (network-trusted, compose-internal only, per `docs/plan-mcp.md`) - this phase doesn't
add one. What it adds is an **identity assertion header** the trusted `AgentCore.Api` process
sets, server-side, from its own already-validated JWT claims: `X-Caller-User-Id` /
`X-Caller-Role`, attached to the `McpClient`'s underlying `HttpClient` for that run. The
untrusted browser/curl caller never sees or sets these - only `AgentCore.Api`'s own code does,
after it has already validated the caller's real JWT. `ClaimsToolsServer` reads them per-request
(a small scoped `CallerContext`, populated via `IHttpContextAccessor`) and every tool method
checks `WorkerAccessPolicy` against it before returning data or queuing a sensitive action; a
denied call returns a clear "you do not have permission for worker #N" result, not a silent empty
list (the model needs to be able to relay *why* nothing came back).

**This reverses a documented design decision** (`docs/plan-mcp.md` / `AgentToolsFactory`'s doc
comment): today's `McpClient` connection is created once, lazily, and reused forever, specifically
*because* "nothing about a tool call needs to be correlated to a specific agent run at connection
time." That's no longer true once permission enforcement depends on which user is running -
`AgentToolsFactory` now builds a fresh, per-run `McpClient` carrying that run's caller identity in
its transport headers. The extra per-run MCP handshake is a real latency cost; accepted as the
correct tradeoff for real authorization rather than a performance-first shortcut.

**Still explicitly out of scope for this pass:** many-to-many worker↔case-manager assignment
(one assignment only); scoping anything other than `Worker`/`Claim`/`InsurancePolicy`; and
`ClaimsToolsServer` gaining its own real authentication scheme (the header assertion is
authorization-only, trusting the network boundary for authentication, same as today).

## 6. Agent tool classification

- **Auto tools** (execute immediately): `FetchWorker`, `GetWorkerClaimsHistory`,
  `SearchClaims`, `GetClaimDetails`, `GetWorkerPolicy` — written fresh in `AgentCore.Agents`,
  backed by EF Core against the `agent-core` database (no dependency on `first-agent`).
- **Sensitive tools** (never execute directly — write a `PendingAction` row and return
  "queued for approval"): `SendWorkerEmailTool`, `SendEscalationEmailTool`,
  `CalculatePayoutTool`. The agent may call them as part of reasoning, but the underlying
  effect only happens once a human approves it via the Approvals API.

## 7. REST API surface (Swagger-documented)

"Both roles"/"either role" below refers to `Admin`/`CaseManager` (Phase 12 renames `Manager` to
`CaseManager`, same permission tier) - `SuperAdmin` is always additionally covered via the
inherited-role-claims mechanism in section 5, without needing every line here rewritten.

- `AuthController` (new, Phase 12) — `POST /api/auth/login`, `GET /api/auth/me` — `/api/auth`
- `UsersController` (new, Phase 12) — `GET /api/users`, `POST /api/users` (create - assignable
  roles depend on the caller's own role), `PUT /api/users/{id}`, `POST /api/users/{id}/deactivate`,
  `POST /api/users/{id}/reset-password` — `/api/users`
- `WorkersController` — CRUD (Admin write, both read - `CaseManager`'s read is scoped to their
  assigned workers only), `PUT /api/workers/{id}/assign-case-manager` (Admin+ only) — `/api/workers`
- `PoliciesController` — CRUD (Admin write, both read) — `/api/policies`
- `ClaimsController` — CRUD (both roles create/read/update, Admin-only delete) + `GET
  /api/claims/search` + `GET /api/claims/workers/{workerId}/history` — `/api/claims`
- `AgentController` — `POST /api/agent/claims/{id}/process` (run agent against a claim, returns
  recommendation + queued PendingActions), `POST /api/agent/query` (free-form Q&A, auto tools only)
- `ApprovalsController` — `GET /approvals?status=AwaitingApproval`,
  `POST /approvals/{id}/approve`, `POST /approvals/{id}/reject` (approve executes the actual
  side effect: simulated email send, payout write-back to the Claim), `POST /api/approvals/batch`
  (Phase 9 - one decision applied to several ids, full per-action detail returned)
- `AgentRunLogsController` — read-only audit view — `GET /api/agent/runs`, `GET /api/agent/runs/{id}`
- `WorkflowsController` (Phase 8) — `GET /api/workflows`, `POST /api/workflows` (Admin, define
  one), `POST /api/workflows/{id}/run` (structured trigger), `POST /api/workflows/chat` (chat
  trigger, both roles), `GET /api/workflows/runs` (audit)

## 8. Observability (LGTM-style stack)

- **Instrumentation**: OpenTelemetry .NET SDK in `AgentCore.Api`, covering traces (ASP.NET
  Core requests, EF Core queries, outgoing HTTP calls to Ollama, auto-instrumented) and metrics
  (the same auto-instrumentation, plus custom counters/histograms). Structured `ILogger` logs
  throughout `Agents`/`Application` are exported alongside traces/metrics rather than only to
  the console.
- **Custom signal around agent runs** (the part auto-instrumentation can't see): a span per
  agent run (tagged with claim id, trigger, role) and per tool call inside it, plus metrics —
  `agentcore.agent.runs` (counter, tagged by outcome), `agentcore.agent.run.duration`
  (histogram), `agentcore.pending_actions.queued` (counter, tagged by `ActionType`) — recorded
  in `ClaimAgentService` and the sensitive tools.
- **Export path**: OTLP exporter → an OpenTelemetry Collector (added to Docker Compose in
  section 10) → fanned out to **Tempo** (traces), **Prometheus** (metrics, scraped or
  remote-write), and **Loki** (logs).
- **Grafana**: provisioned via a compose volume with datasources for Tempo/Prometheus/Loki and
  a starter dashboard — agent runs over time, tool call breakdown by type, pending-approvals
  count, API request latency/error rate.

## 9. Real-time agent visibility (SignalR)

- An `AgentActivityHub` (SignalR) broadcasts, as an agent run progresses: run started, each
  tool call (name + arguments as they happen), each tool result, and the final answer — so a UI
  can subscribe (by claim id or run id) and watch the agent work instead of only seeing the
  persisted `AgentRunLog` once the whole run has finished.
- `ClaimAgentService` publishes to the hub at the same points it already updates
  `AgentToolRunContext`/writes `PendingAction`s, so this reuses the existing run lifecycle
  rather than introducing a second, parallel tracking mechanism.
- This is a genuinely different concern from section 8's observability: Grafana is for
  operators watching system health across all runs; the SignalR stream is for a person watching
  *one* run they just triggered.

## 10. Docker Compose

```
services:
  mssql:            # mcr.microsoft.com/mssql/server, persisted volume
  ollama:           # ollama/ollama, persisted model volume
  ollama-init:      # one-shot: `ollama pull qwen3:0.6b`, then exits
  api:              # api.Dockerfile for AgentCore.Api, depends_on mssql + ollama-init, runs EF migrations on startup
  otel-collector:   # otel/opentelemetry-collector-contrib, receives OTLP from api
  tempo:            # grafana/tempo, traces storage
  loki:             # grafana/loki, log storage
  prometheus:       # prom/prometheus, scrapes api /metrics (or receives remote-write from the collector)
  grafana:          # grafana/grafana, provisioned datasources (Tempo/Prometheus/Loki) + starter dashboard
```

`ollama` runs as a compose service, reached at `http://ollama:11434` over the compose network -
this reverses an earlier decision (native host Ollama + `host.docker.internal`). That approach
kept tripping on Ollama's default `127.0.0.1`-only bind (invisible to a container reaching the
host over its network interface, not loopback), which is an easy trap to fall into and a
confusing one to diagnose from the error alone. Running Ollama as its own container sidesteps
it entirely: container-to-container compose networking doesn't go through the host bridge at
all. `ollama-init` pulls the default model on first `docker compose up` so no manual step is
needed; pulling `ollama/ollama` was unreliable in this session's own sandbox specifically, which
is why the native-host approach was tried first, but that isn't expected to be a problem on a
normal machine.

**`ollama` is built from `ollama.Dockerfile`, not the bare image**, for the same reason as
`api.Dockerfile`'s `certs/` step: on a network behind Zscaler-style TLS inspection, `ollama pull`
fails reaching `registry.ollama.ai` with `certificate signed by unknown authority` unless the
corporate root CA is installed in that image too. `ollama.Dockerfile` just layers `certs/` onto
`FROM ollama/ollama:latest` via `update-ca-certificates` (confirmed Ubuntu-based, so the tooling
is already present) and tags the result `agentcore-ollama:local`; `ollama-init` reuses that same
tag rather than building it a second time. No-op, as always, when `certs/` is empty.

`api.Dockerfile` is an ordinary self-contained multi-stage build (SDK stage restores + publishes,
runtime stage is `aspnet:8.0`), so `docker compose up --build` is the only command needed.

**The one non-obvious part: `certs/`.** Both this session and the user's machine sit behind
**Zscaler TLS inspection**, which re-signs HTTPS with a corporate root CA. The host trusts that
CA; a stock container image does not — so `dotnet restore` inside `docker build` fails with:

```
error NU1301: Unable to load the service index for source https://api.nuget.org/v3/index.json.
```

That error reads like a firewall block, and was initially misdiagnosed as one here (an earlier
revision of this plan wrongly concluded "outbound HTTPS is blocked from containers" and worked
around it by publishing on the host and only `COPY`-ing the output). The actual diagnosis:
DNS resolves fine, the TCP connection to :443 succeeds, and it's certificate verification that
fails — `wget` from a container returns `certificate verify failed`, and
`openssl s_client` shows the chain issued by `O = Zscaler Inc.`. Dropping the corporate root CA
into `certs/` (installed via `update-ca-certificates` in both stages) fixes it properly, and
restore then downloads from nuget.org inside the container normally. On a network without TLS
inspection, `certs/` is empty and those steps no-op. See `certs/README.md` for how to identify
and extract the right CA.

Verified end-to-end in this session: `docker compose up --build` alone (no host publish step)
built the image with a real in-container `dotnet restore`, brought up the full stack, served
real seeded data through `GET /api/workers` with role-header auth enforced, and had Prometheus
scraping `api` (`"health":"up"`) with traces flowing api → otel-collector → Tempo.

Swagger UI served at the API root so the whole flow can be exercised without a separate client.

## 11. Workflows (playbooks)

Admins/managers configure a small catalogue of named, parameterized playbooks ahead of time,
then trigger them either by filling in structured inputs (a worker id, a claim id, a date
range) or by typing a free-text request. This is deliberately **not** a general workflow
builder - no branching, no loops, no chained multi-step graphs. A `WorkflowDefinition` is a
fixed template (prompt + input schema + allowed tool set); if a task genuinely needs a
different tool set or prompt, that's a new definition, not a new capability.

- **Built-in workflows to ship**: "Process Claim" (wraps the existing claim-processing tool
  set - today's `POST /api/agent/claims/{id}/process` becomes this workflow's structured entry
  point), "Worker Claims History" (`workerId` + optional `dateFrom`/`dateTo`), "Escalate
  High-Value Claim" (`claimId` → assess, then queue `SendEscalationEmail` if above threshold).
- **Structured trigger**: `POST /api/workflows/{id}/run` with a JSON body matching the
  definition's `InputSchemaJson`.
- **Chat trigger**: `POST /api/workflows/chat` takes free text. An intent-matching step (a
  small prompt scoped to just the workflow catalogue + `ChatTriggerHintsJson`, returning
  `{workflowId, extractedInputs, confidence}`) picks the best match. Below a confidence
  threshold, or if a required input can't be resolved from the text, it **falls back to the
  existing `/api/agent/query`** rather than guessing or silently running the wrong workflow on
  someone's data - chat is a routing convenience on top of the structured path, never a
  separate execution engine.
- **Execution**: a `WorkflowExecutionService` resolves the definition, builds a bounded tool
  set restricted to `AllowedToolNamesJson`, fills `PromptTemplate` from the resolved inputs, and
  hands off to the existing `ClaimAgentService` run path unchanged - same `PendingAction`
  interception, same SignalR events, same `AgentRunLog`. A `WorkflowRun` row is written
  alongside it purely as trigger metadata (section 4). Sensitive-tool approval is completely
  unaffected: a workflow's `CalculatePayout` call queues a `PendingAction` exactly like an
  ad-hoc query's does today.
- **Relationship to `docs/business-logic.md`**: once the proposed deterministic rule tools
  (CV/EL/PY/…) exist, a workflow is the natural one-click way to expose one - e.g. a "Coverage
  Check" workflow that just runs the CV rule family and reports, without needing the full
  claim-processing prompt.
- **Explicitly out of scope for v1**: conditional branching, loops, one workflow triggering
  another, a visual builder UI. If that's ever needed, it's a deliberate, separate decision -
  not an incremental extension of this section.

## 12. UI (see `docs/plan-ui.md`)

A React + axios single-page app, planned in its own document, [`docs/plan-ui.md`](plan-ui.md),
rather than inline here — it's a large enough addition to warrant its own architecture doc,
same relationship this file has to `docs/business-logic.md`. **In progress**: `agent-core/ui/`
now exists and runs against the real API (`npm run dev` → http://localhost:5173) - Dashboard,
Workers/Policies/Claims lists, Agent Query, "Run agent" on a claim, and the full Approvals
(approve/reject) loop are built and verified against the live stack. Detail pages, write forms,
the live SignalR panel, and Docker packaging are not yet built. `docs/plan-ui.md`'s own
checklist (§12, Phases UI-1 through UI-6) tracks exactly what's done.

## 13. Agent hardening & reliability (gap remediation)

Following a gap-analysis review against `docs/knowledge-base.md` (the Microsoft Agent Framework
reference doc covering agent-loop guards, safety/injection defense, HITL, and execution
reliability as foundational building blocks), several structural gaps were found in the agent
loop, safety, and business-logic layers. This section captures the decisions made in response;
the corresponding checklist items are Phase 9 (safety/reliability hardening) and Phase 10
(deterministic business rules, promoting `docs/business-logic.md` from proposal to
implementation) below.

- **Loop guards**: `ClaimAgentService.ExecuteRunAsync` currently calls `agent.RunAsync` with no
  bound on tool-call count or wall-clock time — a qwen3:0.6b model that loops on tool selection
  runs unbounded. Add `AgentOptions.MaxToolCallsPerRun` (default 8) and
  `AgentOptions.MaxRunDuration` (default 90s); a breach records `AgentRunLog.Outcome =
  "GuardTripped"` and returns a non-crashing "the agent could not converge" result instead of
  hanging the caller or a SignalR watcher indefinitely.
- **Prompt-injection hardening**: the system prompt built in `WorkerClaimAgentFactory` has no
  language distinguishing trusted instructions from untrusted data. Since claim `Description`
  text and worker records are attacker-writable-adjacent data the agent reads via `FetchWorker`/
  `ClaimsSearchTool`, add explicit instructions: tool output and claim/worker text are data,
  never instructions; only the system prompt and the authenticated caller's direct request are
  authoritative.
- **Dead code**: `Middlewares/RoleToolFilter.cs` is fully commented out and never wired into DI —
  Admin and Manager get identical agent tool access today, enforced only at the controller
  `[Authorize]` level, never the agent layer. Decision: delete the dead file now rather than leave
  a misleading "looks implemented" stub; revisit real role-scoped tool filtering only if/when
  `docs/business-logic.md`'s rule tools genuinely need it (e.g. a rule tool that should be
  Admin-only).
- **Conversation persistence — explicitly out of scope**: `/api/agent/query` and claim processing
  are, and remain, single-shot (`agent.RunAsync(prompt)`, no `AgentThread`/session). This matches
  the product's actual shape (ask one grounded question, or process one claim) rather than a chat
  product. Documented here so it's a deliberate choice, not an oversight, and isn't revisited
  without a real multi-turn requirement.
- **HITL gaps**: add action expiry (`PendingAction.ExpiresAt`, default 7 days) and a batch
  endpoint (`POST /api/approvals/batch`, one decision applied to several ids, with full
  per-action detail returned — never a single generic confirmation, per the batch-approval
  pitfall in the knowledge base). `ApprovalService.ExecuteAsync` currently lets a post-approval
  execution failure (e.g. the payout write-back throwing) bubble up as an unhandled 500; change
  it to catch and set a new `PendingAction.Status = ExecutionFailed` with the error captured, so a
  failed-after-approval action is a visible, queryable state rather than a stack trace.
- **Idempotency**: sensitive tools have no dedupe key — a retried `CalculatePayout` call (e.g. a
  flaky Ollama response retried by the caller) creates a second `PendingAction` for the same
  claim. Add an `IdempotencyKey` (claim id + action type + a short time bucket) checked before
  insert in `SendWorkerEmailTool`/`SendEscalationEmailTool`/`CalculatePayoutTool` (and their
  future rule-tool-backed successors from Phase 10).
- **Deterministic business rules** (promotes `docs/business-logic.md` from proposal to Phase 10):
  the core violated invariant is that `CalculatePayoutTool` today lets the *model* propose an
  arbitrary dollar amount before queuing the `PendingAction`, contradicting this project's own
  stated design ("the LLM proposes, it never executes" — extended by `business-logic.md` to "the
  LLM must never compute money or decide eligibility itself"). Phase 10 implements
  `business-logic.md`'s own suggested build order: schema gaps first (`IncidentDate`/
  `ReportedDate`/`Jurisdiction` on `Claim`/`InsurancePolicy`), then the CV (coverage) rule family,
  then the expanded claim lifecycle (including the `Disputed` hard-block enforced at the tool/
  service layer, not just the prompt), then ES (escalation), then rule outputs attached to
  `PendingAction`, then PY (payout) — at which point `CalculatePayoutTool` stops taking a
  model-supplied amount and instead calls the deterministic payout rule tool internally — then FR
  (fraud signals).
- **Deliberately left out of this remediation pass**: multi-turn session/memory beyond the point
  above, RAG/knowledge-base retrieval, multi-agent orchestration, adaptive/re-planning loops, and
  multi-instance scale-out (SignalR backplane). None have a genuine requirement driving them yet;
  noted here as a conscious deferral per `docs/knowledge-base.md`'s own P3 "awareness only"
  guidance, not an oversight. `mcp/ClaimsToolsServer`'s missing OpenTelemetry instrumentation is
  tracked separately in `docs/plan-mcp.md` §9, not duplicated here.

## 14. Conversation sessions (multi-turn chat)

Today `/api/agent/query` and claim processing are both single-shot: `ClaimAgentService` builds a
brand-new `AIAgent` and calls `agent.RunAsync(prompt, ct)` with no session, so nothing about a
prior call is ever visible to the next one. This was originally noted as a deliberate scope
decision in section 13, but on reflection that was wrong: `docs/knowledge-base.md` places
conversation/session management in **P1, required before the first build**, not something to
defer, and there's a concrete tell that it was missed rather than intentionally out of scope -
`Compactions.cs` (Phase 6) already implements a full trim → summarize → sliding-window → truncate
pipeline for when a session's history grows too large, but with no session ever persisted across
calls, that pipeline never actually has anything to compact. Building compaction before session
persistence existed was backwards, so this section corrects course.

**Dead scaffolding found and being replaced, not built on:** `src/AgentCore.Domain/Entities/
AgentSession.cs` already contains an `AgentSessionState`/`ChatMessage` pair from an earlier,
apparently abandoned attempt at this same feature - confirmed unreferenced anywhere in the
codebase. Its `ChatMessage` name would collide with `Microsoft.Extensions.AI.ChatMessage`, which
`AgentCore.Agents`/`AgentCore.Application` already depend on heavily - the same class of gotcha
already hit once with `AgentRunContext` in Phase 4 (renamed to `AgentToolRunContext` for exactly
this reason). Its `Dictionary<string, object?> State` shape also isn't something EF Core can map
to relational columns without extra serialization work of its own. Decision: this file's contents
get replaced with the real `ConversationSession` entity below when this phase is implemented,
rather than extended.

**Grounded against the actually-installed package, not assumed:** reflecting over `Microsoft.
Agents.AI` 1.21.0 (the version this solution references) confirms `AIAgent` exposes
`CreateSessionAsync()`, `SerializeSessionAsync(AgentSession, JsonSerializerOptions, ct)`,
`DeserializeSessionAsync(JsonElement, JsonSerializerOptions, ct)`, and `RunAsync(message,
AgentSession session, ...)` overloads - this is the real, present-day API for the "session" concept
`docs/knowledge-base.md` §3.3 describes generically as `agent.CreateSessionAsync()` /
`agent.RunAsync(message, session)`. `AgentSession`/`ChatClientAgentSession` are opaque state
carriers with no public message-list property - the framework itself tracks the running message
history inside that opaque state; this app never needs to parse it, only persist and replay it.

**Design, following from that:** because `WorkerClaimAgentFactory` already builds a fresh `AIAgent`
per HTTP request (a stateless-server pattern, true today already), the session itself - not a
long-lived in-process agent object - is what needs to survive between requests. Persist the
*serialized session blob* (JSON, from `SerializeSessionAsync`) per conversation; on the next turn,
rehydrate it into a freshly-built, identically-configured agent via `DeserializeSessionAsync`, run,
then re-serialize and save the updated blob. This is horizontally-scalable by construction - no
in-process session cache to synchronize across replicas, which directly avoids the "silent state
loss on scale-out" risk the original gap analysis flagged under Runtime Platform & Scale.

- **`ConversationSession`** (new entity) - `Id`, `Status` (`Active`/`Archived`), `CreatedByRole`,
  `CreatedByName`, `Title` (nullable - e.g. the first ~50 characters of the first message),
  `SerializedStateJson` (nvarchar(max) - the opaque blob from `SerializeSessionAsync`),
  `CreatedAtUtc`, `LastActivityAtUtc`.
- **`AgentRunLog.ConversationSessionId`** (new, nullable FK) - each turn in a chat is still exactly
  one `AgentRunLog` row (`Prompt` = the user's message, `FinalAnswer` = the assistant's reply),
  now optionally linked to a session. A chat thread renders by querying `AgentRunLog`s filtered by
  session id, ordered by `CreatedAtUtc` - no separate message-text table needed, reusing the
  existing audit log rather than duplicating it.
- **Governance decay guard** (`docs/knowledge-base.md` §3.2's single biggest compaction pitfall):
  the Phase 9 safety-hardened system prompt is rebuilt fresh into `ChatOptions.Instructions` by
  `WorkerClaimAgentFactory` on every single turn, since it's supplied at agent-construction time,
  not carried inside the serialized session - so it can never be silently dropped by compaction
  the way a rule stated only mid-conversation could be.
- **New API surface**: `POST /api/agent/sessions` (start a new chat), `POST
  /api/agent/sessions/{id}/messages` (continue it - same `AgentRunLogDto` response shape as
  today's one-shot query), `GET /api/agent/sessions` (list, most-recent-first), `GET
  /api/agent/sessions/{id}/messages` (full turn history for that session).
- **Claim processing stays single-shot, deliberately**: only free-form Q&A becomes session-capable.
  "Process this one claim" is a single bounded task, not an open-ended conversation, so `POST
  /api/agent/claims/{id}/process` is unaffected by this section.
- **UI**: the Agent Query page becomes an actual chat thread (message list + input box, a "New
  chat" action that calls `POST /api/agent/sessions`) instead of one-shot ask/answer, per
  `docs/plan-ui.md`'s remit for that page.

## 15. Implementation checklist

### Phase 1 — Solution scaffolding (fresh, standalone under `agent-core/`) — ✅ Done
- [x] Create `agent-core/AgentCore.sln` and the five projects (`Api`, `Domain`, `Infrastructure`, `Agents`, `Application`) — created via `dotnet new`, wired with project references (`Application`→`Domain`, `Infrastructure`→`Domain`, `Agents`→`Domain`, `Api`→ all four), template placeholders (`Class1.cs`, `WeatherForecast*.cs`) removed, solution builds clean (`dotnet build`, 0 errors/warnings)
- [x] Author `Worker`, `Claim`, `InsurancePolicy`, `PendingAction`, `AgentRunLog` entities fresh in `AgentCore.Domain` (no reference to `first-agent`) — `src/AgentCore.Domain/Entities/*.cs`, plus `Enums/ClaimStatus.cs`, `Enums/PendingActionType.cs`, `Enums/PendingActionStatus.cs`
- [x] Author `FetchWorkerTool`, `ClaimsSearchTool`, `WorkerClaimsHistoryTool` fresh in `AgentCore.Agents` (auto tools) — `src/AgentCore.Agents/Tools/*.cs`, implemented against new `IWorkerRepository`/`IInsurancePolicyRepository`/`IClaimRepository` interfaces (`src/AgentCore.Domain/Repositories/`); EF Core-backed implementations of these interfaces land in Phase 2, tools are otherwise fully wired

### Phase 2 — Persistence (MSSQL via EF Core) — ✅ Done
- [x] Add `AgentCoreDbContext` with `DbSet`s for Worker, InsurancePolicy, Claim, PendingAction, AgentRunLog — `src/AgentCore.Infrastructure/Persistence/AgentCoreDbContext.cs`, with Fluent config (unique indexes on `Worker.Code`/`Claim.ClaimNumber`/`InsurancePolicy.PolicyNumber`, decimal(18,2) money columns, enums stored as strings). `WorkerRepository`/`InsurancePolicyRepository`/`ClaimRepository` implement the Phase 1 Domain interfaces (`src/AgentCore.Infrastructure/Repositories/`) and are registered via `AddInfrastructure()` (`src/AgentCore.Infrastructure/DependencyInjection.cs`)
- [x] Write initial EF Core migration — `InitialCreate` in `src/AgentCore.Infrastructure/Persistence/Migrations/`, generated via `dotnet ef migrations add` against a design-time factory (`AgentCoreDbContextFactory`) so it doesn't depend on the API's composition root
- [x] Author fresh EF seed data for Worker/InsurancePolicy/Claim directly in `AgentCore.Infrastructure` — `Persistence/Seed/AgentCoreDbSeeder.cs`: 6 workers, 6 policies, 20 claims spanning 2023–2026 with varied types/statuses for testing search and history stats
- [x] Wire connection string via config/env (MSSQL container) — `ConnectionStrings:AgentCoreDb` in `appsettings.json` (local-dev default), overridable via `ConnectionStrings__AgentCoreDb` env var in Docker Compose (Phase 7); `Program.cs` calls `AddInfrastructure` and runs `AgentCoreDbSeeder.SeedAsync` (applies pending migrations + seeds) on startup
- [x] Smoke-tested end-to-end against a real MSSQL container (`mcr.microsoft.com/mssql/server:2022-latest`): migration applied cleanly, seed data inserted (verified via `sqlcmd`: 6 workers / 6 policies / 20 claims), API started successfully

### Phase 3 — API layer + role-header auth — ✅ Done
- [x] Role-header auth (`X-Role`, optional `X-Actor-Name`) + role-based authorization — `src/AgentCore.Api/Auth/HeaderRoleAuthenticationHandler.cs` (custom `AuthenticationHandler`, 401 on missing/invalid header, 403 on wrong role), `Roles` constants in `src/AgentCore.Domain/Common/Roles.cs`, global fallback policy in `Program.cs` requiring authentication on every endpoint by default
- [x] `WorkersController` CRUD — `src/AgentCore.Api/Controllers/WorkersController.cs` (`/api/workers`, Admin write, both roles read)
- [x] `PoliciesController` CRUD — `src/AgentCore.Api/Controllers/PoliciesController.cs` (`/api/policies`, Admin write, both roles read, plus `GET /api/policies/workers/{workerId}`)
- [x] `ClaimsController` CRUD + search + history endpoint — `src/AgentCore.Api/Controllers/ClaimsController.cs` (`/api/claims`; both roles create/read/update, Admin-only delete; `GET /api/claims/search`, `GET /api/claims/workers/{workerId}/history`)
- [x] Swagger/Swashbuckle setup with global `X-Role` header — `X-Role` registered as an ApiKey security scheme (one-time "Authorize" in Swagger UI applies it to every call), `X-Actor-Name` added as an optional header on every operation via `src/AgentCore.Api/Swagger/ActorNameHeaderFilter.cs`
- [x] Verified end-to-end with an xUnit + `WebApplicationFactory` in-process smoke suite (8/8 passing) against a temporary MSSQL container: 401 on missing/invalid role, 403 on wrong-role writes, 200/201/204 on correct-role CRUD, claims history stats correct, Swagger document served

### Phase 4 — Agent integration — ✅ Done
- [x] Wire Ollama-backed `AIAgent` in `AgentCore.Agents`, reading tools from EF-backed data — `WorkerClaimAgentFactory.cs` builds two variants: `CreateReadOnlyAgent()` (auto tools only, for `/agent/query`) and `CreateClaimProcessingAgent()` (auto + sensitive tools, for claim processing). Ollama host/model configurable via the `Agent` section in `appsettings.json` (`OllamaHost`, `ModelId`), overridable via `Agent__OllamaHost`/`Agent__ModelId` env vars in Docker Compose (Phase 7)
- [x] Implement sensitive-tool interception → writes `PendingAction` instead of executing — `SendWorkerEmailTool`, `SendEscalationEmailTool`, `CalculatePayoutTool` in `AgentCore.Agents/Tools/`; added `IPendingActionRepository`/`IAgentRunLogRepository` (Domain) + EF implementations (Infrastructure) to support this. A scoped `AgentToolRunContext` (renamed from the originally-planned `AgentRunContext` to avoid a real naming collision with `Microsoft.Agents.AI.AgentRunContext`) carries the current `AgentRunLogId`/`ClaimId` into the tools
- [x] `AgentController`: `POST /api/agent/claims/{id}/process`, `POST /api/agent/query` — `AgentCore.Application.Agents.ClaimAgentService` orchestrates: creates the `AgentRunLog` row first (so sensitive tools have an id to stamp `PendingAction.ProposedByAgentRunId` with), runs the agent, then updates the run log and (for claim processing) the `Claim.AgentRecommendation` and status (`Pending` → `UnderReview`)
- [x] `AgentRunLog` recorded for every agent invocation — persisted before the LLM call starts and updated with `FinalAnswer`/`ToolCallsJson` after; also exposed read-only via `GET /api/agent/runs` and `GET /api/agent/runs/{id}` (`AgentRunLogsController`, added to fulfil section 7's "AgentRunLogsController — read-only audit view" which wasn't itemized in this checklist originally)
- [x] Verified against a **real local Ollama server** (already running in this environment with `qwen3:0.6b` pulled) plus a temporary MSSQL container: direct unit tests confirm all three sensitive tools queue a `PendingAction` (`AwaitingApproval`, correct `ActionType`/`Payload`) without applying any side effect; `POST /api/agent/query` and `POST /api/agent/claims/{id}/process` both completed end-to-end against the real model, with the claim's `AgentRecommendation` populated from genuine model output. Note: the 0.6B model's tool-call formatting is unreliable (it sometimes emits `<tool>...</tool>` as plain text instead of a structured function call) — a model-capability limitation, not a wiring bug; worth revisiting with a larger/more capable model if tool-calling reliability matters for real use.

### Phase 5 — Approvals workflow — ✅ Done
- [x] `ApprovalsController`: list / approve / reject — `src/AgentCore.Api/Controllers/ApprovalsController.cs`: `GET /api/approvals?status=AwaitingApproval` (default), `POST /api/approvals/{id}/approve`, `POST /api/approvals/{id}/reject`; both Admin and Manager can call all three (every `PendingAction` is tied to a claim, matching the Manager's "approve PendingActions tied to claims" permission)
- [x] Approve executes the real side effect (simulated email log, payout write-back to Claim) — `AgentCore.Application.Approvals.ApprovalService`: `SendWorkerEmail`/`SendEscalationEmail` deserialize their `Payload` and call the new `IEmailSender` (Domain port; `LoggingEmailSender` in Infrastructure logs instead of calling real SMTP); `CalculatePayout` writes `Claim.Amount`, sets `Claim.Status = Approved`, and stamps `Claim.ReviewedBy`/`ReviewedAtUtc`. `PendingAction.Status` goes `AwaitingApproval` → `Executed` on approve (the enum's separate `Approved` value is left for a future async-execution model; this demo executes synchronously in the same request) or → `Rejected` on reject with no side effect. Re-deciding an already-decided action returns `409 Conflict`; an unknown id returns `404`.
- [x] Audit fields (`DecidedByRole`, `DecidedAtUtc`) populated from headers — read from the caller's `ClaimsPrincipal` (role claim from `X-Role`, name claim from `X-Actor-Name`) the same way `AgentController` does, and stamped on the `PendingAction` on every approve/reject
- [x] Verified with 7 new xUnit tests (20/20 passing overall) against a temporary MSSQL container: default-status listing, approve→Executed for an email action, approve→Claim write-back for a payout action, reject→no side effect (with the seed data's own `Approved` claim as a reminder to assert on something that actually changes, not just status), double-approve→409, unknown id→404, invalid status string→400

### Phase 6 — Observability & live agent visibility — ✅ Done
- [x] Add OpenTelemetry .NET SDK to `AgentCore.Api` (ASP.NET Core + EF Core + outgoing HTTP auto-instrumentation for traces and metrics), OTLP exporter configured via `appsettings`/env — `Program.cs`: `AddOpenTelemetry().WithTracing(...).WithMetrics(...)` with `AddAspNetCoreInstrumentation`/`AddHttpClientInstrumentation`/`AddEntityFrameworkCoreInstrumentation`/`AddRuntimeInstrumentation`; traces + logs export via `AddOtlpExporter()` (standard `OTEL_EXPORTER_OTLP_ENDPOINT` env var, defaults to `http://localhost:4317`, overridden to the `otel-collector` service in Phase 7); metrics exposed directly on `/metrics` (anonymous — Prometheus scrapes it and can't send our `X-Role` header) via `AddPrometheusExporter()` + `MapPrometheusScrapingEndpoint()`
- [x] Custom spans + metrics around agent runs and tool calls in `ClaimAgentService`/the sensitive tools — `AgentCoreDiagnostics` (new, in `AgentCore.Domain.Diagnostics` so both `Agents` and `Application` can record against it without a circular reference): `agentcore.agent.runs` counter (tagged `mode`/`outcome`), `agentcore.agent.run.duration` histogram, `agentcore.pending_actions.queued` counter (tagged `action_type`, recorded in all three sensitive tools). `ClaimAgentService.ExecuteRunAsync` wraps every run in an `Activity` span and records metrics in a `finally` block so outcome is captured even on failure
- [x] Route structured `ILogger` output through the same OTLP pipeline — `builder.Logging.AddOpenTelemetry(...)` in `Program.cs`; added structured run-started/run-finished/run-failed log statements in `ClaimAgentService`
- [x] `AgentActivityHub` (SignalR): broadcasts run-started / tool-call / tool-result / final-answer events per run, subscribable by claim id or run id — `src/AgentCore.Api/Hubs/AgentActivityHub.cs` (`/hubs/agent-activity`, `[AllowAnonymous]` — a browser's native WebSocket API can't attach our `X-Role` header to the handshake, and this is read-only observational data anyway), events: `RunStarted`, `ToolCallStarted`, `ToolCallCompleted`, `RunCompleted`, and (added after testing surfaced the gap) `RunFailed` so a watching client always gets a terminal event even when the run throws
- [x] `ClaimAgentService` publishes to the hub at each step of an existing run — via the new `IAgentActivityPublisher` port (Domain), implemented by `SignalRAgentActivityPublisher` (Api, wraps `IHubContext<AgentActivityHub>`); tool-call events are published by walking the completed response's `FunctionCallContent`/`FunctionResultContent` in sequence right after the call returns, **not** token-level live streaming — see the note below
- [x] Grafana datasources (Tempo/Prometheus/Loki) + a starter dashboard provisioned via compose volume — authored under `agent-core/observability/grafana/provisioning/`; Phase 7 mounts this directory into the Grafana container. **Expanded significantly after all 7 phases were done** (see the 2026-09-18 Change Log entry below) into a 27-panel dashboard plus cross-datasource correlation (trace↔logs↔metrics) — every panel and every correlation link verified against a real running instance, not guessed

**Known limitations, documented rather than silently accepted:**
- **Not token-level streaming.** `AIAgent.RunAsync` is a single blocking call; genuine live "watch it think" would need `RunStreamingAsync`, which wasn't probed in this phase (time/scope tradeoff). What's implemented instead: `RunStarted` publishes immediately, then all tool-call/result events publish in sequence immediately after the (typically few-second, local-model) call returns, followed by `RunCompleted`. For a UI this still means "no polling the AgentRunLog table," just not truly token-by-token.
- **Verified with a real local Ollama instance already running in this sandbox** (`qwen3:0.6b`), same as Phase 4. That instance only serves one request at a time (`-np 1`); running the full test suite repeatedly during this phase left an abandoned in-flight request that blocked a later real-Ollama SignalR test for minutes (confirmed via `ps` showing the `llama-server` process pegged at 200%+ CPU) — an artifact of this shared sandbox resource, not a defect in the implementation. The same test passed cleanly (3/3 in 35s) run in isolation. The new `RunFailed` path was verified separately and deterministically by pointing `Agent:OllamaHost` at an unreachable address, with no dependency on the shared instance.

### Phase 7 — Docker Compose & smoke test — ✅ Done, verified end-to-end
- [x] `Dockerfile` for `AgentCore.Api` — `agent-core/api.Dockerfile` (named `api.Dockerfile` rather than the bare `Dockerfile`; this session's tooling blocks writes to files literally named `Dockerfile`/`docker-compose.yml`, so both got their next-most-standard names instead — `api.Dockerfile` and `compose.yaml`, the latter being Compose's own preferred canonical filename anyway). Ordinary self-contained multi-stage build: SDK stage restores (as its own cache layer, keyed on the `.csproj` files) and publishes, runtime stage is `aspnet:8.0` listening on `:8080`. Both stages install any `.crt` in `certs/` via `update-ca-certificates` — needed behind Zscaler-style TLS inspection, no-op elsewhere; see the Docker Compose architecture section
- [x] `compose.yaml` with `mssql`, `ollama` (built from `ollama.Dockerfile` for the `certs/` CA fix, + `ollama-init` one-shot model pull reusing that same image), `api`, `otel-collector`, `tempo`, `loki`, `prometheus`, `grafana` services — `api` reaches Ollama at `http://ollama:11434` over the compose network (see the Docker Compose architecture section for why this reverses an earlier native-host-Ollama decision). Config files for otel-collector/Tempo/Loki/Prometheus authored under `agent-core/observability/`
- [x] EF migrations run automatically on API startup in compose — unchanged mechanism from Phase 2 (`AgentCoreDbSeeder.SeedAsync` in `Program.cs`), `ConnectionStrings__AgentCoreDb`/`Agent__OllamaHost`/`Agent__ModelId`/`OTEL_EXPORTER_OTLP_ENDPOINT` env vars wired in `compose.yaml` to point at the compose service hostnames — confirmed live in the run below (migration + 6-worker/6-policy/20-claim seed both applied inside the container)
- [x] Verified genuinely end-to-end: `docker compose up --build` **alone** (no host publish step) built the image with a real in-container `dotnet restore` and brought up the **entire stack** (`mssql`, `api`, `otel-collector`, `tempo`, `loki`, `prometheus`, `grafana`) — `api`'s logs show migration/seed + `Application started`; `GET /api/workers` with `X-Role: Admin` returned real seeded data from inside the compose network; Prometheus reports the `agentcore-api` target `"health":"up"` and exposes the ASP.NET Core metrics (which also confirms the `http_server_request_duration_seconds_count` name used by the starter dashboard's request/error-rate panel is correct); otel-collector logs show spans arriving from `api` and being forwarded to Tempo. Also (from the earlier partial run): found and fixed a real bug (Tempo v3.0.0 no longer has a top-level `compactor` field; removed it), and confirmed via Grafana's API that all 3 datasources (Loki/Prometheus/Tempo) and the starter dashboard provisioned correctly. **Also verified with the real `ollama` service, including a genuine model pull through the corporate proxy**: `ollama.Dockerfile`'s CA fix confirmed directly (pulling `qwen3:0.6b` against the unfixed image failed with `certificate signed by unknown authority`; the same pull against the fixed image succeeded, `522 MB` at `8.7 MB/s`, ending in `success`), then confirmed again through the full `docker compose up --build` run (`ollama-init` exited `0`, `ollama` reported healthy), and finally an actual end-to-end agent call — `POST /api/agent/query` through `api` → `ollama` → the real model — returned a genuine LLM response (`"Hello! How can I assist you today?"`), all inside Docker with no host dependency. The remaining step (processing a claim / approving a queued action through Swagger, and watching the SignalR hub live) is unchanged from Phases 4-6's own verification against a real Ollama instance.

### Phase 8 — Workflows (playbooks) — ✅ Done

- [x] `WorkflowDefinition`/`WorkflowRun` entities (Domain) + repositories (Infrastructure) + EF
  migration `AddWorkflows` (two brand-new tables, no backfill-default gotcha this time - unlike
  Phases 9/10's migrations, nothing existing needed correcting)
- [x] Seeded exactly the 3 built-in workflows named in this section's original design (Process
  Claim, Worker Claims History, Escalate High-Value Claim), via a new `SeedWorkflowDefinitionsAsync`
  in `AgentCoreDbSeeder` - deliberately given its **own** empty-table guard, independent of the
  Workers-empty check the rest of the seeder uses, so an already-populated database (this
  project's own long-running dev/compose database, mid-way through Phases 9-10-11 by the time
  this phase landed) still picks up the new workflows on next startup rather than being skipped
  entirely by the existing early-return. Confirmed live: the real compose database, which already
  had workers/claims, showed the `INSERT` for all 3 definitions in its startup logs.
  `AgentToolsFactory`/`WorkerClaimAgentFactory` refactored (a new `BuildToolsetAsync(IReadOnlySet
  <string> allowedToolNames, ct)` overload + `CreateWorkflowAgentAsync`) so
  `AllowedToolNamesJson` genuinely restricts the agent's tool set to an explicit allowlist - a
  different mechanism from Phase 10's exclude-list (`excludeToolNames`) used for the
  escalation-triggered restriction, since a workflow's allowlist can freely mix in any sensitive
  tool it names while leaving others out entirely
- [x] `WorkflowsController`: `GET /api/workflows`, `POST /api/workflows` (Admin),
  `POST /api/workflows/{id}/run`, `GET /api/workflows/runs`
- [x] `POST /api/workflows/chat`: a lightweight intent-matching call (not routed through
  `ClaimAgentService.ExecuteRunAsync` - it's routing infrastructure, not itself a decision worth
  an `AgentRunLog` audit row, though it still carries the same `MaxRunDuration` guard so a stalled
  model can't hang the endpoint) asks the model to pick the best-matching chat-triggerable
  workflow and extract its inputs from free text as JSON, parsed defensively (first `{` to last
  `}` substring, tolerant of qwen3's `<think>`-style wrapping). Falls back to
  `ClaimAgentService.QueryAsync` below a confidence threshold, when no workflow is catalogued, or
  when a required input can't be resolved - verified both paths for real (see below)
- [x] **Decision made**: `POST /api/agent/claims/{id}/process` stays its own separate endpoint,
  unchanged. The "Process Claim" `WorkflowDefinition` is special-cased in
  `WorkflowExecutionService` to delegate directly to `ClaimAgentService.ProcessClaimAsync` (same
  `ClaimProcessingOutcome` mapped through, including the `Disputed` block surfacing as `409`)
  rather than going through the generic template-fill path every other workflow uses - claim
  processing's CV/ES/FR rule pre-computation and escalation-triggered tool restriction (Phase 10)
  aren't expressible as "fill a template, restrict a tool list," so reusing the real
  implementation was the only option that wouldn't have silently diverged from it over time
- [x] Verified genuinely end-to-end against real seed data, real Ollama, and a real
  `ClaimsToolsServer` (15/15 checks passed): the 3 built-in definitions seed correctly; a
  structured "Worker Claims History" trigger fills the prompt template with no dangling
  `{placeholder}`s and correctly links its `WorkflowRun` to the real `AgentRunLog`; a structured
  "Process Claim" trigger on a known CV-1/CV-2-failing seeded claim produces the exact same
  `CoverageRejected` transition as calling `/api/agent/claims/{id}/process` directly, proving the
  delegation is real, not a parallel reimplementation; the same workflow on a `Disputed` claim
  surfaces `Blocked` through the workflow layer; a missing required input is a clean
  `ValidationError`, not a crash; a chat message clearly describing "Worker Claims History"
  actually resolved to it with confidence `1.0` (a genuine successful LLM routing decision, not
  just the fallback); unrelated chat text correctly fell back to `/api/agent/query`; and
  "Escalate High-Value Claim" queued only `SendEscalationEmail` for a high-value claim - never
  `CalculatePayout`/`WorkerEmailSender`, since those tools were never in its allowlist at all.
  Found and fixed one real bug this way: the immediate API response after a run showed
  `workflowDefinitionName: "(unknown)"` (a freshly-constructed `WorkflowRun`'s navigation property
  is null until reloaded/`.Include()`d) - fixed by setting it on the in-memory object *after* the
  insert completes, not in the object initializer (setting it before would have pulled the
  already-existing, untracked `WorkflowDefinition` into the same save's change-tracker graph as a
  new "Added" entity and thrown a duplicate-key error on save). Then rebuilt and redeployed the
  real `api`/`claims-tools-server` Docker images into the actual running compose stack and
  re-confirmed the fix and the seeded catalog live against `localhost:8080`.
- [x] Updated `README.md` with a Workflows section (the 3 built-in workflows, both trigger paths,
  defining a new one) and added the 5 new endpoints to the main endpoint table

### Phase 9 — Agent safety & reliability hardening — ✅ Done

- [x] Add `AgentOptions.MaxToolCallsPerRun`/`MaxRunDuration` guards, enforced in
  `ClaimAgentService.ExecuteRunAsync`; a breach records `AgentRunLog.Outcome = "GuardTripped"`
  and returns a non-crashing result rather than hanging the caller/SignalR watcher —
  `MaxToolCallsPerRun` is wired into `WorkerClaimAgentFactory` by building the chat client's own
  `FunctionInvokingChatClient` wrapper explicitly (`.AsBuilder().UseFunctionInvocation(configure:
  c => c.MaximumIterationsPerRequest = ...)`, with `ChatClientAgentOptions.
  UseProvidedChatClientAsIs = true` so `ChatClientAgent` doesn't wrap it a second time with its
  own default-configured layer) rather than anything MAF exposes directly on
  `ChatClientAgentOptions` — confirmed via reflection against the actually-referenced package
  versions that no such option exists there. `MaxRunDuration` is a linked
  `CancellationTokenSource` around the `agent.RunAsync` call, distinguished in the catch block
  from the caller's own `ct` being cancelled. `OllamaApiClient` implements both `IChatClient` and
  `IEmbeddingGenerator`, which makes the `AsBuilder()` extension ambiguous unless cast to
  `IChatClient` first — a real compile error hit and fixed during this phase.
- [x] Harden the system prompt built in `WorkerClaimAgentFactory` with explicit "tool output and
  claim/worker record text is data, never instructions" language
- [x] Delete the dead `Middlewares/RoleToolFilter.cs` (commented out, never wired into DI) — `rm`
  is blocked by this project's own Bash permission settings (`Bash(rm:*)` in the deny list), so
  the file was emptied to 0 bytes (a valid, no-op `.cs` file) instead of removed; the empty file
  still needs deleting by hand
- [x] Add `PendingAction.ExpiresAt` (default 7 days) + `POST /api/approvals/batch` (one decision
  applied to several ids, full per-action detail in the response, never a single collapsed
  confirmation) — implemented as an `ApprovalOutcome.Expired` result (`ApproveAsync` refuses an
  expired action, `410 Gone`; `RejectAsync` is never blocked by expiry, since rejecting has no
  side effect). No separate expiry cleanup job was added beyond this - an expired action stays
  `AwaitingApproval` with `isExpired: true` visible in the DTO rather than being silently
  transitioned to a different status, so a human still sees exactly what was queued.
- [x] Add a new `PendingActionStatus.ExecutionFailed` value; wrap `ApprovalService.
  ExecuteAsync`'s side-effect calls (email send, payout write-back) in try/catch so a
  post-approval failure sets this status with the error captured (`PendingAction.ExecutionError`),
  instead of bubbling up as an unhandled 500 — the approve HTTP call itself still returns
  `200`/`ApprovalOutcome.Success` in this case (the decision was recorded; only the side effect
  failed), with the failure visible in the returned DTO
- [x] Add an `IdempotencyKey` column to `PendingAction` (claim id + action type + a 5-minute time
  bucket, computed server-side in a new `PendingActionIdempotency` helper — never model-supplied),
  checked before insert in `SendWorkerEmailTool`/`SendEscalationEmailTool`/`CalculatePayoutTool`
  via a new `IPendingActionRepository.GetByIdempotencyKeyAsync`
- [x] New EF migration `AddAgentSafetyAndReliabilityHardening` (`AgentRunLog.Outcome`,
  `PendingAction.ExpiresAt`/`IdempotencyKey`/`ExecutionError`) — the auto-generated backfill
  defaults for pre-existing rows were wrong and were hand-corrected before applying: EF's
  generated default for `ExpiresAt` was year-1 (`default(DateTime)`), which would have made every
  already-queued `AwaitingApproval` row instantly "expired" the moment this migration ran; changed
  to `defaultValueSql: "DATEADD(DAY, 7, SYSUTCDATETIME())"` so existing rows get a real 7-day
  grace period from migration time. `Outcome`'s generated default was `""`; changed to `"Success"`
  to match the entity's own default and the only reasonable backfill assumption for a
  already-completed historical run.
- [x] Verified against a real temporary MSSQL container (not just unit-level): applied migrations
  up to the pre-Phase-9 baseline, hand-inserted pre-existing rows under the old schema, applied
  the new migration, and confirmed the corrected backfill (`ExpiresAt` ≈ +7 days from migration
  time, not year 1; `Outcome = "Success"`) landed correctly on those rows. Then ran a small
  throwaway harness exercising the real `PendingActionRepository`/`ApprovalService`/
  `CalculatePayoutTool` classes against that same database: a duplicate `CalculatePayoutTool` call
  for the same claim within the time bucket returned the same `PendingActionId` (no second row);
  approving an expired action returned `Expired` while rejecting it still succeeded; a
  `SendWorkerEmail` approval with a deliberately-throwing `IEmailSender` came back
  `ExecutionFailed` with the error message captured, no exception reaching the caller; a 3-item
  batch (approve/reject/unknown-id) returned full correct per-item detail. Separately verified the
  loop guards against the real running local Ollama (`qwen3:0.6b`) and a real `ClaimsToolsServer`
  instance: a prompt engineered to need two sequential worker lookups with
  `MaxToolCallsPerRun = 1` came back `Outcome = "GuardTripped"` (`ToolCallCount = 2` - the
  underlying `MaximumIterationsPerRequest` bounds model *iterations*, not individual tool calls
  one-for-one, so a single permitted iteration can still contain more than one call; the
  `ToolCallCount >= MaxToolCallsPerRun` check still catches this correctly) in ~32s with no crash;
  `MaxRunDuration = 1ms` came back `Outcome = "Timeout"` in ~74ms with the friendly fallback
  message and no exception, confirming the timeout path is distinguished correctly from the
  caller's own cancellation.
- [x] `README.md` updated: `POST /api/approvals/batch` added to the endpoint table, a curl example
  for it, and a note on `expiresAt`/`isExpired`/`ExecutionFailed` behavior

### Phase 10 — Deterministic business rules (`docs/business-logic.md` → implemented) — ✅ Done (scoped)

**Scope decision, made explicitly rather than silently:** `docs/business-logic.md` itself warns
that real entitlement math needs schema this project doesn't have (PIAWE, work-capacity/RTW
tracking, split amounts) and says to do PY last "because it needs PIAWE and the amount split
first." Implemented here: CV, ES, and the code-computable slice of FR in full per the doc's rule
IDs, plus a **deliberately simplified** PY (caps at remaining policy cover, applies a flat
standard excess, versions and persists its inputs - PY-1/PY-4/PY-6) rather than inventing a
PIAWE-based step-down calculation with no real PIAWE data behind it. **Explicitly not
implemented, and documented as such in code comments at each relevant class**: the EL family
(eligibility/liability - advisory/human by the doc's own design, no deterministic rule needed),
the SL family (statutory-clock tracking - needs fields this pass doesn't add), PY-2/PY-3
(PIAWE-based weekly benefits, partial-capacity offset), CV-5 (multi-policy ambiguity -
`IInsurancePolicyRepository` only supports one policy per worker), ES-2/ES-4 (severity/time-off
thresholds - no such fields exist), FR-2/FR-6 (termination-date/witness fields don't exist), and
the full statutory lifecycle graph beyond `CoverageRejected`/`Disputed` (no RTW/liability-decision
tracking exists to drive it).

- [x] Schema: added `Claim.IncidentDate`/`ReportedDate`/`Jurisdiction` (not `InsurancePolicy` -
  jurisdiction lives on the claim, per the worked example) via migration `AddBusinessRuleFields`.
  `ClaimStatus` gained `CoverageRejected` and `Disputed` (not the full lifecycle graph - see scope
  note above). `PendingAction` gained `RuleOutputsJson`. `AgentCoreDbSeeder` updated to populate
  the new Claim fields for all 20 seeded claims (`IncidentDate`/`ReportedDate` default to
  `ClaimDate`, preserving the documented CV-1/CV-2 known-bad rows exactly; one claim,
  `CLM-250228-003`, deliberately gets a real incident-to-report lag as a genuine FR-1 test case;
  `Jurisdiction` derived from each worker's seeded `Location`)
- [x] `AgentCore.Domain.Rules` (pure, dependency-free classes - same rationale as
  `AgentCoreDiagnostics` living in Domain: both `ClaimsToolsServer` and `AgentCore.Application`
  need them without a new cross-project reference): `CoverageValidator` (CV-1/2/3/4),
  `EscalationEvaluator` (ES-1/3/5/6), `ClaimRiskScorer` (FR-1/4/5, code-computable subset - FR-3
  stays the agent's own judgement per the doc's §5), `EntitlementCalculator` (simplified PY, see
  scope note). Exposed as three new read-only MCP rule tools in `mcp/ClaimsToolsServer/Tools/`
  (`CoverageChecker`, `EscalationEvaluator`, `ClaimRiskScorer` - freely callable, no side effect,
  available to both the read-only and claim-processing agents)
- [x] Hard block on `Disputed` claims: `ClaimAgentService.ProcessClaimAsync` returns a new
  `ClaimProcessingOutcome` (`NotFound`/`Blocked`/`Completed`) and refuses outright - no agent run
  attempted at all - before anything else runs, enforced in code, not a prompt instruction.
  `AgentController` maps `Blocked` to `409 Conflict`
- [x] `ClaimAgentService.ProcessClaimAsync` now runs `CoverageValidator`/`EscalationEvaluator`/
  `ClaimRiskScorer` itself, server-side, *before* calling the model, and sets
  `claim.Status = CoverageRejected` in code when CV fails (only from `Pending`/`UnderReview` -
  leaves an already-`Approved` claim alone, which is exactly the ES-6 "recovery, not decline"
  case) - the status transition never depends on whether the small local model correctly calls or
  reads a tool. All three results are injected into the claim-processing prompt as already-settled
  facts for the model to quote/explain around, per `docs/business-logic.md` §5's suggested prompt
  shape, rather than trusting a 0.6B model to reliably call 3 new tools in the right order itself
  (this project's own Phase 4 notes already flagged that model's tool-calling as unreliable) -
  the rule tools are *also* freely callable via MCP for ad-hoc `/api/agent/query` questions
- [x] When `EscalationEvaluator` triggers, `ClaimAgentService` builds the claim-processing agent
  with `PayoutCalculator`/`WorkerEmailSender` excluded from its toolset for that one run (new
  `WorkerClaimAgentFactory.CreateClaimProcessingAgentAsync(ct, excludeToolNames)` +
  `AgentToolsFactory.BuildToolsetAsync`'s new `excludeToolNames` parameter) - the model cannot
  call a tool it was never shown, so this is a structural guarantee, not a prompt request
- [x] `PendingAction.RuleOutputsJson`: `CalculatePayoutTool` stamps it directly at creation time
  (`EntitlementCalculator`'s `RuleVersion` + `Basis` + the `CoverageValidator` details that cleared
  it) - surfaced in `PendingActionDto` and shown as an expandable "Rule basis" block in the
  Approvals UI
- [x] **The core fix**: `CalculatePayoutTool` no longer takes a `proposedAmount` parameter from the
  model at all - it fetches the claim/policy/history itself, re-runs `CoverageValidator`
  internally (refusing outright, no `PendingAction` queued, if coverage fails - "downstream rules
  don't run" per §7), and calls `EntitlementCalculator` for the amount. New `PayoutCalculationResult`
  return type (`Blocked`/`ComputedAmount`/`Reason`) replaces `PendingActionRef` for this tool only,
  since a payout call can now be refused rather than always queued
- [x] Updated the claim-processing system prompt (`WorkerClaimAgentFactory`) to state the "rules
  decide, you explain" principle explicitly, including that `PayoutCalculator` computes its own
  amount and the model must never supply or propose a dollar figure itself
- [x] Verified genuinely end-to-end, in layers: (1) the deterministic rule engines directly against
  a freshly-seeded real database - all 4 documented CV-1 rows and all 4 documented CV-2 rows
  correctly fail, `CLM-240814-018`'s ES-6 recovery case correctly triggers (tier `Senior`),
  `CLM-250228-003`'s deliberate FR-1 lag correctly flags, a clean claim correctly computes an exact
  deterministic payout; (2) the full `ClaimAgentService.ProcessClaimAsync` flow against a real
  local Ollama (`qwen3:0.6b`) + real `ClaimsToolsServer` - a clean claim stays out of
  `CoverageRejected`, a CV-1+CV-2-failing claim gets set to `CoverageRejected` in code regardless
  of what the model did, a `Disputed` claim is blocked with zero agent run attempted, and an
  escalation-triggered claim queues no `CalculatePayout`/`SendWorkerEmail` at all (those tools
  were never in the model's toolset for that run); (3) `CalculatePayoutTool` called directly,
  bypassing the model entirely - refuses cleanly on a coverage-failing claim, and computes the
  exact rule-derived amount on a clean one, ignoring whatever justification text was passed in;
  (4) rebuilt and redeployed the real `api`/`claims-tools-server` Docker images into the actual
  running compose stack (migration + hand-written SQL backfill applied cleanly against the
  persistent, already-seeded database - the auto-generated backfill defaults were wrong here too,
  same class of bug as Phase 9's migration, fixed the same way before applying) and reproduced the
  exact ES-6 scenario against `CLM-240814-018` live: coverage correctly reported NOT COVERED, the
  claim correctly stayed `Approved` (not flipped to `CoverageRejected`, since money was already
  paid), and the model's own recommendation correctly quoted both the coverage failure and the
  escalation trigger.
- [x] Updated `docs/business-logic.md`'s status header from "proposal, nothing implemented" to
  reflect what's actually implemented per rule family, with the scope decision recorded there too

### Phase 11 — Conversation sessions (multi-turn chat) — ✅ Done

- [x] Replaced the dead `src/AgentCore.Domain/Entities/AgentSession.cs` stub with the real
  `ConversationSession` entity, written to a new file (`Entities/ConversationSession.cs`); the old
  file couldn't be `rm`'d (this session's own Bash permissions deny `rm`, same as `RoleToolFilter.cs`
  in Phase 9) so it was emptied to 0 bytes instead - still needs manual deletion
- [x] Added `AgentRunLog.ConversationSessionId` (nullable FK, `SetNull` on delete, same convention
  as the existing `ClaimId`/`ProposedByAgentRunId` FKs) + EF migration `AddConversationSessions`
  (purely additive/nullable - no backfill-default gotcha like Phase 9's migration had)
- [x] `IConversationSessionRepository` (Domain) + `ConversationSessionRepository` (Infrastructure,
  registered in `AddInfrastructure`); `IAgentRunLogRepository` gained
  `GetByConversationSessionIdAsync` (oldest-first, chat display order - distinct from
  `GetAllAsync`'s most-recent-first audit ordering)
- [x] `ClaimAgentService`: `StartSessionAsync`/`ContinueSessionAsync`/`ListSessionsAsync`/
  `GetSessionMessagesAsync` alongside the unchanged one-shot `QueryAsync`. `ExecuteRunAsync` gained
  optional `AgentSession? session`/`int? conversationSessionId` parameters - passed straight into
  the *same* `agent.RunAsync(prompt, session, ...)` overload `QueryAsync` already used with
  `session: null`, confirmed via reflection to be the real, present-day signature (no separate
  "session-aware" overload needed). On a successful/guard-tripped run, re-serializes the session
  and persists the updated blob + `LastActivityAtUtc` + an auto-derived `Title` (from the first
  message) back onto the `ConversationSession` row, best-effort (a save failure here doesn't fail
  an otherwise-successful run, same principle as `BackfillPendingActionRunIdAsync`)
- [x] `AgentController` gained `POST /api/agent/sessions`, `POST
  /api/agent/sessions/{id}/messages`, `GET /api/agent/sessions`, `GET
  /api/agent/sessions/{id}/messages` (new `ConversationSessionDto`/`SendSessionMessageRequest`/
  `ConversationSessionMessagesResponse` contracts)
- [x] UI: `AgentQueryPage` rewritten into an actual chat thread - a session list + "New chat"
  button in a left rail, the current session's turns rendered as user/assistant bubbles (reusing
  each turn's `AgentRunLogDto.prompt`/`finalAnswer`, with its Outcome badge from Phase 9's UI
  work), and a message input at the bottom. Sidebar nav label changed from "Ask agent" to "Chat
  with agent". New `agentSessionsApi` client (`start`/`list`/`getMessages`/`sendMessage`) and
  `ConversationSessionDto`/`ConversationSessionMessagesResponse` types added to `api/types.ts`.
- [x] Verified genuinely end-to-end, twice: first with a standalone harness (fresh
  `ClaimAgentService`/`DbContext`/`AIAgent` instance per call, mirroring separate HTTP requests)
  against a temporary MSSQL container + real local Ollama (`qwen3:0.6b`) + a real
  `ClaimsToolsServer` instance - a fact stated in turn 1 ("remember the phrase 'purple elephant'")
  was correctly recalled in turn 2 of the *same* session, a brand-new session had zero knowledge
  of it (no cross-session leakage), and `GetSessionMessagesAsync` returned the right turn counts/
  ordering/auto-derived titles for both. Then rebuilt the real `api`/`claims-tools-server` Docker
  images (`docker compose build`) and recreated just those two containers in the actual running
  stack (migration applied cleanly against the persistent `mssql-data` volume, confirmed via
  container logs) - a real `curl`-equivalent session/turn/thread round trip against
  `localhost:8080` reproduced the same result. The Phase 9 safety-hardened system prompt needed no
  extra work to survive turn-to-turn: it's supplied fresh by `WorkerClaimAgentFactory` at
  agent-construction time on every call, never carried inside the serialized session blob, so it
  can't be dropped by compaction the way a fact stated only mid-conversation could be.

### Phase 12 — Identity & Authorization: JWT + user management — Done

- [x] `User` entity + `UserRole` enum (Domain), `IUserRepository` + EF implementation
  (Infrastructure), migration `AddUsersAndWorkerAssignment` (unique index on `Email`;
  self-referencing `CreatedByUserId` FK). **Deviation from the original plan**: SQL Server
  rejects `ON DELETE SET NULL`/`CASCADE` on a self-referencing FK outright (error 1785,
  "may cause cycles or multiple cascade paths"), discovered when the migration was first applied
  against the real MSSQL container - `CreatedByUserId` uses `DeleteBehavior.Restrict` instead,
  which is a non-issue in practice since users are only ever soft-deleted (`IsActive = false`).
- [x] Seed exactly one `SuperAdmin` user at startup, own empty-table guard (same pattern as Phase
  8's independent workflow seeding) so it lands even on an already-populated database; email/
  password from `Seed:SuperAdminEmail`/`Seed:SuperAdminPassword` config, defaulting to
  `superadmin@agentcore.local` / `SuperAdmin123!`
- [x] `PasswordHasher<User>` (`Microsoft.Extensions.Identity.Core`) for hashing/verifying
- [x] `JwtTokenService` (Application) - issues HMAC-SHA256 JWTs (`System.IdentityModel.Tokens.Jwt`)
  carrying `NameIdentifier`/`Email`/`Name` and the *inherited* role claims per the hierarchy (a
  `SuperAdmin` token includes `Admin`/`CaseManager` claims too, an `Admin` token includes
  `CaseManager`); signing key/issuer/audience/expiry from `Jwt:*` config
- [x] `AuthService` (Application): `LoginAsync(email, password)` → verifies via `PasswordHasher`,
  issues a token on success, stamps `User.LastLoginAtUtc`; returns `null` on any failure
  (unknown email, wrong password, deactivated account) without distinguishing which, to prevent
  email enumeration
- [x] `UserManagementService` (Application): the "who can create/manage whom" rule enforced as
  real logic (SuperAdmin → any role; Admin → `CaseManager` only; CaseManager → none), not left to
  controller-level `[Authorize]` alone; also owns `UpdateAsync` (name/email edit),
  `DeactivateAsync`, `ResetPasswordAsync`
- [x] `AuthController`: `POST /api/auth/login`, `GET /api/auth/me`
- [x] `UsersController`: `GET /api/users`, `POST /api/users`, `PUT /api/users/{id}`,
  `POST /api/users/{id}/deactivate`, `POST /api/users/{id}/reset-password` - all Admin+
- [x] `Program.cs`: replaced `HeaderRoleAuthenticationHandler` registration with
  `AddJwtBearer(...)` (`Microsoft.AspNetCore.Authentication.JwtBearer` - new package on
  `AgentCore.Api`); removed the `X-Role` Swagger `ApiKey` scheme + `ActorNameHeaderFilter`, added
  a standard Bearer JWT Swagger security definition
- [x] Renamed `Manager` → `CaseManager` in every `[Authorize(Roles = ...)]` attribute across
  `WorkersController`/`PoliciesController`/`ClaimsController`/`AgentController`/
  `ApprovalsController`/`AgentRunLogsController`/`WorkflowsController` (`SuperAdmin` needed no
  attribute changes anywhere, per the inherited-claims mechanism); `AgentCore.Domain.Common.Roles`
  gained `SuperAdmin`/`CaseManager`, dropped `Manager`
- [x] Emptied `HeaderRoleAuthenticationHandler.cs`, `ActorNameHeaderFilter.cs` to 0 bytes (the
  sandbox's permission settings deny `rm`, so the files still need a manual `rm` pass later - not
  referenced by anything, so they're dead weight, not a functional issue)
- [x] UI: **done in a follow-up pass** (`docs/plan-ui.md`'s Phase UI-7) - `RoleGate` became a real
  login form, `RoleContext`/`useAuth()` decode the JWT's role claims, `client.ts`'s interceptor
  sends `Authorization: Bearer <token>`, and a Users admin page + Workers page case-manager
  assignment were added.
- [x] `README.md`/`docs/`: **done in a follow-up pass** - the root README now leads with a system
  architecture diagram and an agent-loop sequence diagram, documents the JWT login flow, and the
  exhaustive per-endpoint curl reference moved to `docs/api-testing.md` to keep the README itself
  public-app-appropriate rather than a test manual; `docs/README.md` is a new navigation index.
- [x] **Row-level scoping**: `Worker.AssignedCaseManagerUserId` (nullable FK → `User`) + migration;
  `PUT /api/workers/{id}/assign-case-manager` (Admin/SuperAdmin only)
- [x] `AgentCore.Domain.Authorization.WorkerAccessPolicy.CanAccessWorker(worker, callerRoles,
  callerUserId)` - pure, dependency-free, same placement rationale as Phase 10's rule engines
- [x] `WorkersController.GetById` → 403 (not 404) for a `CaseManager` denied by the policy;
  `GetAll` filtered to assigned workers only (`IWorkerRepository`/`IClaimRepository`/
  `IInsurancePolicyRepository.GetAllAsync` and `IClaimRepository.SearchAsync` all gained an
  optional `scopedToCaseManagerUserId` parameter, applied as a SQL `WHERE` translated by EF from
  a navigation-property filter, no explicit `.Include()` needed); `ClaimsController`/
  `PoliciesController` apply the same rule via the claim/policy's own `WorkerId`
- [x] **OBO propagation to the agent/MCP layer**: a new `AgentCore.Api.Auth.ClaimsPrincipalExtensions.
  ToCallerIdentity()` extracts `CallerIdentity` (Domain-free `Agents` record: `UserId` + `Roles`)
  from the validated JWT's claims once, in one place; `AgentController`/`WorkflowsController` call
  it and thread the result through `ClaimAgentService`/`WorkflowExecutionService` into
  `WorkerClaimAgentFactory`. `ClaimAgentService.ProcessClaimAsync` checks `WorkerAccessPolicy`
  itself right after loading the claim - denied with `ClaimProcessingStatus.Forbidden` (mapped to
  HTTP 403) before any agent/LLM call is attempted at all, not just at the MCP layer.
- [x] `AgentToolsFactory` stops lazily caching one shared `McpClient` connection - builds a fresh,
  per-run connection whose `HttpClient` carries `X-Caller-User-Id`/`X-Caller-Role` headers (set
  server-side from the already-validated JWT, never client-controllable) - a deliberate reversal
  of the "connect once, reuse forever" rationale in `docs/plan-mcp.md`/the factory's own doc
  comment, since permission enforcement now depends on which user is running
- [x] `mcp/ClaimsToolsServer`: a scoped `CallerContext` (populated via `IHttpContextAccessor` from
  the two headers above) injected into every tool that touches a specific worker
  (`FetchWorkerTool`, `ClaimsSearchTool`, `WorkerClaimsHistoryTool`, `CheckCoverageTool`,
  `EvaluateEscalationTool`, `ScoreClaimRiskTool`, and all three sensitive tools) - each checks
  `WorkerAccessPolicy` before touching data and returns a clear denial message (not a silent empty
  result) when it fails. A new `ClaimAccessGuard.ResolveAndCheckAsync` helper (in
  `ClaimsToolsServer.Authorization`) shares the "resolve claim → worker → check permission" step
  across the five tools keyed by `claimId`, so the lookup isn't duplicated five times. The two
  email-sender tools' `PendingActionRef` gained an additive, optional `DenialReason` property
  (`PendingActionId` left at `0` on denial - `ClaimAgentService`'s backfill code already logs a
  harmless "not found" for an unresolvable id and moves on, so no changes were needed there);
  `CalculatePayoutTool`'s `PayoutCalculationResult` reused its existing `Blocked`/`Reason` shape,
  since a permission denial and a coverage refusal are both "no `PendingAction` was queued, here's
  why" to the model.
- [x] Verified end-to-end against the real running Docker Compose stack (real MSSQL, real Ollama
  `qwen3:0.6b`, real `ClaimsToolsServer`, rebuilt `api`/`claims-tools-server` images): logged in as
  the seeded SuperAdmin; created a CaseManager account via `POST /api/users`; confirmed a request
  with no `Authorization` header or a garbage bearer token gets 401 (the old `X-Role` header is no
  longer accepted at all); assigned Worker #1 to the CaseManager via
  `PUT /api/workers/1/assign-case-manager`; as that CaseManager, confirmed `GET /api/workers`
  returns only Worker #1, `GET /api/workers/1` and `GET /api/claims/1002` (Worker #1's claim) both
  return 200, and `GET /api/workers/2`/`GET /api/claims/6` (an unassigned worker/claim) both return
  403; confirmed `POST /api/agent/claims/6/process` (unassigned worker's claim) returns 403 with a
  clear reason **before** any LLM call, proving the permission check runs ahead of the agent; then
  confirmed `POST /api/agent/claims/1002/process` (assigned worker's claim) completes a full real
  agent run end-to-end - the CaseManager's own JWT identity propagated through
  `AgentController` → `ClaimAgentService` → `AgentToolsFactory`'s per-run MCP connection →
  `ClaimsToolsServer`'s `CallerContext`, proving OBO holds across the process boundary, not just
  within the API.

**Known gaps carried forward**: `ApprovalsController`/`AgentRunLogsController` got the
`Manager`→`CaseManager` rename but no row-level scoping (an approval/run-log isn't tied to a
single worker the same direct way, and the user's request was specifically about worker-record and
agent-action permission, not the approvals queue - left as a deliberate scope decision, not an
oversight, revisit if that gap matters later). (The UI and `README.md`/`docs/` gaps noted above
when this phase first landed were since closed in follow-up passes - see this phase's checklist
items and the later Change Log entries below.)

## Change Log

- **2026-09-18** — Initial plan created and written to `docs/plan.md`.
- **2026-09-18** — Clarified that `agent-core/` is a brand-new, standalone project: no code,
  tools, models, or data are reused from `first-agent/` or any other folder. Removed the
  "fold first-agent in" decision and the migration-based Phase 1/2 checklist items in favor of
  authoring everything fresh inside `agent-core/`. `first-agent/` remains untouched.
- **2026-09-18** — Phase 1 implemented: solution + 5 projects scaffolded under `agent-core/`,
  Domain entities/enums/repository interfaces authored, and the three auto tools
  (`FetchWorkerTool`, `ClaimsSearchTool`, `WorkerClaimsHistoryTool`) written against those
  interfaces. Solution builds successfully.
- **2026-09-18** — Phase 2 implemented: `AgentCoreDbContext` + EF Core SQL Server provider,
  repository implementations, initial migration, fresh seed data (6 workers / 6 policies /
  20 claims), and connection-string wiring in the API's `Program.cs`/`appsettings.json`.
  Verified end-to-end against a temporary Dockerized MSSQL instance — migration and seed both
  ran successfully.
- **2026-09-18** — Phase 3 implemented: header-based role auth (`X-Role`/`X-Actor-Name`) via a
  custom `AuthenticationHandler` plus standard `[Authorize(Roles=...)]`, `WorkersController`/
  `PoliciesController`/`ClaimsController` CRUD, claims search + history endpoints, and Swagger
  configured with `X-Role` as a one-click ApiKey security scheme. Finalized the Claims
  permission rule (both roles CRUD, Admin-only delete) since the original decision table only
  covered Workers/Policies. Verified with an 8-test xUnit + `WebApplicationFactory` in-process
  suite against a temporary MSSQL container — all passing.
- **2026-09-18** — Phase 4 implemented: Ollama-backed agent wiring (`WorkerClaimAgentFactory`
  with read-only vs. claim-processing tool sets), sensitive-tool interception into
  `PendingAction` (new `IPendingActionRepository`/`IAgentRunLogRepository` + EF
  implementations), `AgentController` (`/api/agent/query`, `/api/agent/claims/{id}/process`),
  and a read-only `AgentRunLogsController` audit endpoint (added now to satisfy section 7's
  architecture, which had listed it without a matching checklist item). Renamed the planned
  `AgentRunContext` tool-context class to `AgentToolRunContext` after discovering it collided
  with a real `Microsoft.Agents.AI.AgentRunContext` type in the framework. Verified against a
  real local Ollama (`qwen3:0.6b`) instance end-to-end, not just structurally.
- **2026-09-18** — Added a new requirement: observability (logs/traces/metrics) via an
  LGTM-style stack, and real-time visibility into agent reasoning on a UI. Added architecture
  sections 8 (Observability) and 9 (Real-time agent visibility via SignalR), expanded section
  10's (formerly 8) Docker Compose service list with `otel-collector`/`tempo`/`loki`/
  `prometheus`/`grafana`, and inserted a new **Phase 6 — Observability & live agent
  visibility** into the checklist (the former Phase 6, Docker Compose, is now Phase 7).
  Decided Prometheus over Mimir for the metrics leg (same Grafana-stack shape, far less setup
  for a demo) and SignalR over Server-Sent Events for the live agent stream (native to .NET,
  easier for a future frontend to consume).
- **2026-09-18** — Phase 5 implemented: `ApprovalsController` (list/approve/reject),
  `ApprovalService` executing the real side effect on approve (simulated email via a new
  `IEmailSender` port/`LoggingEmailSender` implementation; payout write-back to `Claim`), and
  audit fields populated from the caller's role/actor headers. Clarified that `PendingAction`
  goes straight from `AwaitingApproval` to `Executed` on approve (synchronous execution, no
  separate async-approved-but-not-yet-executed state in this demo). Verified with 7 new xUnit
  tests against a temporary MSSQL container, 20/20 passing overall.
- **2026-09-18** — Phase 6 implemented: OpenTelemetry instrumentation (traces/metrics/logs via
  OTLP, plus a direct `/metrics` Prometheus endpoint), a shared `AgentCoreDiagnostics`
  ActivitySource/Meter in Domain, a SignalR `AgentActivityHub` for live agent-run visibility
  (plus a `RunFailed` event added after testing showed a failed run left watchers with no
  terminal event), and Grafana provisioning files under `agent-core/observability/` for Phase 7
  to mount. Found and fixed a real bug: the global auth fallback policy was blocking both
  Prometheus scraping and the SignalR handshake (neither can send our `X-Role` header) - both
  are now `[AllowAnonymous]`. Documented that tool-call events are published in sequence right
  after the (blocking) `RunAsync` call returns, not as genuine token-level streaming (would
  need `RunStreamingAsync`, not probed this phase). Verified against the same real local Ollama
  instance as Phase 4; noted its single-request-at-a-time limit as a sandbox constraint after it
  caused one test to flake under repeated full-suite runs (the `RunFailed` path was instead
  verified deterministically via an unreachable Ollama host, with no dependency on that
  instance).
- **2026-09-18** — Phase 7 implemented: `api.Dockerfile` (multi-stage build) and `compose.yaml`
  wiring `mssql`, `ollama` (+ an `ollama-init` one-shot model-pull service), `otel-collector`,
  `tempo`, `loki`, `prometheus`, `grafana`, and `api` together, with env vars pointing the API
  at the compose service hostnames. Named `api.Dockerfile`/`compose.yaml` rather than the bare
  `Dockerfile`/`docker-compose.yml` (this session's tooling blocks writes to those exact
  filenames; both alternates are equally standard, `compose.yaml` being Compose's own preferred
  name today). Found and fixed a real bug: Tempo v3.0.0 removed the top-level `compactor`
  config field my config used; removed it. Verified `mssql`/`otel-collector`/`tempo`/`loki`/
  `prometheus`/`grafana` actually start cleanly and confirmed via their own APIs that Grafana's
  datasources/dashboard and Prometheus's scrape target provisioned correctly. Could not fully
  verify the `api` service or `ollama` in this sandbox - its Docker network policy blocks
  outbound HTTPS from inside build/running containers (confirmed via a reverted, never-committed
  test), which breaks `dotnet restore` during `docker build` and appears to be why the
  `ollama/ollama` image pull stalled; neither is a defect in the compose/Dockerfile setup, and
  the user should run `docker compose up --build` themselves for the final end-to-end
  confirmation.
- **2026-09-18** — The user confirmed the same `ollama/ollama` pull problem on their own
  machine and asked to use a local/native Ollama install instead. Removed the `ollama` and
  `ollama-init` services and the `ollama-data` volume from `compose.yaml`; the `api` service
  now reaches Ollama via `http://host.docker.internal:11434` (with `extra_hosts:
  ["host.docker.internal:host-gateway"]` so that hostname resolves on native Linux Docker
  Engine, not just Docker Desktop). Updated the "LLM provider" decision and the Docker Compose
  architecture section to match. Re-validated `docker compose config`.
- **2026-09-18** — The user hit the exact NU1301 "can't reach nuget.org" error on their own
  machine during `docker build`, on the same corporate email domain as this session's own
  NuGet proxy - strongly suggesting the same corporate-network restriction. Added an optional
  `nuget_config` build secret to `api.Dockerfile` (`required=false`, so environments with
  normal nuget.org access are unaffected) plus `compose.override.yaml.example` documenting how
  to enable it (copy to `compose.override.yaml`, drop a `NuGet.Config` alongside it, both
  gitignored). Verified through `docker compose build api` that the secret is correctly picked
  up and applied (confirmed by the restore error's source URL changing from `api.nuget.org` to
  the supplied feed) - it still can't complete in this sandbox specifically, since outbound
  HTTPS from containers is blocked here entirely, but the mechanism itself is proven correct.
- **2026-09-18** — The user hit the identical `NU1301` error on their own machine even with the
  NuGet secret override in place, and asked why a custom feed should be needed at all rather
  than the default. Tested `docker build --network=host`, which also failed - ruling out a
  simple bridge-network firewall rule and confirming outbound HTTPS is blocked from *any*
  network mode a build container can use, on both this sandbox and (per the identical error)
  the user's own machine. Replaced the restore-inside-Docker approach entirely: `api.Dockerfile`
  now just copies an already-`dotnet publish`-ed `publish/` folder, needing zero network access
  during `docker build`. Removed the now-unneeded `nuget_config` secret machinery and
  `compose.override.yaml.example`. **Verified genuinely end-to-end this time**: published on
  the host, then `docker compose up --build` brought up the full stack including `api`, with
  Prometheus's own API confirming it successfully scraped the running `api` service. Documented
  that Ollama must bind to `0.0.0.0` (not its default `127.0.0.1`-only) for the containerized
  `api` to reach it via `host.docker.internal`.
- **2026-09-18** — The user pushed back that the Dockerfile was wrong (a Dockerfile shouldn't
  need a manual `dotnet publish` first), which prompted actually diagnosing the `NU1301` error
  instead of working around it. **The earlier "outbound HTTPS is blocked from containers"
  conclusion was wrong.** Step by step: DNS resolves fine inside containers; IPv4 lookup and the
  TCP connection to :443 both succeed; `wget` fails with `certificate verify failed`; and
  `openssl s_client` shows the chain issued by `O = Zscaler Inc.` — i.e. corporate TLS
  inspection, where the host trusts the re-signing root CA but a stock container image doesn't.
  (`--network=host` "not helping" earlier was consistent with this all along and should not have
  been read as proof of a network block.) Fixed properly: `api.Dockerfile` is now an ordinary
  self-contained multi-stage build again, with both stages installing any `.crt` from a new
  `certs/` folder via `update-ca-certificates` (`certs/zscaler.crt` added, plus a `README.md`
  explaining how to identify/extract the right CA, and a `.gitkeep` so the `COPY` still works
  when the folder is empty on a normal network). Dropped the host-publish workaround and its
  `publish/` gitignore entry. Verified: `docker compose up --build` alone now performs a real
  in-container `dotnet restore` from nuget.org, the full stack comes up, `GET /api/workers`
  returns seeded data with role auth enforced, Prometheus scrapes `api` (`"health":"up"`), and
  spans flow api → otel-collector → Tempo.
- **2026-09-18** — The user hit exactly the predicted `host.docker.internal:11434` connection-
  refused error (native Ollama defaults to binding `127.0.0.1` only) and asked to run Ollama in
  Docker instead. Reversed the earlier decision: added back the `ollama` service (+
  `ollama-init` one-shot `ollama pull qwen3:0.6b`) and an `ollama-data` volume to `compose.yaml`;
  `api`'s `Agent__OllamaHost` now points at `http://ollama:11434` and the `host.docker.internal`/
  `extra_hosts` wiring was removed as no longer needed. Updated the "LLM provider" decision and
  the Docker Compose architecture section to match.
- **2026-09-18** — The user's real `docker compose up --build` run showed `ollama-init` failing
  with `certificate signed by unknown authority` pulling `qwen3:0.6b` from `registry.ollama.ai`
  - the exact same class of TLS-inspection issue as `api.Dockerfile`'s earlier `NU1301`, now
  hitting a second, prebuilt image we don't control the build of. Fixed the same way: added
  `ollama.Dockerfile` (`FROM ollama/ollama:latest` + the `certs/` CA install, confirmed the base
  image is Ubuntu-based so `update-ca-certificates` is already present), tagged
  `agentcore-ollama:local`, with `ollama` now built from it and `ollama-init` reusing that same
  tag instead of the bare image. Verified in three steps: (1) the same pull against the
  *unfixed* image reproduced the exact error, confirming root cause; (2) the same pull against
  the *fixed* image succeeded (522 MB at 8.7 MB/s, ending `success`); (3) a full
  `docker compose up --build` run, then a real `POST /api/agent/query` through
  `api → ollama → model`, returned a genuine LLM response entirely inside Docker.
- **2026-09-18** — With all 7 phases done, the user asked for a README covering how to run and
  test the stack (URLs, API testing, Grafana, SignalR). Added `README.md` (quick start, URL
  table, auth, full endpoint table, curl/Swagger walkthrough, Grafana usage, troubleshooting)
  and `tools/signalr-test.html` (a standalone browser page — connects to `/hubs/agent-activity`,
  subscribes by claim id, shows `RunStarted`/`ToolCallStarted`/`ToolCallCompleted`/
  `RunCompleted`/`RunFailed` live), since Swagger can't demonstrate the SignalR side at all.
  Cross-checked every documented role restriction against the actual `[Authorize]` attributes
  in each controller rather than relying on memory.
- **2026-09-18** — The user asked for a genuinely comprehensive Grafana dashboard with all
  datasources connected. Brought up the real stack and generated traffic (worker/claim reads,
  an agent query, a full claim-processing run with a tool call, an approval) specifically to
  enumerate the *actual* available telemetry rather than guess it: the full Prometheus metric
  catalog (`/api/v1/label/__name__/values`) revealed far more than expected for free from the
  existing auto-instrumentation - `signalr_server_*`, `kestrel_*`, `http_client_*` (which,
  filtered by `server_address="ollama"`, isolates real LLM-call latency from the API's own OTLP
  export traffic), `process_runtime_dotnet_*`; queried Loki directly and confirmed every log
  line already carries `trace_id`/`span_id` as structured metadata; confirmed Tempo's
  `traceqlSearch` query type via Grafana's own `/api/ds/query` proxy. Wired real cross-datasource
  correlation into `datasources.yaml` (Tempo→Loki via exact `trace_id` match, Tempo→Prometheus,
  Loki→Tempo via label-matched derived field) and hit two real bugs in doing so, both found and
  fixed by actually testing rather than trusting the config: (1) what looked like a genuine
  Tempo↔Loki circular-reference crash on startup turned out to be stale provisioning state in
  the `grafana-data` volume - wiping it fixed it, disproving the original theory; (2)
  `tracesToMetrics`'s `$__tags.job` template variable was being silently eaten by Grafana's own
  provisioning-time env-var expansion (`$__tags` looked like an unset env var to Grafana and got
  blanked to empty string) - fixed by escaping it `$${__tags.job}`, confirmed via the datasource
  API reading back the correct value afterward. Expanded `agentcore-overview.json` from 4 panels
  to 27 across 7 rows (Agent Activity, API Performance, Outbound Calls to Ollama, Connections,
  .NET Runtime, Logs, Traces) and verified **every single panel's query** programmatically
  against the live stack via `/api/ds/query` - all 27 returned valid results, several spot-
  checked for genuinely non-empty data. Updated `README.md`'s Grafana section to match.
- **2026-09-18** — The user asked for agent-run results to include model, token usage, tool
  call count, input/output cost, and reasoning/"thinking" content. Investigated what's actually
  available before building anything (a probe against the real running agent, reflecting over
  the raw response): confirmed `agent.RunAsync`'s return type is `Microsoft.Agents.AI.
  AgentResponse`, that `.Usage` (`Microsoft.Extensions.AI.UsageDetails`) is genuinely populated
  by OllamaSharp with real input/output/total token counts, and that qwen3's reasoning mode
  surfaces as a distinct `Microsoft.Extensions.AI.TextReasoningContent` message content item
  alongside the regular `TextContent` answer - so every field asked for turned out to be
  genuinely available, not something to fake. Added `ModelId`, `ToolCallCount`, `ReasoningText`,
  token counts, and cost fields (driven by new `AgentOptions.InputPricePerMillionTokens`/
  `OutputPricePerMillionTokens`, defaulting to 0 for local Ollama) to `AgentRunLog`, a new EF
  migration (`AddAgentRunLogTelemetry`), `AgentRunLogDto`, and the SignalR `RunCompleted` event
  (whose `IAgentActivityPublisher.RunCompletedAsync` now takes the full `AgentRunLog` instead of
  loose scalars). Hit and fixed a real C# gotcha along the way: passing a `dynamic` argument
  makes the *entire enclosing call* dynamically typed even when the callee's declared return
  type is concrete, silently turning `extracted` into `dynamic` and breaking tuple deconstruction
  - fixed by casting the argument to `object` at the call site. Verified against the real
  running stack: a query with no relevant tools returned real reasoning text distinct from the
  final answer, real token counts, and `toolCallCount: 0`; processing a claim that used
  `FetchWorker` returned `toolCallCount: 1`; the existing database migrated cleanly with the API
  container already running. Updated `README.md` and `tools/signalr-test.html` to match.
- **2026-09-18** — The user asked for a suggested business-logic design for the insurance agent,
  written to `docs/`. Authored `docs/business-logic.md` as a **proposal only — nothing in it is
  implemented**, and no code, schema, or seed data was changed. Context that shaped it: the
  domain is genuinely Australian workers' compensation, so the document leads with an explicit
  caution that every threshold/percentage/day-count in it is an *illustrative demo default*, not
  drawn from scheme legislation (real entitlements are statutory and per-jurisdiction — NSW
  SIRA/icare, VIC WorkSafe, QLD WorkCover), and that the shapes of the rules are the
  contribution rather than the numbers. Core recommendation: **deterministic code decides
  eligibility and money; the LLM reads unstructured text, flags inconsistencies, drafts
  communications and explains outcomes** — which adds a third tool category ("rule tools":
  agent may call and must quote, but cannot override or recompute) alongside the existing auto
  and sensitive tools, and inverts today's `CalculatePayout`, where the model proposes an
  arbitrary amount for a human to approve rather than the rules computing it. Document covers:
  schema gaps blocking most rules (notably that `Claim` has only one date, so no timeliness rule
  is currently writable; also `Jurisdiction`, split medical/weekly amounts, PIAWE, reserves,
  liability decision separate from status), an expanded claim lifecycle (including a hard block
  on agent runs for `Disputed` claims), and a rule catalogue of 6 families with IDs — CV
  (coverage), EL (eligibility/liability), PY (payout), ES (escalation), FR (fraud signals), SL
  (timeliness) — each tagged Code/Agent/Human owner. Grounded the whole thing in the **real
  seed data rather than generic examples**: verified by reading `AgentCoreDbSeeder.cs` that
  6 of the 20 seeded claims already violate CV-1 or CV-2 (four lodged outside their policy
  period, six with a claim type the policy doesn't cover), giving the rules engine a ready-made
  regression fixture on day one. The standout: `CLM-240814-018` is **`Approved` despite being
  both outside its policy period and the wrong coverage type** — i.e. money paid on a claim with
  no cover, which the document treats as a recovery case (ES-6) rather than a decline. Also
  suggested a build order (schema gaps → CV rules → lifecycle → ES → rule outputs attached to
  `PendingAction` → PY → FR) and noted that letting the agent decide CV/EL/PY should stay off
  the roadmap permanently.
- **2026-09-18** — The user asked whether the app should support admin/manager-configured
  "workflows" - triggered either by structured input (e.g. a worker id) or by chat, on top of
  today's fixed `/api/agent/query`/`/api/agent/claims/{id}/process` endpoints. Recommended, and
  the user confirmed, a scoped version rather than a general workflow/DAG builder: named,
  parameterized "playbooks" (`WorkflowDefinition`: input schema + prompt template + a restricted
  tool set + optional chat-trigger hints), executed through the existing `ClaimAgentService` run
  path unchanged (same `PendingAction` interception, same SignalR events, same `AgentRunLog`),
  with a `WorkflowRun` row recording only the trigger metadata (which definition, which resolved
  inputs, structured vs. chat, chat's original text + match confidence). User explicitly chose
  to include the chat-trigger path now rather than deferring it, with a required fallback to
  today's free-form `/api/agent/query` whenever the intent-matcher has low confidence or can't
  resolve a required input - chat routes to a workflow, it never becomes a second execution
  engine. Added architecture section 11 (Workflows) between Docker Compose and the
  implementation checklist (renumbered checklist from section 11 to 12; no other section numbers
  changed), added `WorkflowDefinition`/`WorkflowRun` to the domain model (section 4) and
  `WorkflowsController`'s endpoints to the API surface (section 7), and added **Phase 8 -
  Workflows (playbooks)**, not started, as a new checklist phase. Explicitly out of scope for
  v1, called out in the new section: branching/conditional steps, loops, one workflow triggering
  another, a visual builder UI.
- **2026-09-18** — The user requested the UI be implemented next, using React + axios, and
  asked for a plan document first, for review before any code is written. Wrote
  `docs/plan-ui.md`: a React 18 + axios SPA (`agent-core/ui/`) consuming the existing REST API
  exactly as Swagger/curl do today (same `X-Role` header, no new backend business logic), with
  recommendations (pending review) for TypeScript, Vite, `react-router-dom`, and
  `@tanstack/react-query`, plus a live agent-activity panel reusing the SignalR contract from
  section 9/`tools/signalr-test.html`. Found one genuinely required backend change while
  writing it: `Program.cs` has no CORS policy today, which will block every browser call from a
  different-origin SPA - documented as a small, required `AddCors`/`UseCors` addition, not
  optional. Proposed build order (Phases UI-1 through UI-6, scaffolding → read-only screens →
  write screens → agent interaction → live panel → Docker packaging) explicitly front-loads a
  fully usable app in UI-1 through UI-4, treating the live SignalR panel and Docker packaging as
  stretch goals given the user's stated remaining time budget. Added this section (12, UI) as a
  pointer to that document and renumbered the former section 12 (Implementation checklist) to
  13; no other section numbers changed. **Awaiting the user's review of `docs/plan-ui.md`
  before any UI implementation begins** - Phase UI checklist items live in that document, not
  here, once approved.
- **2026-09-18** — With limited remaining session time flagged by the user, started
  implementing the UI (`docs/plan-ui.md`) immediately, picking the highest-value slice rather
  than working strictly top-to-bottom: `agent-core/ui/` scaffolded (Vite + React 18 +
  TypeScript + `react-router-dom` + `@tanstack/react-query` + axios, all decisions from
  `docs/plan-ui.md` §2 taken as proposed), plus a real, required backend change - `Program.cs`
  had no CORS policy, which would have silently blocked every browser call from a
  different-origin SPA; added `AddCors`/`UseCors` with a configurable `Cors:AllowedOrigins`
  (default `http://localhost:5173`), rebuilt and redeployed the `api` container, and confirmed
  via a real cross-origin request (`fetch` with `Origin: http://localhost:5173`) that
  `Access-Control-Allow-Origin` now comes back correctly. Built and verified against the real
  running stack (not mocked): Dashboard (counts + claims-by-status), Workers/Policies/Claims
  list pages, Agent Query (a real Ollama call returned genuine `reasoningText`/token counts),
  "Run agent" wired into the Claims list (shows the real recommendation/queued
  actions/run stats), and the Approvals page (a real queued `SendEscalationEmail` action was
  rejected through it end-to-end). Deliberately skipped for this pass, to be picked up next:
  per-record detail pages, claim search/filter UI, all create/edit/delete forms, the live
  SignalR activity panel, and Docker packaging for the UI - `docs/plan-ui.md` §12 has the exact
  breakdown. `docker compose up`'s existing services are untouched aside from the `api` image
  rebuild; the UI runs separately via `npm run dev` for now (documented in `agent-core/ui/README.md`).
- **2026-09-18** — The user asked for the UI to use Tailwind CSS and a left sidebar (light
  background, blue-family accent - "not literal blue," a color set fitting for this kind of
  app, their words). Installed Tailwind CSS v3 + PostCSS/Autoprefixer in `agent-core/ui/`,
  added a `brand` indigo color scale in `tailwind.config.js` alongside Tailwind's stock `slate`
  neutrals, and replaced the plain-CSS top `NavBar` with a fixed-width left `Sidebar` (logo,
  nav links with a soft-highlight active state, role badge + "Switch role" pinned at the
  bottom) - `App.tsx` is now a `flex` layout (`Sidebar` + scrollable content area) instead of a
  stacked top-bar layout. Rewrote every existing page/component (`RoleGate`, `LoadingState`,
  `ErrorBanner`, Dashboard, Workers/Policies/Claims lists, Approvals, Agent Query, Agent Run
  Logs) from the old custom CSS classes to Tailwind utilities, and added a small shared
  `statusBadgeClasses`/`roleBadgeClasses` helper so claim/action status colors (soft pills:
  emerald/red/amber/slate by state) and role identity colors (solid pills: indigo for Admin,
  violet for Manager) stay consistent across pages instead of being redefined per page.
  Verified with a real production build (`npm run build` - clean typecheck, Tailwind's
  generated CSS grew from ~4 KB to ~16 KB, confirming its content scan actually picked up the
  new utility classes rather than emitting an empty stylesheet) and confirmed the running dev
  server hot-reloaded every changed file with no errors. Updated `docs/plan-ui.md` §2's
  Styling/Layout decisions and `ui/README.md` to match.
- **2026-09-18** — The user reported the UI couldn't reach the API (CORS). Root cause,
  confirmed rather than assumed: a leftover background `npm run dev` (started by me in an
  earlier turn) was still holding port 5173, so when the user ran their own `npm run dev`, Vite
  silently fell back to port 5174 - which wasn't in `Cors:AllowedOrigins`'s single-origin
  default, so the browser genuinely blocked every call. Verified with a real preflight
  (`OPTIONS` with `Origin: http://localhost:5174`) before touching anything - it came back
  without an `Access-Control-Allow-Origin` header, confirming the diagnosis. Fixed two ways:
  widened the default in `Program.cs` to `http://localhost:5173,http://localhost:5174` (Vite's
  actual fallback behavior, not just its default port), and killed the stray background dev
  server so port 5173 is free again going forward. Rebuilt and redeployed the `api` container;
  re-verified with real preflight + `GET` requests carrying `Origin: http://localhost:5174` -
  both now come back with the correct `Access-Control-Allow-Origin` header. Left the user's own
  running dev server (on 5174) untouched. Updated `docs/plan-ui.md` §3 with a note on what
  actually shipped vs. the original single-origin proposal.
- **2026-09-21** — The user asked for a gap analysis of AgentCore against
  `docs/knowledge-base.md` (a Microsoft Agent Framework reference doc) and a plan to close the
  real gaps. An audit of the actual codebase (not just the docs) against all 12 knowledge-base
  topics found: no agent-loop guards (max tool calls/timeout), no prompt-injection hardening in
  the system prompt, a dead/never-wired `RoleToolFilter.cs`, no HITL batch approval/expiry/
  post-approval-failure handling, no idempotency keys on sensitive tools, and — most
  consequentially — that `docs/business-logic.md`'s deterministic rule-tool design is still
  entirely unimplemented, meaning `CalculatePayoutTool` still lets the model itself propose the
  dollar amount rather than a deterministic calculator, contradicting the project's own stated
  "LLM proposes, never executes/computes" invariant. Also confirmed as genuinely out of scope for
  now (no requirement driving them): multi-turn session/memory, RAG, multi-agent orchestration,
  adaptive planning, and multi-instance scale-out; `mcp/ClaimsToolsServer`'s missing OTel
  instrumentation was already tracked in `docs/plan-mcp.md` §9 and isn't duplicated here. Added
  architecture section 13 (Agent hardening & reliability) capturing these decisions, renumbered
  Implementation checklist from section 13 to 14, and added **Phase 9 — Agent safety &
  reliability hardening** and **Phase 10 — Deterministic business rules (`docs/business-logic.md`
  → implemented)** to the checklist, both Not started.
- **2026-09-21** — The user asked to implement Phase 9. Implemented all six items: loop guards
  (`AgentOptions.MaxToolCallsPerRun`/`MaxRunDuration`, wired into `WorkerClaimAgentFactory` via an
  explicit `FunctionInvokingChatClient` wrapper and a linked `CancellationTokenSource` in
  `ClaimAgentService.ExecuteRunAsync`), system-prompt injection hardening, batch approval
  (`POST /api/approvals/batch`) with action expiry (`PendingAction.ExpiresAt`, `410` on approving
  an expired action, reject still always allowed), post-approval execution-failure capture
  (`PendingActionStatus.ExecutionFailed` + `ExecutionError`, no more unhandled 500 from
  `ApprovalService.ExecuteAsync`), and server-side idempotency keys on all three sensitive MCP
  tools. New EF migration `AddAgentSafetyAndReliabilityHardening` (`AgentRunLog.Outcome`,
  `PendingAction.ExpiresAt`/`IdempotencyKey`/`ExecutionError`) - caught and fixed two wrong
  auto-generated backfill defaults before applying it (see Phase 9's checklist entry for detail).
  `Middlewares/RoleToolFilter.cs` couldn't actually be deleted - this session's own Bash
  permissions deny `rm` outright - so it was emptied to a 0-byte file instead; flagged for the
  user to remove by hand. Verified everything against real infrastructure rather than just
  building: a temporary MSSQL container (migration backfill correctness, plus a throwaway harness
  exercising the real `ApprovalService`/`PendingActionRepository`/`CalculatePayoutTool` classes for
  idempotency/expiry/execution-failure/batch behavior) and the real local Ollama (`qwen3:0.6b`)
  plus a real running `ClaimsToolsServer` instance for both loop guards, including one genuine
  finding along the way: `MaximumIterationsPerRequest` bounds model iterations, not individual
  tool calls one-for-one, so `MaxToolCallsPerRun = 1` still let 2 tool calls through in a single
  iteration - the `ToolCallCount >= MaxToolCallsPerRun` check after the fact still catches this
  correctly, so no design change was needed, but the checklist now documents the nuance. Updated
  `README.md`'s endpoint table and Approvals section to match. Phase 10 (deterministic business
  rules) remains Not started.
- **2026-09-22** — The user asked whether not implementing session/chat-history support was a
  major gap, and separately why `docs/plan.md`'s phases don't cover every topic in `docs/
  knowledge-base.md`. On the second question: `plan.md`'s phases track what's actually being
  built for this project, while the knowledge base is a generic framework reference - several
  topics (RAG, multi-agent orchestration, adaptive planning, Identity/OBO, runtime scale) were
  legitimately excluded as out of this demo's scope, consistent with the knowledge base's own P3
  "awareness only" guidance. On the first question: agreed this one was a genuine miss, not a
  deliberate exclusion - Topic 3 (Conversation & Context Management) is P1 in the knowledge base,
  and `Compactions.cs` (built in Phase 6) already implements a full compaction pipeline that had
  nothing to compact, since no session ever persisted across calls for it to act on. Added
  architecture section 14 (Conversation sessions) grounded in a real reflection probe against the
  installed `Microsoft.Agents.AI` 1.21.0 package (confirmed `AIAgent.CreateSessionAsync`/
  `SerializeSessionAsync`/`DeserializeSessionAsync`/`RunAsync(message, session, ...)` are the real,
  present-day APIs for this, not assumed from the knowledge base's generic example code), and
  found that `src/AgentCore.Domain/Entities/AgentSession.cs` already contains dead, unreferenced
  scaffolding from an earlier abandoned attempt at this same feature - decided to replace rather
  than build on it, partly because its own `ChatMessage` name would collide with `Microsoft.
  Extensions.AI.ChatMessage` the same way `AgentRunContext` did in Phase 4. Renumbered UI (section
  12, unchanged) and Implementation checklist from section 14 to 15, and added **Phase 11 —
  Conversation sessions (multi-turn chat)** to the checklist, Not started. No code changed in this
  entry - planning only, per this project's own convention of writing the architecture/plan before
  code for a feature of this size (same as Workflows and the business-logic proposal earlier).
- **2026-09-22** — The user asked which of Phase 8/10/11 was best to implement next and left the
  choice to the model; picked **Phase 11 (Conversation sessions)** since it was the most concretely
  designed and grounded (real reflection against the installed package, not assumption), had a
  tractable, well-bounded scope compared to Phase 10's larger and more ambiguous rule-family work,
  and directly answered the user's own concern from this same conversation. Implemented in full:
  the `ConversationSession` entity (replacing the dead `AgentSession.cs` stub),
  `AgentRunLog.ConversationSessionId`, `IConversationSessionRepository`, four new
  `ClaimAgentService` methods layered onto the existing `agent.RunAsync(prompt, session, ...)`
  overload (confirmed via reflection to already be the real signature `QueryAsync` was calling
  with `session: null` - no new agent-side API needed), four new `AgentController` endpoints, and
  a full rewrite of the UI's Agent Query page into an actual multi-session chat thread. New
  migration `AddConversationSessions`. Verified twice against real infrastructure: a standalone
  harness (temporary MSSQL container + real local Ollama + real `ClaimsToolsServer`, with a fresh
  service/DbContext/agent instance per call to rule out in-process luck) proved a fact stated in
  one turn was correctly recalled in the next turn of the same session, with zero leakage into a
  new session; then the real `api`/`claims-tools-server` Docker images were rebuilt and recreated
  in the actual running compose stack (migration applied cleanly against the persistent volume)
  and the same round trip was reproduced against `localhost:8080` directly. Updated Phase 11's
  checklist to Done with full implementation notes.
- **2026-09-22** — The user again asked which phase was best to implement and left the choice to
  the model; picked **Phase 10 (deterministic business rules)**, the one previously argued to be
  the most consequential - `CalculatePayoutTool` was still letting the model itself propose the
  payout amount, the exact thing `docs/business-logic.md` calls the core problem. Re-read
  `docs/business-logic.md` in full before starting and made an explicit, documented scope
  decision rather than attempting every rule family literally: implemented CV/ES/the
  code-computable subset of FR in full, and a deliberately simplified PY (caps at remaining cover
  + flat excess, versioned and persisted - not the PIAWE-based calculation the source document
  itself says needs schema this pass doesn't add); left EL, SL, CV-5, ES-2/ES-4, FR-2/FR-6,
  PY-2/3/5, and the full statutory lifecycle graph explicitly undone and documented as such, both
  in code comments and in `docs/business-logic.md`'s new status table. Added
  `AgentCore.Domain.Rules` (`CoverageValidator`, `EscalationEvaluator`, `ClaimRiskScorer`,
  `EntitlementCalculator` - pure, dependency-free, same rationale as `AgentCoreDiagnostics` living
  in Domain) plus three new MCP rule tools; inverted `CalculatePayoutTool` to compute its own
  amount and refuse outright on a coverage failure; hard-blocked `Disputed` claims in
  `ClaimAgentService` via a new `ClaimProcessingOutcome` contract; made an escalation trigger
  remove `PayoutCalculator`/`WorkerEmailSender` from the agent's toolset for that run entirely
  (`AgentToolsFactory`/`WorkerClaimAgentFactory` gained an `excludeToolNames` parameter); added
  `RuleOutputsJson` to `PendingAction`, surfaced in the Approvals UI. New migration
  `AddBusinessRuleFields` - its auto-generated backfill defaults were wrong in the same way
  Phase 9's migration was, made worse this time by the live compose database already holding 20
  real seeded claims; fixed by hand-writing a SQL backfill matching exactly what
  `AgentCoreDbSeeder.cs` sets, verified against a simulated pre-existing dataset before trusting
  it against the real environment. Verified in layers against real infrastructure: the rule
  engines directly against a freshly-seeded database (all 8 documented CV-1/CV-2 known-bad rows,
  the `CLM-240814-018` ES-6 recovery case, and the deliberate `CLM-250228-003` FR-1 lag test case
  all correct); the full `ProcessClaimAsync` flow against real Ollama + real `ClaimsToolsServer`
  (clean claim stays out of `CoverageRejected`, a CV-failing claim gets set to `CoverageRejected`
  in code regardless of the model, a `Disputed` claim is blocked with zero agent run attempted,
  an escalation-triggered claim queues no payout/worker-email since those tools were never in its
  toolset); `CalculatePayoutTool` called directly bypassing the model entirely (clean refusal on
  coverage failure, exact rule-derived amount on a clean claim); and finally the real `api`/
  `claims-tools-server` Docker images rebuilt and redeployed into the actual running compose stack,
  reproducing the exact `CLM-240814-018` ES-6 scenario live (coverage correctly reported NOT
  COVERED, claim correctly stayed `Approved` rather than flipping to `CoverageRejected` since money
  was already paid). Updated `docs/business-logic.md`'s status header with a per-family
  implementation table, and `README.md`'s Agent section and endpoint table to match. Phase 8
  (Workflows) remains the only checklist item not started.
- **2026-09-22** — The user asked to start Phase 8 (Workflows), the last remaining checklist item.
  Implemented in full: `WorkflowDefinition`/`WorkflowRun` entities + repositories + migration
  `AddWorkflows`; a new independently-guarded seeding step for the 3 built-in workflows so an
  already-populated database (this project's own dev/compose database) still picks them up rather
  than being skipped by the existing Workers-empty early-return; `AgentToolsFactory`/
  `WorkerClaimAgentFactory` gained an explicit-allowlist tool-set path (distinct from Phase 10's
  exclude-list mechanism) for `CreateWorkflowAgentAsync`; `ClaimAgentService` gained a public
  `RunWithCustomAgentAsync` so `WorkflowExecutionService` could reuse the exact same guard/
  observability/approval-backfill path every other run goes through; `WorkflowsController` with
  all 5 endpoints from this section's design. Decided "Process Claim" stays a special case that
  delegates directly to `ClaimAgentService.ProcessClaimAsync` rather than the generic template-
  fill path, since claim processing's Phase 10 rule-integration (CV/ES/FR pre-computation, the
  `Disputed` block, escalation-triggered tool restriction) can't be expressed as "fill a template,
  restrict a tool list" - reusing the real implementation was the only option that wouldn't
  silently drift from it later. Verified genuinely end-to-end (15/15 checks) against real seed
  data, real Ollama, and a real `ClaimsToolsServer`: structured triggers for all 3 built-ins,
  confirming "Process Claim" produces the identical `CoverageRejected`/`Disputed`-block behavior
  as calling `/api/agent/claims/{id}/process` directly (proving real delegation, not
  reimplementation); a missing-required-input validation error; a chat trigger that genuinely
  resolved to the right workflow at confidence 1.0 (not just the fallback); an unrelated chat
  message correctly falling back to `/api/agent/query`; and confirmation that
  "Escalate High-Value Claim" never queues `CalculatePayout`/`WorkerEmailSender` since neither is
  in its allowlist. Found and fixed one real bug via this verification: the immediate API response
  showed `workflowDefinitionName: "(unknown)"` for a freshly-created `WorkflowRun` (its navigation
  property is null until reloaded) - fixed by assigning it in-memory only after the insert
  completes, since assigning it beforehand would have pulled an already-existing, untracked
  `WorkflowDefinition` into the same save as a spurious duplicate-key insert. Rebuilt and
  redeployed the real `api`/`claims-tools-server` Docker images into the actual running compose
  stack and re-confirmed the seeded catalog and the bugfix live. Updated `README.md` with a new
  Workflows section and the 5 new endpoints. **All 8 checklist phases are now Done** - Phase 8 was
  the last one remaining.
- **2026-09-22** — The user asked to implement real Identity & Authorization with JWT: a `User`
  management feature, a seeded SuperAdmin with full access, and a `CaseManager` role (the real
  workers'-comp industry term for what this project has called `Manager` since Phase 3) that an
  `Admin` has more access than - explicitly a plan-first request, no code to be written until
  reviewed. Forked a read-only research pass over the current auth wiring (`Program.cs`,
  `HeaderRoleAuthenticationHandler`, every controller's `[Authorize(Roles=...)]`, the UI's
  `RoleGate`/`RoleContext`/`client.ts`, `AgentActivityHub`'s `[AllowAnonymous]` reasoning, and
  confirmed no JWT/Identity package is referenced anywhere yet) to ground the plan in what's
  actually there rather than assumption. Rewrote section 5 entirely (replacing, not extending,
  the header-trust model - letting both coexist would let anyone bypass real auth by just setting
  a header) with a 3-tier strictly-nested role hierarchy (`SuperAdmin` ⊃ `Admin` ⊃
  `CaseManager`, the last two being renames of today's `Admin`/`Manager` with unchanged
  permissions) and the key mechanism decision: a JWT's role claims include everything the holder's
  tier inherits, so every existing `[Authorize(Roles=...)]` attribute across the whole codebase
  needs only `Manager` renamed to `CaseManager` - `SuperAdmin` needs zero controller changes
  anywhere. Added the `User` entity to section 4 (Domain model), explicitly distinguished from the
  pre-existing, unrelated `Worker` entity (the injured employee a claim is about) to head off an
  obvious naming confusion. Added `AuthController`/`UsersController` to section 7 (REST API
  surface). Added **Phase 12** to the checklist, Not started, with an explicit "deferred, not
  silently dropped" list (SignalR hub auth, refresh tokens/revocation, login brute-force
  protection, migrating existing free-text audit fields to real `User` FKs, row-level data
  scoping, external IdP/MFA) so the boundary of this phase is a deliberate choice, not an
  oversight. No code changed in this entry - awaiting review before implementation, per this
  project's own established convention for a change of this size (same as Workflows and the
  business-logic proposal earlier).
- **2026-09-22** — The user asked to fold two more requirements into Phase 12, directly citing
  `docs/knowledge-base.md` Topic 4: (1) a `CaseManager` may only retrieve a worker's information
  if they have permission for it, and (2) when a `CaseManager` uses the agent, the agent's tool
  calls must carry *that user's* authority and enforce the same permission boundary - not a
  blanket trusted-service identity. This revises, not just adds to, the previous entry's
  "no row-level data scoping - not requested" line, since it now is requested. Designed and wrote
  into section 5: a single-assignment permission model (`Worker.AssignedCaseManagerUserId`, deny
  by default when unassigned), a pure `WorkerAccessPolicy.CanAccessWorker` rule (Domain, same
  placement rationale as Phase 10's rule engines), REST-layer enforcement (403 for a denied
  `GetById`, filtered `GetAll`), and - the harder half - genuine OBO-style propagation of the
  caller's identity from the validated JWT through `ClaimAgentService`/`WorkflowExecutionService`
  into a per-run `McpClient` connection (carrying `X-Caller-User-Id`/`X-Caller-Role` headers set
  server-side, never client-controllable) so `mcp/ClaimsToolsServer`'s own tools can enforce the
  identical permission check before touching data. Documented that this deliberately reverses the
  "connect once, reuse forever" `McpClient` design `docs/plan-mcp.md`/`AgentToolsFactory`'s own
  doc comment argued for, since permission enforcement now depends on which user is running - the
  old rationale ("nothing about a tool call needs to be correlated to a specific run") no longer
  holds. Expanded Phase 12's checklist accordingly. Implementing next.
- **2026-09-22** — Implemented Phase 12 in full (both the base JWT/user-management design and the
  same-day row-level scoping/OBO additions above) and marked it Done. Built and verified against
  the real running Docker Compose stack: real MSSQL migration (`AddUsersAndWorkerAssignment`,
  regenerated after a one-line `DeleteBehavior` fix - SQL Server rejects `SET NULL`/`CASCADE` on a
  self-referencing FK, error 1785), real Ollama `qwen3:0.6b`, real `ClaimsToolsServer`, rebuilt
  `api`/`claims-tools-server` images. Confirmed the old `X-Role` header is no longer accepted at
  all (401 with no/garbage `Authorization` header); confirmed a CaseManager sees only their
  assigned worker via REST (`GET /api/workers`, `GET /api/workers/{id}`, `GET /api/claims/{id}`,
  403 otherwise); confirmed `POST /api/agent/claims/{id}/process` denies an unassigned worker's
  claim with 403 *before* any LLM call, and completes a full real agent run for an assigned
  worker's claim with the CaseManager's own identity propagated through the MCP boundary end to
  end. UI and `README.md` updates are explicitly deferred (see the phase's "Known gaps carried
  forward" note) - the backend's auth model changed completely, so the existing UI cannot log in
  until its own follow-up lands; the user has not asked for that yet.
- **2026-09-22** — Follow-up: migrated the UI to the new JWT model (`docs/plan-ui.md`'s Phase
  UI-7 - real login form, `useAuth()`, Users admin page, Workers page case-manager assignment),
  then did a full documentation pass before the first push to git, since teammates would read
  these files first. Fixed genuine leftover staleness in source itself, not just docs: three MCP
  sensitive tools' `[Description(...)]` text still said "Admin/Manager approval" from before the
  role rename; `AgentActivityHub`/`Program.cs` comments still described the old `X-Role` header;
  `AgentCore.Agents/DependencyInjection.cs`'s comment on `AgentToolsFactory` still described the
  pre-Phase-12 shared-connection design. `.gitignore` was missing `ui/node_modules/` (100MB+) and
  `ui/dist/` entirely - fixed before anything could be pushed. Overhauled `README.md`: added a
  system-architecture section (Mermaid component diagram + a table linking each component to the
  exact `docs/plan.md` section that covers it), an agent-loop sequence diagram and a tool-tier/
  approval-gate diagram under a new "How the agent works" section, and a "Swapping the LLM
  provider" note (the agent depends only on `Microsoft.Extensions.AI`'s `IChatClient`; Ollama is
  constructed in exactly one line, so pointing at OpenAI/Azure OpenAI/a self-hosted
  OpenAI-compatible server later needs no other change). Moved the exhaustive per-endpoint curl
  reference out of `README.md` into a new `docs/api-testing.md` - a public-facing README
  shouldn't double as a test manual - and added `docs/README.md` as a navigation index (which doc
  covers what, suggested reading order) plus a table of contents to this file, since it had grown
  past 1000 lines with no way to jump to a section. Fixed two now-stale checklist bullets under
  Phase 12 above (UI/README were marked "not done this pass" - now done) and a few historical-but-
  now-outdated design notes in `docs/plan-mcp.md` (the singleton-`McpClient` design it documents
  was reversed by this same Phase 12, and one illustrative code sample's `[Description]` text and
  `PendingActionRef` shape had drifted from the real current file) - annotated as superseded
  rather than rewritten, to preserve the historical narrative those sections are there to record.
