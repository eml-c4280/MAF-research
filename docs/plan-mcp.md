# AgentCore MCP Tools Server — Plan

> **Status: implemented and verified end-to-end** (see §10's checklist). Companion to
> [`docs/plan.md`](plan.md) (the backend plan) and [`docs/plan-ui.md`](plan-ui.md) (the UI plan),
> same relationship: this covers moving the claim/worker tool set out of `AgentCore.Agents` and
> into `mcp/ClaimsToolsServer` as a standalone local MCP (Model Context Protocol) server that
> `AgentCore.Agents` calls as an MCP client instead of instantiating tool classes in-process.
>
> **Both open decisions were confirmed and implemented as chosen:** the host backfills
> `PendingAction.ProposedByAgentRunId` after the fact (§4, option 2), and `claims-tools-server`
> publishes a host port for direct debugging (§7). One real bug was found and fixed during
> verification, not anticipated by this plan: an MCP tool's result arrives client-side as
> `Microsoft.Extensions.AI.TextContent`, not the plain `string`/`JsonElement` this plan assumed -
> §4's backfill code has to unwrap `.Text` before parsing JSON out of it, confirmed against a real
> run (`docker compose logs api` first showed the FK silently staying null; a direct
> `AIFunction.InvokeAsync` call against the running `ClaimsToolsServer`, bypassing the flaky
> 0.6B model, pinned down the exact runtime type before fixing and re-verifying).

## 1. Overview & goals

Today, `AgentCore.Agents/Tools/` holds six tool classes (`FetchWorkerTool`, `ClaimsSearchTool`,
`WorkerClaimsHistoryTool`, `SendWorkerEmailTool`, `SendEscalationEmailTool`, `CalculatePayoutTool`),
each implementing `IAgentTool` and DI-registered into the same process as `AgentCore.Api`.
`AgentToolsFactory` filters them by `IsSensitive` and hands the resulting `AITool` list to
`WorkerClaimAgentFactory`, which builds the Ollama-backed `AIAgent` directly in-process.

This plan moves all six tools into `mcp/ClaimsToolsServer` — a new, standalone ASP.NET Core
process exposing them over MCP (Streamable HTTP transport) — and turns `AgentCore.Agents` into an
MCP **client** that discovers and calls those tools remotely. The scaffold already at
`mcp/ClaimsToolsServer/` (an untouched `dotnet new webapi` template) becomes that server.

**Why this is worth the added complexity (stated for the record, since it's a real cost):** it
decouples the tool implementations from the API process's lifecycle/deployment, makes the tool set
independently testable/callable by any MCP-speaking client (not just this one agent), and is a
reasonable dry run for a world where AgentCore hosts tools for multiple different agents/products.
For a single-agent demo it is not strictly *necessary* — worth being explicit that this is the
tradeoff being made.

## 2. Key decisions

| Decision | Choice | Notes |
|---|---|---|
| MCP transport | Streamable HTTP (`ModelContextProtocol.AspNetCore`'s `MapMcp`) | Not stdio. The scaffold is already `Microsoft.NET.Sdk.Web` with Swagger/Kestrel and a fixed dev port (`http://localhost:5050`, from `Properties/launchSettings.json`), and the rest of this repo already runs every component as its own long-lived compose service reachable over the network (`ollama`, `mssql`, …) rather than a child process spawned via stdio. "Local" is read as *"runs on this deployment, not a third-party/remote MCP server"* — not *"stdio child process."* |
| SDK | `ModelContextProtocol` / `ModelContextProtocol.Core` / `ModelContextProtocol.AspNetCore`, v2.2.0 | The official C# MCP SDK. Confirmed via a scratch project that `McpClientTool` (client side) already derives from `Microsoft.Extensions.AI.AIFunction` → `AITool`, so tool results from `ListToolsAsync()` can be used directly as `ChatOptions.Tools` with **no conversion step**. Confirmed `[McpServerToolType]`/`[McpServerTool]` (server side) is built on the same `AIFunctionFactory` machinery as today's `AIFunctionFactory.Create(...)`, so the existing `[Description]` attributes on tool methods/parameters (the whole point of the previous `IAgentTool` refactor) carry over unchanged. |
| Data access from the MCP server | Direct EF Core against the same `AgentCoreDb`, via new project references to `AgentCore.Domain`/`AgentCore.Infrastructure` | Rejected alternative: have `ClaimsToolsServer` call back into `AgentCore.Api`'s REST endpoints over HTTP. Direct DB access keeps the tool implementations almost byte-for-byte identical to today (same repository interfaces, same method bodies) and avoids a second network hop plus the awkwardness of an internal service needing to fabricate a human-asserted-role value for endpoints that expect one (at the time this was written, that meant `X-Role`; since Phase 12, docs/plan.md §5, it's a JWT-derived role — the reasoning is unchanged either way). |
| `PendingAction.ProposedByAgentRunId` correlation | **Host backfills it after the fact**, not the tool | See §4 — this is the one genuinely new problem this refactor introduces, since `AgentToolRunContext`'s in-process ambient-scope trick doesn't cross a process boundary. |
| MCP client lifetime | ~~Singleton, created once and reused~~ **Superseded by Phase 12** | Original reasoning: once tools no longer need a per-run correlation id, there's no reason to reconnect per agent run, so one long-lived `McpClient` amortizes the handshake cost. **This was reversed once permission enforcement needed to know which user is running** (`docs/plan.md` §5) — `AgentToolsFactory` now opens a fresh `McpClient` connection *per call*, carrying that call's `CallerIdentity` as headers, accepting the extra per-run handshake latency as the cost of real authorization. |
| Tool auto/sensitive split | Kept client-side, by tool **name** | MCP has no native concept of "sensitive"; the server exposes one flat tool list. `AgentToolsFactory` keeps its existing job — filtering — just backed by `ListToolsAsync()` results instead of local `IAgentTool` instances. Tool `ToolAnnotations` (`ReadOnly`/`Destructive`, native MCP metadata) are set to match, for any future MCP-aware tooling/inspector, but are not what AgentCore itself relies on for the split. |
| Solution wiring | `ClaimsToolsServer.csproj` added to `AgentCore.sln` under a new `mcp` solution folder | Mirrors how `tests/AgentCore.Agents.Tests` was wired in under `tests` — `dotnet build AgentCore.sln` should still build everything. |
| Docker Compose | New `claims-tools-server` service, own Dockerfile, **host port published** | Mirrors `api.Dockerfile`'s shape (multi-stage, `certs/` CA install). `api`'s `Agent:ClaimsToolsServerUrl` points at `http://claims-tools-server:8081/mcp` in compose, `http://localhost:5050/mcp` for local non-Docker dev (matching the scaffold's existing launch profile). `8081:8081` is published to the host so it can be poked directly with curl or an MCP inspector during development, same as Swagger is for `api`. |

## 3. Project structure

```
mcp/ClaimsToolsServer/
├── ClaimsToolsServer.csproj      # + refs to AgentCore.Domain, AgentCore.Infrastructure
│                                  # + PackageReference ModelContextProtocol.AspNetCore
├── Program.cs                    # AddMcpServer().WithToolsFromAssembly().WithHttpTransport(); MapMcp("/mcp")
├── appsettings.json              # + ConnectionStrings:AgentCoreDb
└── Tools/                        # moved verbatim from src/AgentCore.Agents/Tools/, minus IAgentTool
    ├── FetchWorkerTool.cs
    ├── ClaimsSearchTool.cs
    ├── WorkerClaimsHistoryTool.cs
    ├── SendWorkerEmailTool.cs
    ├── SendEscalationEmailTool.cs
    └── CalculatePayoutTool.cs
```

`src/AgentCore.Agents/Tools/` loses all six tool classes plus `IAgentTool.cs`. `AgentToolsFactory.cs`
and `AgentToolRunContext.cs` are rewritten/removed per §4-§5. Nothing in `AgentCore.Domain` moves —
the MCP server takes a *dependency* on it (repository interfaces, entities), same as
`AgentCore.Infrastructure`/`AgentCore.Agents` already do.

## 4. The run-correlation problem (read this before implementing)

Today, `SendWorkerEmailTool`/`SendEscalationEmailTool`/`CalculatePayoutTool` stamp
`PendingAction.ProposedByAgentRunId` by reading `AgentToolRunContext.AgentRunLogId` — a scoped
object that `ClaimAgentService.ExecuteRunAsync` mutates in place (`_runContext.AgentRunLogId =
run.Id`) *after* the tool instances already exist, relying on DI handing the tool constructor and
`ExecuteRunAsync` the **same shared, mutable, in-process object**. That trick has no equivalent
across a process boundary: `ClaimsToolsServer` runs in a different process/DI container and has no
way to see `AgentCore.Api`'s scoped state.

Two ways to solve this were considered:

1. **Header-based correlation.** Set a custom header (e.g. `X-AgentCore-Run-Id`) on the MCP HTTP
   client transport, read it server-side (via `IHttpContextAccessor`) into a request-scoped
   equivalent of `AgentToolRunContext`. Rejected: it requires reordering `ClaimAgentService`/
   `WorkerClaimAgentFactory` so the `AgentRunLog` row (and thus `run.Id`) exists *before* the agent
   (and its MCP connection) is built — today the agent is built first, then `ExecuteRunAsync`
   creates the run row. It also reintroduces a fresh-connection-per-run requirement, undoing the
   singleton-client simplification below. And it means trusting a plain, unauthenticated header
   between two local services for something that ends up in an audit trail — acceptable in a demo,
   but worth avoiding if there's a cleaner option. (This specific rejection is about correlating a
   *run id* and still stands unchanged — the fresh-connection-per-call reversal did eventually
   happen anyway, but for an unrelated reason: Phase 12's identity/permission headers, `docs/plan.md`
   §5, not run correlation. The two concerns just happened to land on the same tradeoff.)
2. **Host backfills the FK after the tool call returns (chosen).** The MCP sensitive tools still
   create the `PendingAction` row themselves (same as today — they're the ones with the domain
   knowledge of what's being proposed), but leave `ProposedByAgentRunId` unset and return a small
   **structured** result instead of a free-text sentence:
   ```json
   { "pendingActionId": 42, "actionType": "CalculatePayout" }
   ```
   `ClaimAgentService.ExtractResponseDetailsAsync` already walks every `FunctionCallContent`/
   `FunctionResultContent` pair right after each tool call returns (that's the existing SignalR
   `ToolCallCompletedAsync` hook). It gains one more step there: for a tool call whose name is one
   of the three sensitive tools, parse `pendingActionId` out of the JSON result (wrapped in a
   try/catch — log and skip on parse failure, never throw, matching the existing defensive
   `response.Usage` extraction pattern in that same method) and call
   `IPendingActionRepository` to stamp `ProposedByAgentRunId = run.Id` on that row. The host
   already holds `run.Id` and an `IPendingActionRepository` reference at exactly that point — no
   new plumbing needed on the client side at all.

   This also means `AgentToolRunContext` becomes fully redundant: its only other field, `ClaimId`,
   is **already unused** today (`ClaimAgentService` sets `_runContext.ClaimId` but no tool reads
   it — confirmed by grep; every sensitive tool already takes `claimId` as an ordinary, model-
   visible parameter). `AgentToolRunContext.cs` can be deleted outright.

   **UX tradeoff worth flagging**: the model's tool result text changes from a hand-written English
   sentence ("Queued a payout of $4,000.00 for approval (PendingAction #42)...") to structured JSON
   the model itself must turn into prose for the user. This is normal, common practice for
   MCP/tool-calling agents, but it is a visible change in what the model sees mid-conversation —
   worth a quick manual check against a real Ollama run once implemented, the same way every past
   phase in `docs/plan.md` was verified against the real stack rather than assumed.

## 5. Server design (`ClaimsToolsServer`)

```csharp
// Program.cs
builder.Services.AddInfrastructure(builder.Configuration); // reuse AgentCore.Infrastructure's DI extension - EF Core + repositories
builder.Services.AddMcpServer(options =>
    {
        options.ServerInfo = new Implementation { Name = "ClaimsToolsServer", Version = "1.0.0" };
    })
    .WithToolsFromAssembly()
    .WithHttpTransport();

var app = builder.Build();
app.MapMcp("/mcp");
app.Run();
```

Each tool class keeps its existing constructor/repository dependencies and method bodies verbatim.
Only the class-level/method-level attributes change:

```csharp
[McpServerToolType]
public class CalculatePayoutTool
{
    // ctor unchanged (IPendingActionRepository only - AgentToolRunContext dependency removed, see §4)

    [McpServerTool(Name = "PayoutCalculator", Destructive = true)]
    [Description("Propose a payout amount to approve for this claim. This does NOT apply the payout — it queues it for Admin/CaseManager approval first.")]
    public async Task<PendingActionRef> CalculatePayoutAsync(
        [Description("The Id of the claim this payout relates to.")] int claimId,
        [Description("The proposed payout amount.")] decimal proposedAmount,
        [Description("Justification for this amount (e.g. how it was derived from the claim and policy coverage).")] string justification)
    {
        // ... unchanged PendingAction creation ...
        return new PendingActionRef(action.Id, nameof(PendingActionType.CalculatePayout));
    }
}

public record PendingActionRef(int PendingActionId, string ActionType);
```

> **Superseded by later phases, kept here only as the MCP-split-era illustration of the
> attribute/return-shape pattern**: `CalculatePayoutTool` no longer takes `proposedAmount` as a
> parameter at all — Phase 10 (`docs/business-logic.md`) made the amount a deterministic,
> server-computed value (`EntitlementCalculator`) precisely so the model can never supply or
> influence it. Phase 12 (`docs/plan.md` §5) also added a `DenialReason` field to
> `PendingActionRef` and a `CallerContext`/`WorkerAccessPolicy` check before any of this runs. See
> `mcp/ClaimsToolsServer/Tools/CalculatePayoutTool.cs` for the actual current shape.

The three auto tools get `[McpServerTool(Name = "...", ReadOnly = true)]` and are otherwise
unchanged (they already return a plain string, which stays a plain string — no structured-result
requirement for these).

**Open implementation question, not resolved by reflection alone:** whether `WithToolsFromAssembly()`
resolves tool-type instances from the ASP.NET Core DI container automatically, or whether each
`[McpServerToolType]` class must also be explicitly registered (e.g. `services.AddScoped<
CalculatePayoutTool>()`), the way controllers/existing `IAgentTool` classes are. Confirm against
the SDK's own samples/docs at implementation time rather than assuming either way; register
explicitly if in doubt — it's a harmless no-op if the SDK would have done it anyway.

`appsettings.json` gains a `ConnectionStrings:AgentCoreDb` entry (same value/shape as
`AgentCore.Api`'s). `ClaimsToolsServer` does **not** call `Database.Migrate()`/run the seeder —
that stays solely `AgentCore.Api`'s job (only one process should own schema application); the
compose service ordering in §7 accounts for this.

## 6. Client design (`AgentCore.Agents`)

> **This section documents the original MCP-split design** — accurate as of this phase, but the
> singleton/lazy-connection shape below was **superseded by Phase 12** (`docs/plan.md` §5, Identity
> & Authorization): once tool calls needed to carry the caller's identity for row-level
> permission enforcement, a single shared connection could no longer be reused across different
> callers. `AgentToolsFactory` now builds a fresh `McpClient` per call instead, and
> `BuildToolsetAsync` takes a `CallerIdentity` parameter that doesn't appear below. See
> `src/AgentCore.Agents/Tools/AgentToolsFactory.cs` for the real current implementation; the rest
> of this section (async factory methods, DI registration as a stateless singleton, dropping
> `IAgentTool`) is still accurate.

- **`AgentOptions`** gains `ClaimsToolsServerUrl` (default `http://localhost:5050/mcp`).
- **`AgentToolsFactory`** is rewritten, same public shape/intent, new implementation:
  ```csharp
  public class AgentToolsFactory
  {
      private static readonly HashSet<string> SensitiveToolNames =
          ["WorkerEmailSender", "EscalationEmailSender", "PayoutCalculator"];

      private readonly Lazy<Task<McpClient>> _client; // created once, reused (singleton)

      public AgentToolsFactory(IOptions<AgentOptions> options) =>
          _client = new(() => CreateClientAsync(options.Value.ClaimsToolsServerUrl));

      public async Task<List<AITool>> BuildToolsetAsync(bool includeSensitiveTools, CancellationToken ct = default)
      {
          var client = await _client.Value;
          var tools = await client.ListToolsAsync(cancellationToken: ct);
          return tools.Where(t => includeSensitiveTools || !SensitiveToolNames.Contains(t.Name))
                      .Cast<AITool>()
                      .ToList();
      }

      private static async Task<McpClient> CreateClientAsync(string endpoint) =>
          await McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions
          {
              Endpoint = new Uri(endpoint),
              TransportMode = HttpTransportMode.StreamableHttp
          }));
  }
  ```
  Registered as `AddSingleton<AgentToolsFactory>()` (alongside `Compactions`, for the same reason:
  stateless besides a lazily-created, reusable connection).
- **`WorkerClaimAgentFactory`** becomes async: `CreateReadOnlyAgentAsync()` /
  `CreateClaimProcessingAgentAsync()`, and `Build` awaits `_toolsFactory.BuildToolsetAsync(...)`.
- **`ClaimAgentService`**: the two call sites (`QueryAsync`, `ProcessClaimAsync`) now `await` the
  factory calls; `ExecuteRunAsync` gains the FK-backfill step described in §4, right after
  `ToolCallCompletedAsync` is published for a sensitive tool's result.
- **DI (`AgentCore.Agents/DependencyInjection.cs`)**: drop the six `AddScoped<IAgentTool, ...>()`
  registrations and `IAgentTool.cs` entirely; `AgentToolsFactory` moves from scoped-consumer to a
  true singleton with no scoped dependencies left to worry about.

## 7. Docker Compose / deployment

- New `mcp/ClaimsToolsServer/claims-tools-server.Dockerfile` (or reuse a shared multi-stage
  pattern) — same shape as `api.Dockerfile` (SDK stage restores/publishes, `aspnet:8.0` runtime
  stage, `certs/` CA install for the same Zscaler-inspection reason documented in `docs/plan.md`
  §10).
- New `claims-tools-server` service in `compose.yaml`: `depends_on: mssql: condition:
  service_healthy`, listens on `8081` and publishes it to the host (`8081:8081`) so it can be
  poked directly with curl or an MCP inspector during development.
- `api` gains `depends_on: claims-tools-server` and `Agent__ClaimsToolsServerUrl=http://claims-tools-server:8081/mcp`.
  Since `ClaimsToolsServer` doesn't run migrations, `claims-tools-server` should also effectively
  wait on `api` having applied migrations at least once — the simplest correct fix here is to let
  `api`'s own startup (which already runs migrations) be the thing that "is ready" also gates
  agent usage, and accept that `ClaimsToolsServer` can be technically up but return SQL errors for
  a brief window if it fields a request before `api` has finished migrating on a fresh `docker
  compose up`. Not worth a heavier fix (e.g. a shared migration-runner init container) for a demo.
- Local non-Docker dev: run `dotnet run --project mcp/ClaimsToolsServer` alongside `dotnet run
  --project src/AgentCore.Api`, same as today's two-terminal (`api` + `ui`) pattern.

## 8. What gets deleted

- `src/AgentCore.Agents/Tools/IAgentTool.cs`
- `src/AgentCore.Agents/Tools/FetchWorkerTool.cs` / `ClaimsSearchTool.cs` /
  `WorkerClaimsHistoryTool.cs` / `SendWorkerEmailTool.cs` / `SendEscalationEmailTool.cs` /
  `CalculatePayoutTool.cs` (moved, not copied)
- `src/AgentCore.Agents/AgentToolRunContext.cs` (per §4)
- The six `AddScoped<IAgentTool, ...>()` DI lines
- `tests/AgentCore.Agents.Tests/AgentToolsFactoryTests.cs`'s current body (it constructs the local
  `IAgentTool` classes directly) — replaced with a test against the rewritten `AgentToolsFactory`,
  most usefully as an integration-style test that spins up `ClaimsToolsServer` in-process via
  `WebApplicationFactory` and asserts the same "no duplicate tool names" property end-to-end
  through the real MCP `ListToolsAsync()` call, which is a *stronger* test than today's (it also
  catches a duplicate/missing `[McpServerToolType]` registration, not just a duplicated `name:`
  string).

## 9. Explicitly out of scope for this pass

- OpenTelemetry instrumentation on `ClaimsToolsServer` (tracing/metrics parity with `AgentCore.Api`
  per `docs/plan.md` §8). Worth doing once this lands, not blocking it.
- Auth/mTLS between `AgentCore.Api` and `ClaimsToolsServer` — same trust model this repo already
  uses internally (e.g. the SignalR hub, Prometheus scraping, both `[AllowAnonymous]`): a private
  compose network, no external exposure.
- Any change to the *approval-gate* invariant itself. Sensitive tools still only ever write a
  `PendingAction`; nothing here lets the MCP server or the model apply a real side effect directly.
- Workflows (Phase 8, `docs/plan.md` §11) integration — out of scope until that phase exists.

## 10. Implementation checklist

### Phase 1 — Scaffolding & DB access — ✅ Done
- [x] `ClaimsToolsServer.csproj`: added `ModelContextProtocol.AspNetCore` (brings in
  `ModelContextProtocol`/`ModelContextProtocol.Core` transitively) and project references to
  `AgentCore.Domain`/`AgentCore.Infrastructure`
- [x] `appsettings.json`: added `ConnectionStrings:AgentCoreDb`
- [x] `Program.cs`: `AddInfrastructure(builder.Configuration)`, `AddMcpServer().WithHttpTransport()`,
  `app.MapMcp("/mcp")`; removed the template's `weatherforecast` endpoint and Swagger
- [x] Added `ClaimsToolsServer.csproj` to `AgentCore.sln` under a new `mcp` solution folder

### Phase 2 — Move the tools — ✅ Done
- [x] Moved the six tool classes + `[McpServerToolType]`/`[McpServerTool(Name = ...)]` attributes
  (per §5). Resolved DI question: `WithToolsFromAssembly()` DI-resolves tool-type instances
  automatically - confirmed via a real in-process test run with **no** explicit
  `AddScoped<FetchWorkerTool>()`-style registrations added
- [x] Sensitive tools: dropped the `AgentToolRunContext` dependency, return `PendingActionRef`
  instead of a formatted string
- [x] `WithToolsFromAssembly()` wired up; verified via a real `McpClient.ListToolsAsync()` call
  (both an xUnit `WebApplicationFactory` test and a direct call against the running Docker
  container) that `tools/list` returns all six with the expected names

### Phase 3 — Client side — ✅ Done
- [x] `AgentOptions.ClaimsToolsServerUrl` + config in `appsettings.json`
- [x] Rewrote `AgentToolsFactory` per §6; deleted `IAgentTool.cs`
- [x] `WorkerClaimAgentFactory` → async `CreateReadOnlyAgentAsync`/`CreateClaimProcessingAgentAsync`
- [x] `ClaimAgentService`: awaits the now-async factory calls; added the FK-backfill step
  (`BackfillPendingActionRunIdAsync`, called from `ExtractResponseDetailsAsync`) - see the note at
  the top of this document about the `TextContent` unwrapping bug found and fixed here
- [x] Deleted `AgentToolRunContext.cs` and its DI registration
- [x] Updated `DependencyInjection.cs` (dropped `IAgentTool` registrations, `AgentToolsFactory` →
  singleton)

### Phase 4 — Tests & verification — ✅ Done
- [x] Rewrote `tests/AgentCore.Agents.Tests` per §8 (in-process `WebApplicationFactory` + a real
  `McpClient` over an in-memory `HttpMessageHandler`); 3 tests, including one asserting the exact
  six expected tool names (stronger than a bare "no duplicates" check - confirmed by deliberately
  re-introducing a duplicate `Name` and observing the MCP server's tool collection silently drop
  one tool entirely rather than list a duplicate, which the "no duplicates" test alone would have
  missed)
- [x] End-to-end against the real running Docker stack (`docker compose up --build`, then
  `POST /api/agent/claims/{id}/process` against real claims via a small HttpClient script, `curl`/
  `wget` being unavailable in this session): confirmed an auto tool call (`WorkerInformationFetcher`)
  round-tripping through MCP with real DB data, confirmed a sensitive tool call
  (`EscalationEmailSender`/`PayoutCalculator`) creating a real `PendingAction` row, and confirmed
  (after fixing the `TextContent`-unwrapping bug) `ProposedByAgentRunId` correctly stamped with the
  triggering run's id, cross-checked directly against the database and via
  `GET /api/approvals?status=AwaitingApproval` reflecting it in `queuedActions`. Verification
  artifacts (test `PendingAction` rows) were rejected afterward to leave the demo environment clean.

### Phase 5 — Compose & docs — ✅ Done
- [x] `claims-tools-server.Dockerfile` + `compose.yaml` service (per §7); also fixed
  `api.Dockerfile` to restore/publish `AgentCore.Api`'s own project graph directly instead of the
  whole `.sln` - `AgentCore.sln` now also lists `mcp/ClaimsToolsServer` and `tests/`, which the
  api image doesn't need and shouldn't have to copy into its build context. Both images verified
  with a real `docker compose build`.
- [x] Updated `README.md`'s project layout, quick-start, URLs, and troubleshooting sections
- [x] Updated `CLAUDE.md`'s architecture section
