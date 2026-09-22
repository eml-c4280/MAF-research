# AgentCore UI

React + TypeScript + Vite SPA for AgentCore V2.0. See [`../docs/plan-ui.md`](../docs/plan-ui.md)
for the full design; this covers just running it.

## Setup

```sh
npm install
npm run dev
```

Opens on http://localhost:5173. It talks to the API at `http://localhost:8080` by default
(same one `docker compose up` brings up at the repo root) — override by creating a
`.env.local` file here with:

```
VITE_API_BASE_URL=http://localhost:8080
```

Real login: sign in with an AgentCore account's email/password (`POST /api/auth/login`) — the
old `X-Role` picker is gone, since the API no longer accepts that header at all (docs/plan.md
§5). The API seeds one SuperAdmin at startup (`superadmin@agentcore.local` /
`SuperAdmin123!` by default — see the API's `Seed:SuperAdminEmail`/`Seed:SuperAdminPassword`
config); use it to create Admin/CaseManager accounts from the **Users** page (Admin+ only).
The issued JWT is stored in `localStorage` and sent as `Authorization: Bearer <token>` on every
request; a 401 (expired/invalid token) clears it and drops back to the login screen.

A **CaseManager** only sees the `Worker`s (and their `Claim`s/`InsurancePolicy`s) assigned to
them — an Admin+ assigns a worker to a CaseManager from the **Workers** page's "Case manager"
column. This is enforced server-side (row-level scoping + OBO propagation into the agent/MCP
layer); the UI just reflects it — a CaseManager won't see the assignment dropdown at all, only
a read-only "Assigned to you" / "Unassigned" label.

Styled with **Tailwind CSS** (see `tailwind.config.js` for the `brand` color scale) and a
left **sidebar** layout — nav links + role badge/sign-out pinned to the sidebar, content area to
the right. The **Users** nav link only appears for an Admin or SuperAdmin.

## What's implemented so far

Login, Dashboard, read-only Workers/Policies/Claims lists (Workers page also supports assigning
a worker's CaseManager), "Run agent" on a claim, the Approvals queue (approve/reject), free-form
Agent Query, the Agent Run Log audit list, Workflows, and User management (create/deactivate/
reset password). The live SignalR activity panel and Docker packaging are not yet built — see
`docs/plan-ui.md`'s checklist for what's next.
