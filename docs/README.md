# Docs index

This folder is AgentCore's design/decision record — why things are built the way they are, and
the history of how they got there. It's a companion to the root [`README.md`](../README.md)
(what the app is, architecture, how to run it) and [`ui/README.md`](../ui/README.md) (the React
UI), not a replacement for either.

| Doc | What it covers | Read it when |
|---|---|---|
| [`plan.md`](plan.md) | **The living source of truth.** Full architecture, every design decision with its reasoning, the domain model, the REST API surface, and a phase-by-phase build history with a dated Change Log. Has a [table of contents](plan.md#contents). | You need the *why* behind something, not just the *what* — or you're about to change architecture and need to know what to update. |
| [`plan-ui.md`](plan-ui.md) | Companion plan for the React UI: page/route design, what's built vs. still planned. | Working on `ui/`. |
| [`plan-agents.md`](plan-agents.md) | Companion plan for the multi-agent architecture (Phase 13, ✅ done): the specialist agent catalog, tool ownership per agent, the fixed-pipeline workflow orchestration model, entity/API changes, and a known qwen3:0.6b reliability limitation the verification checklist surfaced. | Before touching agent/workflow code, or to understand why AgentCore has more than one agent. |
| [`plan-mcp.md`](plan-mcp.md) | Why the claim/worker tools live in a separate process (`mcp/ClaimsToolsServer`) instead of in-process with the API, the run-correlation problem that split created, and how it's solved. Some sections are marked superseded by later phases — read the callout boxes. | Touching anything MCP-related, or confused about why there are two backend processes. |
| [`business-logic.md`](business-logic.md) | The real insurance rules — coverage validation, eligibility, payout calculation, escalation — implemented as deterministic C# rule engines the agent quotes but never overrides. | Touching anything payout/eligibility/coverage-related. Explains *why* an LLM must never compute money or decide eligibility itself. |
| [`knowledge-base.md`](knowledge-base.md) | Framework/agentic-systems reference (agent loop, MCP, identity propagation, human-in-the-loop, observability, and more) — general knowledge, not specific to this project. Every topic has an "AgentCore Implementation" subsection cross-referencing exactly where that concept is realized in this codebase. | You want the conceptual background behind a pattern this project uses, or you're building a *different* agent project and want reusable reference material. |
| [`api-testing.md`](api-testing.md) | Copy-paste curl for every endpoint — the full endpoint/role table, request payloads, a Swagger walkthrough. | Actually testing/exercising the API by hand. |

## Suggested reading order for someone new to this project

1. Root [`README.md`](../README.md) — what AgentCore is, the system architecture diagram, how
   the agent works, how to run it.
2. `plan.md` [§1–3](plan.md#1-overview) — overview, key decisions, solution structure.
3. `knowledge-base.md` — only if the underlying agent/MCP/HITL concepts are new to you; skip it
   if you already know the territory and just want this project's specifics (in which case its
   per-topic "AgentCore Implementation" notes are still worth skimming as a fast orientation).
4. `plan-mcp.md` and `business-logic.md` — as needed, before touching either area.
5. `api-testing.md` — when you're ready to actually call the thing.

`plan-ui.md` is only relevant once you're working in `ui/`.
