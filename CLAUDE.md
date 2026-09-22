# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

AgentCore V2.0: an insurance claims platform where SuperAdmins/Admins/CaseManagers manage
workers/claims/policies and run a local-Ollama LLM agent to evaluate claims. A `CaseManager` is
scoped to only the `Worker`s assigned to them — including through the agent itself (see
Identity & Authorization below). The agent can read data freely within that scope, but every
sensitive/side-effecting action (email a worker, escalate, calculate a payout) is queued as a
`PendingAction` that a human must approve before it actually executes. This is a from-scratch
project living entirely in this directory — it does not reuse code from any sibling project.

`docs/plan.md` is the living source of truth for architecture/decisions/phase history (update it
in the same turn as any requirement or architecture change, per its own header). `docs/plan-ui.md`
is the equivalent for the React UI, `docs/plan-mcp.md` for the ClaimsToolsServer MCP split (why the
claim/worker tools live in their own process, the run-correlation problem that split created, and
how it's solved). `docs/business-logic.md` is an unimplemented proposal for real insurance rules
(coverage validation, eligibility, payout calculation, escalation) — read it before touching
anything payout/eligibility-related, since it documents *why* an LLM must never compute money or
decide eligibility itself. `README.md` has the practical run/test guide (curl examples for every
endpoint, Grafana usage, troubleshooting).

## Commands

```sh
# Full stack (SQL Server, Ollama + auto-pulled qwen3:0.6b, API, ClaimsToolsServer MCP server, observability stack)
docker compose up --build

# Backend only, against a local SQL Server/Ollama (set connection strings via env or appsettings)
# Both processes are required for the agent to have any tools - AgentCore.Api is an MCP *client*
# of ClaimsToolsServer now, not an in-process tool host.
dotnet build AgentCore.sln
dotnet run --project mcp/ClaimsToolsServer   # separate terminal
dotnet run --project src/AgentCore.Api

# EF Core migrations (run from repo root; design-time factory means no running composition root needed)
dotnet ef migrations add <Name> --project src/AgentCore.Infrastructure --startup-project src/AgentCore.Api
dotnet ef database update --project src/AgentCore.Infrastructure --startup-project src/AgentCore.Api

# Unit tests
dotnet test tests/AgentCore.Agents.Tests/AgentCore.Agents.Tests.csproj

# UI (separate from the .sln — plain npm project)
cd ui && npm install && npm run dev   # http://localhost:5173, needs the API's CORS change (already in Program.cs)
```

`tests/AgentCore.Agents.Tests` (wired into `AgentCore.sln`) is the only checked-in test project so
far. It hosts `ClaimsToolsServer` in-process via `WebApplicationFactory<Program>` (no real database
needed — MCP's `tools/list` is pure reflection over registered tool methods, never invokes them,
so a syntactically-present-but-unreachable connection string is enough) and drives it with a real
`McpClient` over an in-memory `HttpMessageHandler`, checking `AgentToolsFactory` gets back the
expected six tool names with none silently colliding — the MCP tool collection is keyed by name,
so a duplicate `[McpServerTool(Name = ...)]` doesn't produce two list entries, it silently *drops*
one tool entirely; a client-side "no duplicate names" check alone won't catch that, which is why
the test also asserts the exact expected name set. Most earlier phases were instead verified with
ad-hoc xUnit suites run against a temporary MSSQL container and a real local Ollama instance rather
than checked-in automated tests — if you add more, prefer a project per `src/`/`mcp/` project being
tested (`tests/<ProjectName>.Tests`) and wire it into `AgentCore.sln`.

Auth is real JWT — `POST /api/auth/login` (email/password) issues an HMAC-SHA256 token, sent as
`Authorization: Bearer <token>` on every other call: 401 if missing/expired/invalid, 403 if the
caller's role can't perform that action. Roles are a strict hierarchy, `SuperAdmin` ⊃ `Admin` ⊃
`CaseManager` (a token carries every role its tier inherits, so an `[Authorize(Roles=...)]`
check needs only the literal role names, no `SuperAdmin`-specific logic anywhere — see
`docs/plan.md` §5). One `SuperAdmin` is seeded at startup (`Seed:SuperAdminEmail`/
`Seed:SuperAdminPassword` config, defaulting to `superadmin@agentcore.local` /
`SuperAdmin123!`) and creates other accounts via `UsersController`. See `README.md` for the full
endpoint/role table and copy-paste curl bodies.

## Architecture

Five `src/` projects, dependencies flow strictly inward (`Api` → `Application`/`Infrastructure`/
`Agents` → `Domain`; `Domain` has no dependencies), plus `mcp/ClaimsToolsServer` as a separate
deployable that owns the claim/worker tools:

- **`AgentCore.Domain`** — entities (`Worker`, `Claim`, `InsurancePolicy`, `PendingAction`,
  `AgentRunLog`, `User`), enums (including `UserRole`: `SuperAdmin`/`Admin`/`CaseManager`),
  repository *interfaces*, and cross-cutting ports (`IEmailSender`, `IAgentActivityPublisher`)
  that `Agents`/`Application`/`ClaimsToolsServer` depend on without depending on
  `Infrastructure`/`Api` — those layers hand in the concrete implementation via DI. Also owns
  `WorkerAccessPolicy.CanAccessWorker` (`AgentCore.Domain.Authorization`) — the pure, dependency-
  free row-level permission check a `CaseManager` is scoped by, shared by both `Api` and
  `ClaimsToolsServer` without a circular reference (same placement rationale as
  `AgentCoreDiagnostics` below).
- **`AgentCore.Infrastructure`** — EF Core `AgentCoreDbContext`, MSSQL migrations, repository
  implementations, `AgentCoreDbSeeder` (6 workers / 6 policies / 20 claims, seeded on API startup —
  deliberately includes known-bad rows, e.g. claims outside their policy's coverage period, as a
  regression fixture for future rule validation; see `docs/business-logic.md` §6). Both
  `AgentCore.Api` and `mcp/ClaimsToolsServer` reference this and connect to the same `AgentCoreDb`
  directly; only `Api` runs migrations/seeding on startup.
- **`mcp/ClaimsToolsServer`** — a standalone ASP.NET Core process exposing the claim/worker tools
  over MCP (Streamable HTTP, `ModelContextProtocol.AspNetCore`, endpoint `/mcp`) instead of hosting
  them in-process with the API — see `docs/plan-mcp.md` for the full rationale/tradeoffs. Auto
  tools (`FetchWorkerTool`, `ClaimsSearchTool`, `WorkerClaimsHistoryTool`) read via repositories and
  return a plain string; sensitive tools (`SendWorkerEmailTool`, `SendEscalationEmailTool`,
  `CalculatePayoutTool`) never act — they write a `PendingAction` row and return a small
  `PendingActionRef { PendingActionId, ActionType, DenialReason }` (JSON) instead of a hand-written
  sentence, so the *client* (see below) can stamp the audit FK itself. Every tool class is
  `[McpServerToolType]` with `[McpServerTool(Name = "...")]` methods; `WithToolsFromAssembly()`
  discovers and DI-resolves them automatically (confirmed - no explicit
  `AddScoped<FetchWorkerTool>()` etc. needed). The `[Description]` attributes on methods/
  parameters still drive the model-facing tool descriptions, the same way `AIFunctionFactory`
  used them in-process. **Identity & Authorization (Phase 12)**: a scoped `CallerContext`
  (`ClaimsToolsServer.Authorization`, populated from two trusted headers set only by `Api` — see
  below) is injected into every tool that touches a specific worker; each calls
  `WorkerAccessPolicy.CanAccessWorker` before touching data (via the shared
  `ClaimAccessGuard.ResolveAndCheckAsync` helper for the five tools keyed by `claimId`) and
  returns a clear denial message, never a silent empty result, when it fails.
- **`AgentCore.Agents`** — the Ollama-backed agent, an **MCP client** of `ClaimsToolsServer`
  rather than a tool host. `AgentToolsFactory` builds a **fresh `McpClient` connection per call**
  (a deliberate reversal, as of Phase 12, of the original "connect once, reuse forever" design —
  permission enforcement now depends on which user is running, so the connection can no longer
  be shared across callers), whose `HttpClient` carries `X-Caller-User-Id`/`X-Caller-Role`
  headers derived from a `CallerIdentity` (asserted server-side from the already-validated JWT,
  never client-controllable) — this is the OBO propagation into the MCP boundary (`docs/plan.md`
  §5). `ListToolsAsync()` results convert directly into `AITool`s (`McpClientTool` already
  derives from `Microsoft.Extensions.AI.AIFunction` → `AITool` — no conversion needed), filtered
  by a client-side `SensitiveToolNames` set since MCP itself has no "sensitive" concept — the
  server just exposes one flat list. `WorkerClaimAgentFactory` builds two variants from that
  toolset: `CreateReadOnlyAgentAsync(caller)` (auto tools only, used by `/api/agent/query`) and
  `CreateClaimProcessingAgentAsync(caller)` (auto + sensitive tools, used by claim processing).
- **`AgentCore.Application`** — orchestration. `ClaimAgentService.ProcessClaimAsync` checks
  `WorkerAccessPolicy.CanAccessWorker` right after loading the claim and returns
  `ClaimProcessingStatus.Forbidden` (mapped to HTTP 403) *before* any agent/LLM call is attempted
  at all if the caller (a `CaseManager`) isn't assigned to that claim's worker — the REST-layer
  permission check, distinct from the MCP-layer one enforced independently inside
  `ClaimsToolsServer` itself. `ExecuteRunAsync` is the one place every agent invocation flows
  through: persists the `AgentRunLog` row *before* calling the model,
  wraps the call in an `Activity` span + duration/outcome metrics recorded in a `finally` block,
  publishes SignalR lifecycle events (`RunStarted`/tool-call events/`RunCompleted`/`RunFailed`),
  then walks the completed `AgentResponse` for `FunctionCallContent`/`FunctionResultContent` (tool
  calls, published in sequence — not token-level streaming, see `docs/plan.md` §9) and
  `TextReasoningContent` (qwen3's "thinking" output, separate from the final answer). For a
  sensitive tool's result specifically, it also parses out `PendingActionRef.PendingActionId` and
  stamps `PendingAction.ProposedByAgentRunId` — the tool itself can't do this anymore, since it runs
  in a different process with no visibility into this run's id (`docs/plan-mcp.md` §4 covers why
  this design was chosen over passing the run id across the MCP boundary some other way). **Gotcha
  confirmed against a real run**: an MCP tool's result arrives as `Microsoft.Extensions.AI.
  TextContent`, not a plain `string`/`JsonElement` — unwrap its `.Text` property before parsing
  JSON out of it, or the parse silently no-ops and the FK stays null. `ApprovalService` executes the
  real side effect on approve — simulated email send via `IEmailSender`, or payout write-back onto
  the `Claim` (amount + status) — and is a no-op on reject.
- **`AgentCore.Api`** — controllers, `AddJwtBearer` (real JWT validation, replacing the old
  `HeaderRoleAuthenticationHandler`/`X-Role` model entirely), Swagger (with a standard Bearer JWT
  security definition — paste the token from `POST /api/auth/login`), and `AgentActivityHub`
  (SignalR, `/hubs/agent-activity`, `[AllowAnonymous]` since browsers can't attach an
  `Authorization` header to a WebSocket handshake — it's read-only observational data anyway).
  New `AuthController` (`POST /api/auth/login`, `GET /api/auth/me`) and `UsersController`
  (`GET`/`POST /api/users`, `PUT /api/users/{id}`, `POST /api/users/{id}/deactivate`,
  `POST /api/users/{id}/reset-password` — Admin+ only). `AgentCore.Api.Auth.
  ClaimsPrincipalExtensions.ToCallerIdentity()` extracts `CallerIdentity` (userId + roles) from
  the validated JWT's claims once, in one place; `AgentController`/`WorkflowsController` call it
  and thread the result into every agent/workflow service call, which is what makes the OBO
  propagation into MCP (above) possible. `WorkersController`/`ClaimsController`/
  `PoliciesController` enforce `WorkerAccessPolicy` on every read/write that touches a specific
  worker (403 for a `CaseManager` denied by the policy, not 404); `WorkersController` also gained
  `PUT /api/workers/{id}/assign-case-manager` (Admin+ only) — the single-assignment permission
  model's write path.

### The approval gate (the core design constraint)

This is the one invariant that must never be violated when touching the agent: **the LLM proposes,
it never executes.** Any new tool with a real-world effect must follow the sensitive-tool pattern —
write a `PendingAction` (via `IPendingActionRepository`), never call a repository/service that
mutates real state directly. The actual effect only happens in `ApprovalService` on human approval.
`docs/business-logic.md` extends this same principle to money/eligibility: those must become
deterministic "rule tools" the agent can call and quote but never override, not something the model
computes — worth reading before adding anything in that space.

### Observability

OpenTelemetry (traces/metrics/logs) exports via OTLP to an otel-collector, fanned out to
Tempo/Prometheus/Loki, visualized in a pre-provisioned Grafana (`observability/grafana/`) — today
only `AgentCore.Api` is instrumented this way; `mcp/ClaimsToolsServer` isn't yet (see
`docs/plan-mcp.md` §9). Custom signal beyond auto-instrumentation lives in `AgentCoreDiagnostics`
(`AgentCore.Domain.Diagnostics` — shared so `Application` and `ClaimsToolsServer` can both record
against it without a circular reference): `agentcore.agent.runs` counter,
`agentcore.agent.run.duration` histogram, `agentcore.pending_actions.queued` counter (recorded from
`ClaimsToolsServer`'s sensitive tools now, not from `AgentCore.Agents`). This is separate from the
SignalR hub: Grafana is for operators watching system health across all runs, SignalR is for a
person watching *one* run they just triggered.

### UI

`ui/` is a separate Vite/React/TypeScript project (not part of `AgentCore.sln`), consuming the same
REST API via axios. A real login form (`POST /api/auth/login`) replaces the old role-picker;
the issued JWT is stored client-side and sent as `Authorization: Bearer <token>` on every call.
`useAuth()` decodes the token's role claims for nav/button gating only (never trusted for actual
authorization — enforcement is server-side only, unchanged). A Users page (Admin+ only) manages
accounts; the Workers page's "Case manager" column assigns/unassigns a worker to a `CaseManager`.
See `ui/README.md` to run it and `docs/plan-ui.md` for what's built vs. planned (detail pages,
write forms, the live SignalR panel, and Docker packaging aren't done yet).
