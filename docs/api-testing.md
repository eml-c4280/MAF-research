# API testing reference

Copy-paste curl for every endpoint. This is a reference, not the product overview — see the root
[`README.md`](../README.md) for what AgentCore is and its architecture, and Swagger
(`http://localhost:8080/swagger`) for interactive testing without typing any curl at all.

Every JSON body uses **camelCase** field names (ASP.NET Core's default `System.Text.Json`
behavior) and `DateOnly` fields as plain `"YYYY-MM-DD"` strings. Enum fields (`status`) are
case-insensitive. IDs below (`workerId: 1`, claim `2`, etc.) match the seeded data.

## Get a token first

Every example below assumes `$TOKEN` is set:

```sh
TOKEN=$(curl -s -X POST -H "Content-Type: application/json" http://localhost:8080/api/auth/login \
  -d '{"email":"superadmin@agentcore.local","password":"SuperAdmin123!"}' | jq -r .accessToken)
```

`$TOKEN` is whatever account you logged in with — swap in a CaseManager's token (see "Managing
users" below) to see the row-level scoping in action on any endpoint the table below marks
CaseManager-accessible.

## Endpoint table

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

## Managing users

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

## Request payloads

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

Claim processing runs deterministic business rules (`docs/business-logic.md`) before the model
ever sees the claim: `CoverageChecker` (CV), `EscalationEvaluator` (ES), and `ClaimRiskScorer`
(FR) are computed server-side and handed to the model as settled facts to quote, not something it
can recompute. A coverage failure sets the claim to `CoverageRejected` in code regardless of what
the model does; a triggered escalation removes `PayoutCalculator`/`WorkerEmailSender` from the
model's toolset for that run entirely; and `PayoutCalculator` itself computes its own amount
(never a model-supplied number) and refuses outright on a coverage failure. All three rule tools
are also freely callable on their own via `/api/agent/query` (e.g. "is claim 5 covered?").

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

**Workflows** — `/api/workflows` (concept + seeded workflows are in the root README)

```sh
# List the catalog
curl -H "Authorization: Bearer $TOKEN" http://localhost:8080/api/workflows

# Structured trigger — run one with explicit inputs (id is from the list above)
curl -X POST -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  http://localhost:8080/api/workflows/2/run \
  -d '{"inputs": {"workerId": 1}}'

# Chat trigger — free text routed to the best-matching workflow, or falls back to /api/agent/query
curl -X POST -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  http://localhost:8080/api/workflows/chat \
  -d '{"text": "show me the claims history for worker 1"}'

# Define a new workflow (Admin only)
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

The response's `fellBackToQuery` flag tells you which path the chat trigger took. Every workflow
execution — structured or chat, including the fallback — is still just an ordinary `AgentRunLog`
under the hood (same `PendingAction` interception, same SignalR events); `GET /api/workflows/runs`
is purely audit metadata layered on top (which definition, which resolved inputs, and for a chat
trigger, the original text and the intent-matcher's confidence).

## A full walkthrough (via Swagger)

1. Open http://localhost:8080/swagger, `POST /api/auth/login` with the seeded SuperAdmin
   credentials (see the root README's "Auth" section), copy `accessToken` from the response,
   then click **Authorize** and paste it in.
2. `GET /api/workers` — confirm the 6 seeded workers exist.
3. `GET /api/claims/workers/1/history?years=5` — claims history + stats for worker 1.
4. `POST /api/agent/claims/2/process` — runs the real agent against claim `2`. Returns a
   recommendation and any `PendingAction`s it queued (e.g. a proposed payout or worker email).
5. `GET /api/approvals?status=AwaitingApproval` — see what got queued.
6. `POST /api/approvals/{id}/approve` — approving a payout writes the amount back onto the
   claim and marks it `Approved`; approving an email/escalation just logs a simulated send
   (`docker compose logs api` shows it) — nothing real is ever sent.

Same walkthrough with curl:

```sh
curl -H "Authorization: Bearer $TOKEN" http://localhost:8080/api/workers

curl -X POST -H "Authorization: Bearer $TOKEN" \
  http://localhost:8080/api/agent/claims/2/process

curl -H "Authorization: Bearer $TOKEN" \
  "http://localhost:8080/api/approvals?status=AwaitingApproval"

curl -X POST -H "Authorization: Bearer $TOKEN" \
  http://localhost:8080/api/approvals/1/approve
```

## What an agent run's result actually contains

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
