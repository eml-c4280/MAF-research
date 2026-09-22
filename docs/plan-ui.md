# AgentCore UI — Plan

> **Status: Phases UI-1, UI-4, and UI-7 done, UI-2 mostly done — see §12 for exactly what's built.**
> Companion to [`docs/plan.md`](plan.md) (the backend plan), which stays the source of truth
> for the API/agent/observability side. This document covers a new React SPA that consumes the
> already-implemented REST API (Workers/Policies/Claims/Agent/Approvals/AgentRunLogs) via axios.
> Given a stated time constraint, implementation of the highest-value read/agent/approval
> screens started immediately after this plan was drafted, ahead of a full review round -
> §2's decisions (TypeScript, Vite, React Query, etc.) were all taken as proposed. Comment on
> anything you want changed and it'll be adjusted; nothing here is final.

## 1. Overview & goals

A single-page app, in a new `agent-core/ui/` folder (sibling to `src/`), that gives Admins and
Managers a real interface instead of Swagger: browse/manage Workers, Policies, and Claims; run
the agent against a claim and watch it work live; and approve/reject the sensitive actions it
queues. It talks to the existing API exactly as Swagger/curl do today — same `X-Role` header,
same endpoints, no new backend business logic. **React + axios are already decided** (per your
request); everything else below is a recommendation for you to accept or override in review.

## 2. Key decisions

| Decision | Choice | Notes |
|---|---|---|
| Framework | React 18 | As requested. |
| HTTP client | axios | As requested — one configured instance, see §3. |
| Language | TypeScript (Recommended) | Types mirror the existing `Contracts/*.cs` DTOs almost 1:1, which catches API-shape mistakes at compile time instead of at runtime. Plain JS is a fine alternative if you'd rather skip the setup. |
| Build tool | Vite (Recommended) | Fast dev server, minimal config, not deprecated (unlike CRA). |
| Package manager | npm | No extra tool to install; consistent with everything else in this repo being stock tooling. |
| Routing | `react-router-dom` v6 | Standard choice, nothing exotic needed. |
| Data fetching / state | `@tanstack/react-query` (Recommended) | This app is almost entirely CRUD-over-REST; React Query's caching, loading/error state, and "refetch after mutation" handling removes a lot of otherwise-repeated `useEffect`/`useState` boilerplate across 6 resource types. Plain hooks are the simpler alternative if you'd rather avoid the extra dependency. |
| Styling | Tailwind CSS | Switched from plain CSS at the user's request. A `brand` (indigo) color scale plus Tailwind's stock `slate` neutrals - light surfaces, one blue-family accent for anything interactive/selected, not a literally-blue app. |
| Layout | Left sidebar navigation | Switched from a top nav bar at the user's request. Fixed-width `Sidebar` (logo, nav links, role badge + "Switch role" pinned at the bottom) + a scrollable content area to its right. |
| Real-time | `@microsoft/signalr` npm package | Same library already validated in `tools/signalr-test.html`; same hub, same events. |
| Auth | Real JWT login (mirrors the backend) | **Updated 2026-09-22**: the backend replaced the `X-Role` header model with real JWT auth (`docs/plan.md` §5) — the UI now has an actual login form, not a role picker. See §6. |
| Testing | Light (Vitest + React Testing Library on 1-2 critical pieces only) | See §10 — deliberately minimal given the backend already carries the "verify against real infra" rigor and UI time is the current constraint. |

## 3. Backend change required first: CORS

**Checked `Program.cs` — there is no CORS policy configured today.** A browser-based SPA on a
different origin (`http://localhost:5173` in dev, a separate container/port once dockerized)
will be blocked by the browser on every call until this exists. This is a small, required
addition, not optional:

```csharp
builder.Services.AddCors(options => options.AddPolicy("Ui", policy => policy
    .WithOrigins(builder.Configuration["Cors:AllowedOrigin"] ?? "http://localhost:5173")
    .AllowAnyHeader()
    .AllowAnyMethod()));
// ...
app.UseCors("Ui"); // before UseAuthorization
```

`Cors__AllowedOrigin` becomes a new env var, set differently for local dev vs. the `ui` compose
service (§9). This is the one piece of backend work bundled into the UI plan; everything else
is new frontend code only.

> **Update (implemented):** landed as `Cors:AllowedOrigins` (plural, comma-separated), defaulting
> to `http://localhost:5173,http://localhost:5174` rather than just `5173`. Found this the hard
> way: a leftover background `npm run dev` was still holding port 5173 when the user started
> their own, so Vite silently fell back to 5174 - which wasn't allow-listed, so the browser
> genuinely blocked every call. Widened the default to cover Vite's actual fallback behavior
> instead of assuming a single fixed port, and killed the stray background instance so `5173`
> is free again for future runs.

## 4. Project structure

```
agent-core/ui/
├── index.html
├── vite.config.ts
├── package.json
├── .env.example                 # VITE_API_BASE_URL=http://localhost:8080
├── Dockerfile                   # ui.Dockerfile equivalent, see §9
└── src/
    ├── main.tsx
    ├── App.tsx                  # routes
    ├── api/
    │   ├── client.ts            # axios instance + request/response interceptors
    │   ├── types.ts             # mirrors Contracts/*.cs DTOs
    │   ├── auth.ts / users.ts   # login/me; user management (Admin+)
    │   ├── workers.ts / policies.ts / claims.ts / agent.ts / approvals.ts / agentRunLogs.ts / workflows.ts
    ├── auth/
    │   ├── RoleContext.tsx      # AuthProvider/useAuth — { user, roles, login, signOut, hasRole } + localStorage
    │   └── RoleGate.tsx         # real login form + route guard (file kept from the pre-JWT
    │                            # picker era - only the exported names/content changed, not the
    │                            # path, to avoid leaving orphaned files around)
    ├── hooks/
    │   └── useAgentActivity.ts  # SignalR connection + live event log for one claim
    ├── pages/
    │   ├── DashboardPage.tsx
    │   ├── workers/  (ListPage — includes the assign-case-manager control, Admin+ only; FormPage, DetailPage)
    │   ├── policies/ (ListPage, FormPage)
    │   ├── claims/   (ListPage, FormPage, DetailPage — includes "Run agent" + live panel)
    │   ├── approvals/ (ApprovalsPage)
    │   ├── agent/    (QueryPage, RunLogsListPage, RunLogDetailPage)
    │   ├── workflows/ (WorkflowsPage)
    │   └── users/    (UsersPage — Admin+ only: list/create/deactivate/reset-password)
    ├── components/   # Sidebar, LoadingState, ErrorBanner
    └── styles/
```

## 5. Pages, routes, and exactly which existing endpoint each one calls

| Route | Page | Calls | Roles |
|---|---|---|---|
| `/` (gate, not a route) | `RoleGate` | `POST /api/auth/login` | — |
| `/users` | Users list + create/deactivate/reset-password | `GET`/`POST /api/users`, `POST /api/users/{id}/deactivate`\|`reset-password` | **Admin+ only** |
| `/dashboard` | `DashboardPage` | `GET /api/workers`, `GET /api/claims`, `GET /api/approvals?status=AwaitingApproval` (counts derived client-side) | both |
| `/workers` | Workers list | `GET /api/workers` (server-scoped to assigned workers for a CaseManager); `PUT /api/workers/{id}/assign-case-manager` from an inline dropdown | both — the dropdown itself only renders for Admin+ |
| `/workers/:id` | Worker detail | `GET /api/workers/{id}`, `GET /api/policies/workers/{id}`, `GET /api/claims/workers/{id}/history` | both |
| `/workers/new`, `/workers/:id/edit` | Worker form | `POST` / `PUT /api/workers` | **Admin only** |
| `/policies` | Policies list | `GET /api/policies` | both |
| `/policies/new`, `/policies/:id/edit` | Policy form | `POST` / `PUT /api/policies` | **Admin only** |
| `/claims` | Claims list + search filters | `GET /api/claims` or `GET /api/claims/search` | both |
| `/claims/new`, `/claims/:id/edit` | Claim form | `POST` / `PUT /api/claims` | both (delete is Admin-only) |
| `/claims/:id` | Claim detail | `GET /api/claims/{id}`, **"Run agent" button** → `POST /api/agent/claims/{id}/process`, live panel (§7), queued actions for this claim (filtered from `GET /api/approvals`) | both |
| `/approvals` | Approvals queue | `GET /api/approvals?status=`, `POST /api/approvals/{id}/approve`\|`reject` | both |
| `/agent/query` | Free-form Q&A | `POST /api/agent/query` — renders `finalAnswer`, `reasoningText`, tokens, cost | both |
| `/agent/runs` | Run log audit list | `GET /api/agent/runs` | both |
| `/agent/runs/:id` | Run log detail | `GET /api/agent/runs/{id}` | both |

Nothing here is a new backend capability — every call already exists and is documented in
`README.md`'s payload reference.

## 6. Auth / role handling — important framing

> **Updated 2026-09-22**: the backend replaced the `X-Role` header trust model with real JWT
> auth (`docs/plan.md` §5 — SuperAdmin ⊃ Admin ⊃ CaseManager, row-level worker scoping, OBO
> propagation into the agent/MCP layer). The paragraph below describes the *old* model and is
> kept only as history; see the replacement immediately after it.

~~There is no real login on the backend..., `RoleGate` is a convenience picker...~~ (superseded)

**Current model**: `RoleGate` is a real login form (email + password) calling
`POST /api/auth/login`. On success, the returned JWT is stored in `localStorage`
(`agentcore.accessToken`) alongside the returned `UserDto` (`agentcore.user`, for display only —
never trusted for authorization). Axios's request interceptor attaches
`Authorization: Bearer <token>` to every outgoing call — there is no `X-Role`/`X-Actor-Name`
header left anywhere in the app.

`RoleContext.tsx` (kept at its original path/filename, only the exported symbols changed) now
exposes `useAuth()` → `{ user, roles, login, signOut, hasRole }`. `roles` is decoded client-side
from the JWT's role claim (no signature check — that's the API's job; a client-side decode is
only ever used for which buttons/nav links to show, never as an actual authorization decision).
Because a SuperAdmin's token carries `["SuperAdmin","Admin","CaseManager"]` and an Admin's
carries `["Admin","CaseManager"]`, `hasRole("Admin")` correctly returns `true` for both, with no
SuperAdmin-specific UI logic needed anywhere — the same inherited-claims trick the backend uses
for `[Authorize(Roles=...)]`.

- Response interceptor: `401` (missing/expired/invalid token) → clear the stored token, fire a
  `window` event (`agentcore:unauthorized`) that `AuthProvider` listens for to clear its React
  state and bounce back to `RoleGate`, since an axios interceptor can't call a React hook
  directly. `403` → an inline "not permitted" message rather than a crash (unchanged from before).
- Hiding/disabling Admin-only buttons (and the entire `/users` page, the Workers page's
  assign-case-manager dropdown) for a CaseManager is **cosmetic only** — real enforcement is
  server-side (`[Authorize(Roles=...)]`, `WorkerAccessPolicy`, OBO through MCP). This document
  says so explicitly so it's never mistaken for actual access control while reviewing or
  extending the UI later.
- A CaseManager only ever sees the `Worker`s assigned to them via `GET /api/workers` (the API
  scopes this server-side); the UI does not additionally filter anything client-side — there is
  nothing to filter, the API already returns the right rows.

## 7. Live agent activity panel

Reuses the SignalR contract already built and validated (`docs/plan.md` §9,
`tools/signalr-test.html`): `RunStarted`, `ToolCallStarted`, `ToolCallCompleted`, `RunCompleted`
(carrying `modelId`/token counts/cost/`reasoningText`), `RunFailed`. A `useAgentActivity(claimId)`
hook connects to `/hubs/agent-activity`, calls `SubscribeToClaim`, and returns an ordered event
list + latest status. `ClaimDetailPage` renders it as a live feed next to the "Run agent"
button — this is the one page where the UI shows something Swagger structurally can't.

## 8. Error / loading / empty states

Shared, reused across all list/detail pages rather than repeated per-page: a loading spinner
while a query is in flight, an `ErrorBanner` for failed calls (showing the API's message body
for 400s — e.g. the claim-status validation error the backend already returns), and a plain
"no records yet" empty state for lists. Mutation results (create/update/delete/approve/reject)
surface as a small toast rather than a full page reload.

## 9. Docker integration

- New `ui.Dockerfile`: multi-stage, `node:20` build stage → `nginx:alpine` serve stage for the
  static build output. **One nuance worth flagging now**: Vite bakes `VITE_*` env vars in at
  *build* time, which would mean rebuilding the image just to point it at a different API URL.
  To avoid that, the nginx stage will serve a tiny `env.js` generated from the container's actual
  env vars at *startup* (a one-line entrypoint script), so the same built image works against
  different `VITE_API_BASE_URL`/`Cors__AllowedOrigin` values in dev vs. compose without a rebuild.
- New `ui` service in `compose.yaml`: builds from `ui.Dockerfile`, publishes a host port (e.g.
  `5173:80`), `depends_on: api`, `API_BASE_URL: "http://localhost:8080"` (the *browser's* address
  for the API, not the compose-internal one — the browser runs on your host, not inside the
  compose network).
- `certs/`'s corporate-CA fix applies here too, only if `npm install` needs it during the Docker
  build (same Zscaler consideration as `api.Dockerfile`/`ollama.Dockerfile`).

## 10. Testing approach (deliberately light)

Given where the remaining time is, this plan does **not** propose a large UI test suite. The
project's existing standard — verify against the real running stack rather than mocks — still
applies, just manually: click through each page against `docker compose up`'s real API, the same
way Phases 1-7 were verified. If time allows afterward, two things are worth a couple of
automated tests specifically because a bug there is silent and easy to miss: the axios
interceptor actually attaching `Authorization: Bearer <token>`, and the JWT persisting across a
page reload.

## 11. Explicitly out of scope for this pass

- ~~Workflows (Phase 8) UI — no backend exists yet~~ — **done**: `WorkflowsPage` shipped once
  the backend Workflows phase landed.
- ~~Real authentication (login, passwords, JWT, sessions) — unchanged decision from the backend
  plan~~ — **done**: the backend added real JWT auth (`docs/plan.md` §5) and the UI migrated to
  match (§6, §12's new phase below).
- Any charting/metrics UI — Grafana already owns that (`docs/plan.md` §8); the Dashboard page
  here is a business-data summary, not a metrics dashboard.
- The live SignalR activity panel (§7) and Docker packaging (§9) — still not built, see §12.

## 12. Suggested build order

Given limited remaining time, this is ordered so that stopping after any phase still leaves a
working, demoable app — later phases add capability, they don't fix earlier ones.

- [x] **Phase UI-1 — Scaffolding.** ✅ Done. `agent-core/ui/`: Vite + React 18 + TypeScript,
  `@tanstack/react-query`, `react-router-dom`; `api/client.ts` (axios instance + request
  interceptor attaching `X-Role`/`X-Actor-Name` from `localStorage`, response interceptor
  clearing them on `401`); `RoleContext`/`RoleGate`; `NavBar` + routing shell. Backend CORS
  change landed in `Program.cs` (`AddCors`/`UseCors`, `Cors:AllowedOrigins` config, defaulting
  to `http://localhost:5173`) and verified for real — a Node `fetch` sent with
  `Origin: http://localhost:5173` against the rebuilt, redeployed `api` container came back
  with `Access-Control-Allow-Origin: http://localhost:5173` on every route exercised.
- [x] **Phase UI-2 — Read-only screens — mostly done.** Implemented and verified against the
  live API/database (real response bodies checked field-by-field against `api/types.ts`):
  Dashboard (worker/claim/pending-approval counts + claims-by-status breakdown), Workers list,
  Policies list (joined to worker name client-side), Claims list, Agent Run Logs list. **Not
  built yet**: per-record detail pages (`/workers/:id`, `/agent/runs/:id`) and the claims
  search/filter UI — only the plain lists exist so far.
- [ ] **Phase UI-3 — Write screens.** Not started — no create/edit/delete forms yet.
- [x] **Phase UI-4 — Agent interaction — mostly done.** Agent Query page (verified with a real
  Ollama call - `qwen3:0.6b`, real `reasoningText`/token counts returned) and a "Run agent"
  action wired into the Claims list itself rather than a separate Claim Detail page (no Claim
  Detail page exists yet), showing the returned recommendation/queued actions/run stats inline.
  Approvals page (list by status + approve/reject) verified end-to-end - a real
  `SendEscalationEmail` action was rejected through it and came back with the correct
  `Rejected`/`decidedByRole`/`decidedAtUtc` fields.
- [ ] **Phase UI-5 — Live activity.** Not started — `useAgentActivity`/SignalR panel not built;
  `tools/signalr-test.html` still covers this in the meantime.
- [ ] **Phase UI-6 — Docker packaging.** Not started — no `ui.Dockerfile`/compose service yet;
  run it locally via `npm run dev` (see `ui/README.md`).
- [x] **Phase UI-7 — JWT auth migration (2026-09-22).** ✅ Done. Followed the backend's Phase 12
  (`docs/plan.md` §5) replacing `X-Role` with real JWT auth entirely — the UI could not
  authenticate against the new backend at all until this landed. `RoleGate` became a real login
  form (`POST /api/auth/login`); `RoleContext.tsx`'s `useRole()`/`{role, actorName}` became
  `useAuth()`/`{user, roles, login, signOut, hasRole}`, decoding the JWT's role claim
  client-side for nav/button gating only (never trusted for real authorization); `client.ts`'s
  interceptor sends `Authorization: Bearer <token>` instead of `X-Role`/`X-Actor-Name`, and its
  401 handler now fires a `window` event `AuthProvider` listens for instead of writing directly
  to a shared module-level variable. New `api/auth.ts` (login/me) and `api/users.ts` (list/
  create/update/deactivate/reset-password). New `UsersPage` (Admin+ only — gated inline with an
  `ErrorBanner`, same "hidden/disabled for the wrong role is cosmetic only" pattern as
  `WorkflowsPage`'s existing `hasRole("Admin")` check) mirroring `UserManagementService.
  CanAssign`'s hierarchy client-side (SuperAdmin can create any role, Admin only CaseManager) as
  a UI convenience — the real enforcement stays server-side. `WorkersListPage` gained a "Case
  manager" column: an inline assign/unassign dropdown for Admin+ (populated from active
  CaseManager users), a plain "Assigned to you"/"Unassigned" label otherwise (a CaseManager only
  ever sees their own assigned workers anyway, server-scoped). `Sidebar` shows the signed-in
  user's name + highest role badge and a "Sign out" button (was "Switch role"); the "Users" nav
  link only renders for Admin+. `roleBadgeClasses` gained a third color for `SuperAdmin`.
  Verified against the real running stack: logged in as the seeded SuperAdmin, created a
  CaseManager account from the Users page, assigned a worker to them from the Workers page,
  confirmed the CaseManager's own login only ever lists that one worker.
- [x] **Phase UI-8 — Multi-agent architecture adaptation (2026-09-22).** ✅ Done. Brought the UI
  up to date with the backend's Phase 13 (`docs/plan-agents.md`) - the four-specialist agent
  catalog and multi-step workflow pipeline had zero UI exposure before this. `api/types.ts`:
  `PendingActionType` gained `NotifyCaseManager`; `KNOWN_TOOL_NAMES` gained `CaseManagerNotifier`;
  new `AgentCatalogEntryDto` and `AgentRunStepDto`; `ProcessClaimResponse` gained a `steps` array
  (one entry per specialist agent that ran, alongside the existing `run` field which stays "the
  final step's answer" for compatibility). `api/agent.ts` gained `agentCatalogApi` (`GET
  /api/agents`, `POST /api/agents/{agentName}/query`) - shapes verified field-by-field against
  the real running API, not guessed. `ClaimsListPage`'s "Run agent" result now renders a
  "Pipeline" section - each specialist's name, outcome badge, tool-call count, and final answer,
  in order - above the existing final-step run details, since processing a claim is a fixed
  three-agent sequence now, not one monolithic call. `AgentQueryPage` gained a mode toggle: the
  existing multi-turn "Chat with Claims Agent" thread, or a new "Ask a specialist" one-shot panel
  (agent picker from the live catalog + a single question, always read-only regardless of which
  agent is asked, no history between questions). **Not done, deliberately**: authoring/viewing a
  multi-step `WorkflowDefinition`'s own `Steps` from the UI - `CreateWorkflowRequest` itself has
  no `Steps` field yet (multi-step workflows are still seeded, not authored via the API), and a
  generic `POST /api/workflows/{id}/run` response has no per-step breakdown the way
  `ProcessClaimResponse` now does - both are backend gaps, out of scope for a UI-only pass, and
  already tracked as deferred in `docs/plan-agents.md` §11/§14.

**Current state**: a real, usable app is running against the live stack - Dashboard, all three
read-only lists, Agent Query (chat + ask-a-specialist), "Run agent" on Claims (with the full
per-specialist pipeline breakdown), the full Approvals loop, Workflows, real login + User
management + worker/case-manager assignment, all verified against the actual running
API/database/Ollama, not mocked. Remaining work, in priority order: detail pages + claim
search/filters (finishing UI-2), then write forms for Workers/Policies/Claims (UI-3), then the
live panel and Docker packaging (UI-5/UI-6) as originally planned.

---

**Please review and tell me what to change** — decisions in §2 you want flipped (JS instead of
TS, plain hooks instead of React Query, etc.), pages you want added/dropped from §5, or a
different priority order in §12. Once you confirm, I'll add a matching checklist section to
`docs/plan.md` and start on Phase UI-1.
