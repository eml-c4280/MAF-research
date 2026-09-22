# AgentCore V2.0

An insurance claims platform where SuperAdmins, Admins, and CaseManagers can manage
workers/claims and run an AI agent (local Ollama) to evaluate claims. A CaseManager is scoped to
only the workers assigned to them — including through the agent itself. The agent can freely
read data within that scope, but anything sensitive (emailing a worker, escalating, calculating
a payout) is queued as a `PendingAction` that a human has to approve before it actually happens.

Full architecture, design decisions, and the phase-by-phase implementation history live in
[`docs/plan.md`](docs/plan.md). This file is the practical "how do I run and test this" guide.

## Quick start

```sh
docker compose up --build
```

That single command brings up everything: SQL Server, Ollama (+ auto-pulls the `qwen3:0.6b`
model on first run), the API, the ClaimsToolsServer MCP server the agent gets its tools from (see
[`docs/plan-mcp.md`](docs/plan-mcp.md)), and the full observability stack (OpenTelemetry
Collector, Tempo, Loki, Prometheus, Grafana). The database schema and seed data (6 workers, 6
policies, 20 claims) are applied automatically on the API's first startup.

First run takes a few minutes (pulling images + the model). Subsequent runs are fast — data and
models persist in Docker volumes.

**On a corporate network with TLS inspection (Zscaler, Netskope, etc.)**: if the build fails with
`NU1301`/`certificate signed by unknown authority`, see [`certs/README.md`](certs/README.md) —
you need to drop your corporate root CA into `certs/` once.

## URLs

| What | URL | Notes |
|---|---|---|
| **Swagger** | http://localhost:8080/swagger | Try every endpoint interactively |
| **Grafana** | http://localhost:3000 | No login required (anonymous admin — see below) |
| **Prometheus** | http://localhost:9090 | Raw metrics/targets, mostly for debugging |
| **SignalR hub** | `ws://localhost:8080/hubs/agent-activity` | Live agent-run events — see [tools/signalr-test.html](tools/signalr-test.html) |
| **ClaimsToolsServer (MCP)** | http://localhost:8081/mcp | The claim/worker tools, over Streamable HTTP — poke it with an MCP inspector, not a browser. See [`docs/plan-mcp.md`](docs/plan-mcp.md) |
| **SQL Server** | `localhost,1433` (`sa` / `Your_password123`) | Only needed if you want to inspect the DB directly |

Tempo/Loki/otel-collector have no ports published to the host — you only reach them *through*
Grafana's datasources.

## Auth: JWT login

Real login, not a header trick: `POST /api/auth/login` with an email/password issues a JWT,
sent as `Authorization: Bearer <token>` on every other call. There is no `X-Role` header
anymore — a request with no token, an expired one, or a garbage one gets `401`.

**Roles are a strict hierarchy** — `SuperAdmin` ⊃ `Admin` ⊃ `CaseManager` — each level can do
everything the level below it can, plus more. A token carries every role its tier inherits (a
`SuperAdmin`'s token lists all three), so "Admin, CaseManager" in the tables below also always
means "and SuperAdmin", without needing to spell it out on every row.

- **SuperAdmin** — everything Admin can do, plus create/manage Admin and SuperAdmin accounts.
  Exactly one is seeded at startup.
- **Admin** — full CRUD on Workers/Policies, delete claims, approve/reject any `PendingAction`,
  create/manage CaseManager accounts, assign a Worker to a CaseManager.
- **CaseManager** — reads/creates/updates claims, runs the agent, approves/rejects
  `PendingAction`s — but **only for `Worker`s assigned to them** (row-level scoping, enforced
  server-side, including inside the agent's own tool calls — see `docs/plan.md` §5). Cannot
  delete Workers, edit Policies, delete Claims, or manage users.

The stack seeds exactly one **SuperAdmin** at startup:

| | |
|---|---|
| Email | `superadmin@agentcore.local` |
| Password | `SuperAdmin123!` |

(Configurable via the API's `Seed:SuperAdminEmail`/`Seed:SuperAdminPassword` — see `compose.yaml`.)
Use it to create Admin/CaseManager accounts via `/api/users` (see "Managing users" below), and to
assign Workers to CaseManagers via `/api/workers/{id}/assign-case-manager`.

**Log in and grab a token:**
```sh
curl -X POST -H "Content-Type: application/json" http://localhost:8080/api/auth/login \
  -d '{"email":"superadmin@agentcore.local","password":"SuperAdmin123!"}'
# → {"accessToken":"eyJ...", "expiresAtUtc":"...", "user":{...}}
```

Every curl example below assumes you've saved the token to a shell variable:
```sh
TOKEN=$(curl -s -X POST -H "Content-Type: application/json" http://localhost:8080/api/auth/login \
  -d '{"email":"superadmin@agentcore.local","password":"SuperAdmin123!"}' | jq -r .accessToken)

curl -H "Authorization: Bearer $TOKEN" http://localhost:8080/api/workers
```
(`jq` is only needed for the one-liner above — swap in any JSON-field extraction you like, or
just paste the `accessToken` value manually.)

In **Swagger**, click **Authorize** (top right), paste the token (no `Bearer ` prefix needed —
Swashbuckle adds it), and it's applied to every subsequent "Try it out" call.

### Managing users

Only Admin+ can manage accounts, via `UsersController` (`/api/users`):

```sh
# Create a CaseManager (Admin can only assign CaseManager; SuperAdmin can assign any role)
curl -X POST -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  http://localhost:8080/api/users \
  -d '{"name":"Alex Rivera","email":"alex@agentcore.local","password":"CaseManager123!","role":"CaseManager"}'

# List users
curl -H "Authorization: Bearer $TOKEN" http://localhost:8080/api/users

# Assign a worker to that CaseManager (Admin+ only) — id is the created user's id above
curl -X PUT -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  http://localhost:8080/api/workers/1/assign-case-manager \
  -d '{"caseManagerUserId": 2}'

# Deactivate / reset password (soft-disable only, never hard-deleted)
curl -X POST -H "Authorization: Bearer $TOKEN" http://localhost:8080/api/users/2/deactivate
curl -X POST -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  http://localhost:8080/api/users/2/reset-password -d '{"newPassword":"NewPass456!"}'
```

Once that CaseManager logs in and calls `GET /api/workers`, they'll see only worker `1` — and
asking the agent about a *different*, unassigned worker (or processing a claim for one) is
denied with `403`, both via REST and through the agent's own tool calls.

## Testing the REST API

All routes below are under `http://localhost:8080`.

"CaseManager" below is always row-scoped to their assigned workers where a worker is involved
(directly or via a claim/policy) — an Admin/SuperAdmin is unrestricted.

| Method & path | Role | What it does |
|---|---|---|
| `POST /api/auth/login` | none | Log in, get back a JWT |
| `GET /api/auth/me` | any authenticated | Current user's own profile |
| `GET /api/users` | Admin+ | List users |
| `POST /api/users` | Admin+ | Create a user (assignable roles depend on caller's own role) |
| `PUT /api/users/{id}` | Admin+ | Update a user's name/email |
| `POST /api/users/{id}/deactivate` | Admin+ | Soft-disable a user (never hard-deleted) |
| `POST /api/users/{id}/reset-password` | Admin+ | Reset a user's password |
| `GET /api/workers` | Admin+, CaseManager | List workers (CaseManager sees only their own) |
| `GET /api/workers/{id}` | Admin+, CaseManager | Get one worker (403 if not assigned to you) |
| `POST /api/workers` | Admin+ | Create a worker |
| `PUT /api/workers/{id}` | Admin+ | Update a worker |
| `PUT /api/workers/{id}/assign-case-manager` | Admin+ | Assign/unassign a worker's CaseManager |
| `DELETE /api/workers/{id}` | Admin+ | Delete a worker |
| `GET /api/policies` | Admin+, CaseManager | List all insurance policies |
| `GET /api/policies/{id}` | Admin+, CaseManager | Get one policy |
| `GET /api/policies/workers/{workerId}` | Admin+, CaseManager | Get a worker's policy |
| `POST /api/policies` | Admin+ | Create a policy |
| `PUT /api/policies/{id}` | Admin+ | Update a policy |
| `DELETE /api/policies/{id}` | Admin+ | Delete a policy |
| `GET /api/claims` | Admin+, CaseManager | List all claims |
| `GET /api/claims/{id}` | Admin+, CaseManager | Get one claim |
| `GET /api/claims/search?claimType=&status=&years=` | Admin+, CaseManager | Search claims |
| `GET /api/claims/workers/{workerId}/history?years=` | Admin+, CaseManager | Claims history + stats for a worker |
| `POST /api/claims` | Admin+, CaseManager | Create a claim |
| `PUT /api/claims/{id}` | Admin+, CaseManager | Update a claim |
| `DELETE /api/claims/{id}` | Admin+ | Delete a claim |
| `POST /api/agent/query` | Admin+, CaseManager | Free-form Q&A, one-shot, read-only tools only |
| `POST /api/agent/claims/{id}/process` | Admin+, CaseManager | Run the agent against a claim (403 if you're a CaseManager not assigned to its worker, 409 if the claim is `Disputed`) |
| `POST /api/agent/sessions` | Admin+, CaseManager | Start a new multi-turn chat session |
| `GET /api/agent/sessions` | Admin+, CaseManager | List chat sessions, most-recently-active first |
| `POST /api/agent/sessions/{id}/messages` | Admin+, CaseManager | Send the next message in a session |
| `GET /api/agent/sessions/{id}/messages` | Admin+, CaseManager | Full turn history for a session |
| `GET /api/agent/runs` / `GET /api/agent/runs/{id}` | Admin+, CaseManager | Read-only agent-run audit log |
| `GET /api/approvals?status=AwaitingApproval` | Admin+, CaseManager | List queued sensitive actions |
| `POST /api/approvals/{id}/approve` | Admin+, CaseManager | Approve — actually executes the action (returns 410 if the action expired, per `ExpiresAt`) |
| `POST /api/approvals/{id}/reject` | Admin+, CaseManager | Reject — no side effect (always allowed, even for an expired action) |
| `POST /api/approvals/batch` | Admin+, CaseManager | Apply one decision per id in one call; full per-action detail in the response |
| `GET /api/workflows` | Admin+, CaseManager | List workflow definitions (playbooks) |
| `POST /api/workflows` | Admin+ | Define a new workflow |
| `POST /api/workflows/{id}/run` | Admin+, CaseManager | Structured trigger - run a workflow with explicit inputs |
| `POST /api/workflows/chat` | Admin+, CaseManager | Chat trigger - free text routed to the best-matching workflow, or falls back to `/api/agent/query` |
| `GET /api/workflows/runs` | Admin+, CaseManager | Audit log of every workflow execution |

### Request payloads (copy-paste curl for every endpoint)

Every JSON body uses **camelCase** field names (ASP.NET Core's default `System.Text.Json`
behavior) and `DateOnly` fields as plain `"YYYY-MM-DD"` strings. Enum fields (`status`) are
case-insensitive. `$TOKEN` below is whatever account's token you logged in with — swap in a
CaseManager's token (see "Managing users" above) to see the row-level scoping in action on any
endpoint the table above marks CaseManager-accessible. IDs below (`workerId: 1`, claim `2`, etc.)
match the seeded data.

**Workers** — `/api/workers`

```sh
# List / get
curl -H "Authorization: Bearer $TOKEN" http://localhost:8080/api/workers
curl -H "Authorization: Bearer $TOKEN" http://localhost:8080/api/workers/1

# Create (Admin only)
curl -X POST -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  http://localhost:8080/api/workers \
  -d '{
        "code": "WRK-1007",
        "name": "Priya Nair",
        "role": "Electrician",
        "location": "Brisbane, AU",
        "email": "priya.nair@example.com",
        "phoneNumber": "+61 400 000 000",
        "hourlyRate": 48.50,
        "yearsOfExperience": 6,
        "isAvailable": true
      }'

# Update (Admin only) — same body shape, full replace
curl -X PUT -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  http://localhost:8080/api/workers/1 \
  -d '{
        "code": "WRK-1001",
        "name": "Ethan Brooks",
        "role": "Crane Operator",
        "location": "Sydney, AU",
        "email": "ethan.brooks@example.com",
        "phoneNumber": "+61 400 111 111",
        "hourlyRate": 52.00,
        "yearsOfExperience": 9,
        "isAvailable": false
      }'

# Delete (Admin only)
curl -X DELETE -H "Authorization: Bearer $TOKEN" http://localhost:8080/api/workers/7
```

**Policies** — `/api/policies`

```sh
curl -H "Authorization: Bearer $TOKEN" http://localhost:8080/api/policies
curl -H "Authorization: Bearer $TOKEN" http://localhost:8080/api/policies/1
curl -H "Authorization: Bearer $TOKEN" http://localhost:8080/api/policies/workers/1

# Create (Admin only) — coverageType is "Workers Compensation" or "Public Liability" in the
# seed data, but it's a free-text field, not an enum
curl -X POST -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  http://localhost:8080/api/policies \
  -d '{
        "workerId": 1,
        "policyNumber": "POL-9007",
        "provider": "SafeGuard Mutual",
        "coverageType": "Workers Compensation",
        "coverageAmount": 250000,
        "startDate": "2026-01-01",
        "endDate": "2027-01-01",
        "isActive": true
      }'

# Update (Admin only) — same body shape
curl -X PUT -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  http://localhost:8080/api/policies/1 \
  -d '{
        "workerId": 1,
        "policyNumber": "POL-9001",
        "provider": "SafeGuard Mutual",
        "coverageType": "Workers Compensation",
        "coverageAmount": 250000,
        "startDate": "2023-01-01",
        "endDate": "2026-12-31",
        "isActive": true
      }'

# Delete (Admin only)
curl -X DELETE -H "Authorization: Bearer $TOKEN" http://localhost:8080/api/policies/7
```

**Claims** — `/api/claims`

```sh
curl -H "Authorization: Bearer $TOKEN" http://localhost:8080/api/claims
curl -H "Authorization: Bearer $TOKEN" http://localhost:8080/api/claims/2

# Search — all query params optional; years defaults to 3
curl -H "Authorization: Bearer $TOKEN" \
  "http://localhost:8080/api/claims/search?claimType=Injury&status=Approved&years=5"

# Claims history + stats for a worker
curl -H "Authorization: Bearer $TOKEN" \
  "http://localhost:8080/api/claims/workers/1/history?years=5"

# Create (Admin+ or CaseManager) — status is one of Pending/UnderReview/Approved/Rejected
curl -X POST -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  http://localhost:8080/api/claims \
  -d '{
        "workerId": 1,
        "claimNumber": "CLM-260918-021",
        "claimDate": "2026-09-18",
        "claimType": "Injury",
        "amount": 2750,
        "status": "Pending",
        "description": "Twisted ankle stepping off a ladder during a routine inspection."
      }'

# Update (Admin+ or CaseManager) — same body shape, full replace
curl -X PUT -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  http://localhost:8080/api/claims/2 \
  -d '{
        "workerId": 2,
        "claimNumber": "CLM-240609-002",
        "claimDate": "2024-06-09",
        "claimType": "Equipment Damage",
        "amount": 1150,
        "status": "UnderReview",
        "description": "Damaged multimeter, no supporting incident report."
      }'

# Delete (Admin only)
curl -X DELETE -H "Authorization: Bearer $TOKEN" http://localhost:8080/api/claims/21
```

**Agent** — `/api/agent`

Claim processing now runs deterministic business rules (`docs/business-logic.md`) before the
model ever sees the claim: `CoverageChecker` (CV), `EscalationEvaluator` (ES), and
`ClaimRiskScorer` (FR) are computed server-side and handed to the model as settled facts to quote,
not something it can recompute. A coverage failure sets the claim to `CoverageRejected` in code
regardless of what the model does; a triggered escalation removes `PayoutCalculator`/
`WorkerEmailSender` from the model's toolset for that run entirely; and `PayoutCalculator` itself
computes its own amount (never a model-supplied number) and refuses outright on a coverage
failure. All three rule tools are also freely callable on their own via `/api/agent/query` (e.g.
"is claim 5 covered?").

```sh
# Free-form Q&A — read-only tools only, no PendingActions ever queued from this endpoint
curl -X POST -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  http://localhost:8080/api/agent/query \
  -d '{
        "prompt": "Which workers have had more than 2 claims in the last 3 years?"
      }'

# Process a claim — no request body, claim id is in the path
curl -X POST -H "Authorization: Bearer $TOKEN" http://localhost:8080/api/agent/claims/2/process

# Read-only agent-run audit log
curl -H "Authorization: Bearer $TOKEN" http://localhost:8080/api/agent/runs
curl -H "Authorization: Bearer $TOKEN" http://localhost:8080/api/agent/runs/1

# Multi-turn chat — distinct from the one-shot /query above; history carries forward within a
# session (the framework's own session state, persisted server-side) until you start a new one
curl -X POST -H "Authorization: Bearer $TOKEN" http://localhost:8080/api/agent/sessions
curl -X POST -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  http://localhost:8080/api/agent/sessions/1/messages \
  -d '{"message": "Remember the word zebra-quartz."}'
curl -X POST -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  http://localhost:8080/api/agent/sessions/1/messages \
  -d '{"message": "What word did I just ask you to remember?"}'
curl -H "Authorization: Bearer $TOKEN" http://localhost:8080/api/agent/sessions/1/messages
curl -H "Authorization: Bearer $TOKEN" http://localhost:8080/api/agent/sessions
```

**Approvals** — `/api/approvals`

```sh
# List — status defaults to AwaitingApproval; also accepts Approved/Rejected/Executed
curl -H "Authorization: Bearer $TOKEN" "http://localhost:8080/api/approvals?status=AwaitingApproval"

# Approve / reject — no request body, id is the PendingAction id from the list above
curl -X POST -H "Authorization: Bearer $TOKEN" http://localhost:8080/api/approvals/1/approve
curl -X POST -H "Authorization: Bearer $TOKEN" http://localhost:8080/api/approvals/1/reject

# Batch — one decision per id, applied in order; response has full detail per id, not a
# single collapsed confirmation
curl -X POST -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  http://localhost:8080/api/approvals/batch \
  -d '{"decisions":[{"id":1,"approve":true},{"id":2,"approve":false}]}'
```

Each `PendingAction` also carries an `expiresAt` (7 days from when it was queued) and an
`isExpired` flag. `approve` on an already-expired action returns `410 Gone` rather than silently
executing something a human never actually looked at — `reject` is always allowed, since
rejecting has no side effect to worry about. If the approved side effect itself throws (e.g. the
payout write-back failing), the action's status becomes `ExecutionFailed` with `executionError`
set, instead of the request coming back as an unhandled `500`.

### A full walkthrough (via Swagger)

1. Open http://localhost:8080/swagger, `POST /api/auth/login` with the seeded SuperAdmin
   credentials (see "Auth: JWT login" above), copy `accessToken` from the response, then click
   **Authorize** and paste it in.
2. `GET /api/workers` — confirm the 6 seeded workers exist.
3. `GET /api/claims/workers/1/history?years=5` — claims history + stats for worker 1.
4. `POST /api/agent/claims/2/process` — runs the real agent against claim `2`. Returns a
   recommendation and any `PendingAction`s it queued (e.g. a proposed payout or worker email).
5. `GET /api/approvals?status=AwaitingApproval` — see what got queued.
6. `POST /api/approvals/{id}/approve` — approving a payout writes the amount back onto the
   claim and marks it `Approved`; approving an email/escalation just logs a simulated send
   (`docker compose logs api` shows it) — nothing real is ever sent.

### Same walkthrough with curl

```sh
curl -H "Authorization: Bearer $TOKEN" http://localhost:8080/api/workers

curl -X POST -H "Authorization: Bearer $TOKEN" \
  http://localhost:8080/api/agent/claims/2/process

curl -H "Authorization: Bearer $TOKEN" \
  "http://localhost:8080/api/approvals?status=AwaitingApproval"

curl -X POST -H "Authorization: Bearer $TOKEN" \
  http://localhost:8080/api/approvals/1/approve
```

### What an agent run's result actually contains

Every `AgentRunLog` (returned by `/api/agent/query`, `/api/agent/claims/{id}/process`'s `run`
field, and `/api/agent/runs/{id}`) carries the full picture of that run, not just the answer:

| Field | What it is |
|---|---|
| `finalAnswer` | The user-facing response |
| `reasoningText` | The model's own "thinking" / chain-of-thought, when it exposes one — qwen3's reasoning mode does, via `Microsoft.Extensions.AI`'s `TextReasoningContent`; separate from `finalAnswer` |
| `modelId` | Which model served this run, e.g. `"qwen3:0.6b"` |
| `toolCallCount` | How many tools the agent actually invoked — counted directly, not self-reported |
| `toolCallsJson` | The full call/result sequence (tool name, arguments, result) |
| `inputTokenCount` / `outputTokenCount` / `totalTokenCount` | Real usage from the model provider (`response.Usage`, confirmed populated by Ollama) |
| `inputCost` / `outputCost` / `totalCost` | Tokens × `Agent:InputPricePerMillionTokens` / `Agent:OutputPricePerMillionTokens` (both default `0` — a local Ollama model is free; set these if you point `Agent:OllamaHost` at a paid, metered provider instead) |

## Workflows (playbooks)

A `WorkflowDefinition` is a named, reusable playbook — a fixed prompt template + input schema +
restricted tool set, configured ahead of time by an Admin. This is deliberately **not** a general
workflow builder: no branching, no loops, no one workflow triggering another. If a task needs a
different tool set or prompt, that's a new definition, not a new engine feature.

Three built-in workflows are seeded automatically:

| Workflow | Inputs | What it does |
|---|---|---|
| **Process Claim** | `claimId` | Delegates directly to `POST /api/agent/claims/{id}/process` — claim processing has its own deterministic rule-integration (coverage/escalation/risk pre-computation, the `Disputed` hard block) that a generic prompt template can't express, so this workflow wraps that endpoint rather than re-implementing it |
| **Worker Claims History** | `workerId`, optional `dateFrom`/`dateTo` | Read-only summary of a worker's claims — its allowed tool set has no sensitive tools at all |
| **Escalate High-Value Claim** | `claimId` | Assesses coverage/escalation and, if warranted, queues an escalation email — `PayoutCalculator` and `WorkerEmailSender` are not in this workflow's tool set, so the model cannot call them regardless of what it decides |

**Structured trigger** — call a workflow directly with its inputs:

```sh
# List the catalog
curl -H "Authorization: Bearer $TOKEN" http://localhost:8080/api/workflows

# Run one (id is from the list above)
curl -X POST -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  http://localhost:8080/api/workflows/2/run \
  -d '{"inputs": {"workerId": 1}}'
```

**Chat trigger** — free text is routed to the best-matching workflow by a small intent-matching
step; below a confidence threshold, or if a required input can't be resolved from the text, it
**falls back to `/api/agent/query`** rather than guessing or silently running the wrong workflow
on someone's data:

```sh
curl -X POST -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  http://localhost:8080/api/workflows/chat \
  -d '{"text": "show me the claims history for worker 1"}'
```

The response's `fellBackToQuery` flag tells you which path was taken. Every workflow execution —
structured or chat, including the fallback — is still just an ordinary `AgentRunLog` under the
hood (same `PendingAction` interception, same SignalR events); `GET /api/workflows/runs` is purely
audit metadata layered on top (which definition, which resolved inputs, and for a chat trigger,
the original text and the intent-matcher's confidence).

Defining a new workflow (Admin only):

```sh
curl -X POST -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  http://localhost:8080/api/workflows \
  -d '{
        "name": "Coverage Check",
        "description": "Runs just the CV rule family and reports the result.",
        "inputSchemaJson": "[{\"name\":\"claimId\",\"type\":\"int\",\"required\":true,\"description\":\"The claim to check.\"}]",
        "promptTemplate": "Check coverage for claim #{claimId} using CoverageChecker and report the result exactly.",
        "allowedToolNamesJson": "[\"CoverageChecker\"]",
        "isChatTriggerable": true,
        "chatTriggerHintsJson": "[\"is this claim covered\",\"check coverage\"]"
      }'
```

## Watching an agent run live (SignalR)

Swagger can't show this — the agent-run lifecycle is broadcast over a SignalR hub at
`/hubs/agent-activity`, independent of the REST call that triggered it.

**Easiest way to see it**: open [`tools/signalr-test.html`](tools/signalr-test.html) directly in
a browser (just double-click it, no server needed for the page itself). It connects to the hub,
lets you subscribe to a claim id, and shows every event as it arrives:

- `RunStarted` — the agent run began (includes the prompt)
- `ToolCallStarted` / `ToolCallCompleted` — each tool the agent invoked, in sequence
- `RunCompleted` — the final answer, plus the same model/token/cost/tool-call/reasoning fields described above
- `RunFailed` — if the run threw (e.g. Ollama unreachable)

Set the claim id, click **Connect & subscribe**, then either click **Trigger via API** on the
page or fire `POST /api/agent/claims/{id}/process` for that same claim id in Swagger — you'll
see the events land in the page in real time.

You can subscribe by **claim id** (`SubscribeToClaim`, known ahead of time — recommended) or by
**run id** (`SubscribeToRun`, only known after the run starts). The hub requires no auth header
(browsers can't attach custom headers to a WebSocket handshake) — it's read-only observational
data, the same information anyone can already read from `GET /api/agent/runs/{id}`.

Note: events publish in sequence right after the (blocking) agent call returns — not
token-by-token live generation. See `docs/plan.md` section 9 for why.

## Configuring / using Grafana

Open http://localhost:3000 — **no login needed** (anonymous admin access is enabled via
`GF_AUTH_ANONYMOUS_ENABLED` in `compose.yaml`, fine for local use, don't expose this to an
untrusted network as-is).

Everything is pre-provisioned, including cross-links between all three datasources — nothing to
configure manually.

### Datasources & how they're wired together

Connections → Data sources shows **Prometheus**, **Loki**, **Tempo**, already connected to each
other:

- **A trace → its logs**: open any span in Tempo (Explore → Tempo, or from the dashboard's
  "Recent traces" panel) and click **Logs for this span** — jumps to Loki filtered to that exact
  `trace_id`. Every log line already carries `trace_id`/`span_id` (confirmed directly against a
  running instance — EF Core command logs, the simulated email-send log, etc. all share the
  `trace_id` of the request that triggered them).
- **A trace → its metrics**: from a span, **Trace to metrics** jumps to that service's p95
  request-duration query in Prometheus.
- **A log line → its trace**: expand any log line in Loki (Explore → Loki) — its `trace_id`
  field shows a **View Trace** link straight into Tempo.

If you ever see Grafana fail to start with `Datasource provisioning error: data source not
found`, that's stale state in the `grafana-data` volume from an earlier provisioning attempt,
not a real config problem — `docker compose rm -f grafana && docker volume rm
agent-core_grafana-data`, then start again. See the comment at the top of
`observability/grafana/provisioning/datasources/datasources.yaml` for how this was diagnosed.

### The dashboard

Dashboards → **AgentCore** folder → **AgentCore Overview** — organized into rows, every panel's
query verified directly against a running instance (not guessed):

| Row | Panels |
|---|---|
| **Agent Activity** | Runs by mode/outcome, run duration (p50/p95/p99), pending actions by type, total runs, failure rate, pending actions queued |
| **API Performance** | Request rate by route, 5xx error rate by route, request duration (p50/p95/p99), active requests, responses by status code, exceptions handled |
| **Outbound Calls to Ollama** | Call rate by status, call duration (p50/p95) — isolated from the OTLP export traffic the API also makes |
| **Connections** | Active SignalR connections, active Kestrel connections, SignalR connection duration |
| **.NET Runtime** (collapsed by default) | GC collections by generation, allocation rate, thread pool queue length, exception rate |
| **Logs** | Recent logs, errors-only |
| **Traces** | Recent traces (table, clickable through to the full waterfall) |

If you want to add your own panels, every metric name/label above was confirmed against the
live `/api/v1/label/__name__/values` and `/api/v1/query` endpoints, not assumed from
documentation — query Prometheus directly (http://localhost:9090) to see the full catalog,
which also includes free auto-instrumentation you get without any extra code: `kestrel_*`,
`dns_lookup_duration_seconds_*`, `process_runtime_dotnet_*`, and more.

## Web UI

A React + TypeScript SPA in `ui/` gives every role a real interface instead of Swagger — a real
login form (not a role picker), Dashboard, Workers/Policies/Claims lists (Workers page also
assigns a worker's CaseManager, Admin+ only), "Run agent" on a claim, the Approvals queue
(approve/reject), free-form Agent Query, Workflows, and User management (Admin+ only), all wired
to the same API above. See [`ui/README.md`](ui/README.md) to run it and
[`docs/plan-ui.md`](docs/plan-ui.md) for what's built vs. still planned (detail pages, write
forms for Workers/Policies/Claims, the live SignalR panel, and Docker packaging aren't done yet).

```sh
cd ui && npm install && npm run dev   # http://localhost:5173
```

**Requires the CORS change** already applied in `Program.cs` (`AddCors`/`UseCors`) — if you're
running an older `api` image, rebuild it first: `docker compose build api && docker compose up -d api`.

## Local development (without Docker)

You can run just the API directly against a local SQL Server/Ollama instead of the full stack:

```sh
dotnet build AgentCore.sln
dotnet run --project src/AgentCore.Api
```

Set `ConnectionStrings__AgentCoreDb` and `Agent__OllamaHost` env vars (or edit
`src/AgentCore.Api/appsettings.json`) to point at wherever your SQL Server/Ollama actually are.

## Project layout

```
agent-core/
├── src/
│   ├── AgentCore.Domain          # Entities (incl. User), enums, repository interfaces,
│   │                             # WorkerAccessPolicy - no dependencies
│   ├── AgentCore.Application     # ClaimAgentService, ApprovalService, AuthService,
│   │                             # UserManagementService, WorkflowExecutionService
│   ├── AgentCore.Infrastructure  # EF Core, MSSQL migrations, repositories, seed data
│   ├── AgentCore.Agents          # Ollama-backed agent; discovers its tools from ClaimsToolsServer over MCP
│   └── AgentCore.Api             # Controllers (incl. Auth/Users), JWT auth, Swagger, SignalR hub, Program.cs
├── mcp/
│   └── ClaimsToolsServer         # Claim/worker tools (auto + sensitive), exposed over MCP - see docs/plan-mcp.md
├── tests/AgentCore.Agents.Tests  # xUnit; hosts ClaimsToolsServer in-process to test tool discovery
├── ui/                            # React + TypeScript SPA - see ui/README.md, docs/plan-ui.md
├── observability/                 # otel-collector / Tempo / Loki / Prometheus / Grafana configs
├── certs/                          # Drop a corporate root CA here if your network needs one
├── api.Dockerfile / claims-tools-server.Dockerfile / ollama.Dockerfile / compose.yaml
├── tools/signalr-test.html        # Browser test page for the live agent-activity hub
└── docs/
    ├── plan.md                   # Full architecture, decisions, and phase-by-phase history
    ├── plan-ui.md                 # The React UI: design, pages/routes, what's built vs. planned
    ├── plan-mcp.md               # The ClaimsToolsServer MCP split: design, tradeoffs, checklist
    ├── business-logic.md          # Deterministic insurance rules (coverage/eligibility/payout/escalation)
    └── knowledge-base.md          # MAF/agentic-systems framework reference, cross-referenced to this codebase
```

## Troubleshooting

- **`NU1301` / `certificate signed by unknown authority` during `docker build`** — corporate
  TLS inspection; see [`certs/README.md`](certs/README.md).
- **`ollama-init` fails pulling the model with the same certificate error** — same cause, same
  fix; `ollama.Dockerfile` picks up `certs/` too.
- **Agent calls fail with a connection error to Ollama** — check `docker compose logs ollama`
  and `docker compose logs ollama-init`; the model needs to finish pulling before `api` can use
  it (`ollama-init` must exit `0` before `api` starts, per `compose.yaml`'s `depends_on`).
- **Agent calls fail because the tool list can't be fetched** — check `docker compose logs
  claims-tools-server`; `api` reaches it at `Agent__ClaimsToolsServerUrl` (`http://claims-tools-server:8081/mcp`
  in compose). It doesn't run migrations itself, so on a *very* fresh `docker compose up` it can
  briefly serve requests before `api` has finished migrating the database - retry after a few
  seconds. See [`docs/plan-mcp.md`](docs/plan-mcp.md).
- **401 Unauthorized on any API call** — missing, expired, or invalid `Authorization: Bearer`
  token; log in again via `POST /api/auth/login`.
- **403 Forbidden** — valid token, but either the role can't do this action (e.g. a CaseManager
  trying to delete a Worker) or, for a CaseManager, the worker/claim/policy in question isn't
  assigned to them.
