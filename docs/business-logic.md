# AgentCore — Insurance Business Logic

**Status: partially implemented, per rule family — see `docs/plan.md` Phase 10 for the full
implementation notes and verification.** This document originally shipped as a proposal; the
architectural core of §1 (rules decide, the agent explains) and most of §4's rule catalogue are
now real code, deliberately scoped rather than built exhaustively:

| Family | Status |
|---|---|
| **CV** (coverage validation) | Implemented: CV-1/2/3/4 (`AgentCore.Domain.Rules.CoverageValidator`). Not CV-5 (multi-policy ambiguity) - `IInsurancePolicyRepository` only supports one policy per worker. |
| **ES** (escalation) | Implemented: ES-1/3/5/6 (`EscalationEvaluator`). Not ES-2/ES-4 - no severity or time-off-work fields exist. |
| **FR** (fraud/anomaly signals) | Implemented: FR-1/4/5, the code-computable subset (`ClaimRiskScorer`). FR-3 stays agent judgement, by design (§5). Not FR-2/FR-6 - no termination-date or witness/report fields exist. |
| **PY** (payout) | Implemented, **deliberately simplified**: caps at remaining policy cover, applies a flat standard excess, versions and persists its inputs (PY-1/PY-4/PY-6 - `EntitlementCalculator`). Not PY-2/PY-3 (PIAWE-based weekly-benefit step-down, partial-capacity offset) or PY-5 (third-party recovery) - genuinely need schema this pass didn't add (`Worker.PIAWE`, `WorkCapacity`, `ThirdPartyInvolved`), which this document's own §8 warned would be prerequisites. |
| **EL** (eligibility/liability) | Not implemented - by the doc's own design this family is advisory/human-decided, not a deterministic rule; nothing here required code. |
| **SL** (timeliness/SLA) | Not implemented - needs statutory-clock tracking this pass doesn't add. |
| Expanded lifecycle (§3) | Only `CoverageRejected` and `Disputed` added to `ClaimStatus`; the full liability/RTW/reopened graph needs work-capacity/return-to-work tracking that doesn't exist yet. |

The rest of this document is kept as originally written below, since the proposal's reasoning and
the illustrative numbers/thresholds are still the reference for what's implemented and why.

**Status (original): proposal, nothing here is implemented yet.** `docs/plan.md` describes what
AgentCore *is*; this document proposes what it should actually *decide*. Right now the agent's
entire business logic is one sentence in a prompt ("give a clear recommendation (approve, reject,
or escalate) with reasoning") — there are no coverage checks, no eligibility rules, no payout
calculation, and no escalation thresholds. Everything below is a suggestion to be argued with,
not a spec to implement as-is.

**A caution up front, because it matters more than anything else in this document.** Australian
workers' compensation is statutory and differs by jurisdiction — NSW (SIRA/icare), VIC
(WorkSafe), QLD (WorkCover) and the rest each have their own entitlement rules, step-down
schedules, and statutory timeframes. Every threshold, percentage and day-count below is an
**illustrative demo default, invented to make the rules concrete**. None of it is drawn from
scheme legislation and none of it should survive contact with a real claims team unchanged. The
*shapes* of the rules are the contribution here; the numbers are placeholders.

---

## 1. The architectural decision that matters most

**Rules decide. The agent explains.**

An LLM must not be the thing that determines eligibility or computes money. Those decisions are
regulated, auditable, must be reproducible on identical inputs, and must be defensible years
later in a dispute. A model that gives a slightly different answer on a re-run, or that can be
talked into a different number by how a claim description is worded, cannot do that job.

What an LLM is genuinely good at here, and what the rules engine is bad at:

| The agent (LLM) is good at | Deterministic code must own |
|---|---|
| Reading unstructured text — claim descriptions, medical certificates, incident reports | Coverage validation (dates, policy type, limits) |
| Spotting inconsistencies between a description and the structured data | Eligibility / liability determination |
| Summarising a worker's claim history into something a human can act on | Any calculation that produces a dollar amount |
| Flagging "this looks unusual, a human should look" | Statutory timeframes and SLA clocks |
| Drafting the worker email / escalation note | Which approval tier a decision needs |
| Explaining, in plain English, *why* the rules produced an outcome | The audit record of what was decided and on what basis |

This maps onto AgentCore's existing tool split, with one addition. Today there are two
categories; there should be three:

1. **Auto tools** (exist today) — read-only lookups. `FetchWorker`, `SearchClaims`,
   `GetWorkerClaimsHistory`. Agent calls freely.
2. **Rule tools** (new) — deterministic functions that compute a definitive answer in code and
   return it as a structured result. `CheckCoverage`, `AssessEligibility`, `CalculateEntitlement`,
   `ScoreClaimRisk`. The agent may call these and must quote their output, but **cannot
   override or recompute them**. Their results, not the model's opinion, are what gets persisted
   as the decision basis.
3. **Sensitive tools** (exist today) — anything with a real-world effect. `SendWorkerEmail`,
   `SendEscalationEmail`, `CalculatePayout`. Queued as a `PendingAction` for human approval.

The key change: `CalculatePayout` currently lets the *model* propose an arbitrary amount, which
a human then approves. That's backwards. The amount should come from a rule tool; the sensitive
tool should only queue *applying* the already-computed amount. The human is approving "pay the
calculated $X", not "the model thinks $X sounds right".

---

## 2. Where the current model is too thin

Several rules below can't be written against today's schema. These gaps are worth fixing first,
because most of them are cheap and they unblock everything else.

**On `Claim`:**
- `IncidentDate` and `ReportedDate`, separate from `ClaimDate` (lodgement). Workers' comp cares
  enormously about the gaps between these three — they drive statutory clocks and are a primary
  fraud signal. Today there's only one date, so *no* timeliness rule can be written.
- `Jurisdiction` (NSW/VIC/QLD/…). `Worker.Location` is free text (`"Sydney, AU"`); scheme rules
  are per-state, so this needs to be a real field on the claim or worker.
- Split `Amount` into `MedicalCosts`, `WeeklyBenefits` and `OtherCosts`. One blended number
  can't support entitlement rules, because the two are calculated completely differently and
  have different caps.
- `ReserveEstimate` — the insurer's estimate of ultimate cost, which is what actually drives
  escalation tiering in practice, not the amount claimed so far.
- `LiabilityDecision` + `LiabilityDecidedAtUtc`, distinct from `Status`. "Liability accepted" and
  "claim closed" are different axes; collapsing them into one enum loses information.
- `WorkCapacity` (none / partial / full) and `CertificateExpiryDate`.
- `ThirdPartyInvolved` — drives recovery/subrogation, which is real money.

**On `Worker`:**
- `PIAWE` (pre-injury average weekly earnings) or at least contracted hours/week. `HourlyRate`
  alone can't produce a weekly benefit figure.
- `IsAvailable` is currently a generic boolean. In this domain it's really "has work capacity" —
  worth renaming and tying to `WorkCapacity` above rather than leaving it ambiguous.

**On `ClaimStatus`:** `Pending → UnderReview → Approved/Rejected` is too coarse for a real
claim. See the lifecycle below.

---

## 3. Proposed claim lifecycle

```
Lodged
  ├─→ CoverageRejected        (no valid policy — terminal, rule-decided)
  └─→ UnderAssessment
        ├─→ ProvisionalLiabilityAccepted   (pay while deciding — see EL-4)
        ├─→ LiabilityAccepted
        │     ├─→ Open ──→ RTWInProgress ──→ Closed
        │     └─→ Reopened  (new treatment/deterioration after closure)
        ├─→ LiabilityDenied   (terminal unless disputed)
        └─→ Disputed          (worker/employer challenges — exits automation entirely)
```

Two things worth calling out:

- **`CoverageRejected` is rule-decided and terminal.** If there's no policy covering the
  incident date, nothing else matters — no agent judgement required, no human approval needed to
  *decline assessment* (though declining the claim itself still needs a human).
- **`Disputed` should remove the claim from all automation.** Once a matter is contested,
  anything the agent drafts is potentially discoverable. Agent runs on disputed claims should be
  blocked, not just discouraged.

---

## 4. Rule catalogue

Each rule states its owner: **Code** (deterministic, agent cannot override), **Agent** (model
judgement, advisory only), or **Human** (requires approval).

### CV — Coverage validation (owner: Code)

| ID | Rule | Outcome |
|---|---|---|
| CV-1 | A policy must exist for the worker where `IncidentDate` falls within `StartDate`–`EndDate`. | Fail → `CoverageRejected`. Blocks all downstream rules. |
| CV-2 | The claim's type must be covered by the policy's `CoverageType`. Workers' Compensation covers `Injury`/`Medical`; Public Liability covers `Liability`/`Property Damage`. | Fail → `CoverageRejected` with the specific mismatch quoted. |
| CV-3 | `Amount` (and cumulative claims against the policy period) must not exceed `CoverageAmount`. | Exceeds → cap at remaining cover and escalate the excess. |
| CV-4 | `Policy.IsActive` must agree with `EndDate` vs today. Disagreement is a data-integrity fault. | Flag for ops — do not silently trust either field. |
| CV-5 | Multiple overlapping policies → use the one in force on `IncidentDate`; if still ambiguous, escalate. | Ambiguous → human. |

### EL — Eligibility / liability (owner: Code, with Agent input)

| ID | Rule | Outcome |
|---|---|---|
| EL-1 | Employment relationship must be current at `IncidentDate`. | Fail → deny. |
| EL-2 | Injury must arise out of, or in the course of, employment. **Agent-assisted**: the model reads the description and classifies (clearly work-related / clearly not / ambiguous). Ambiguous never auto-proceeds. | Ambiguous → human review. |
| EL-3 | Excluded mechanisms (serious and wilful misconduct, intoxication, self-inflicted) per scheme rules. **Agent-assisted**: flag keywords for a human, never decide. | Any flag → human. |
| EL-4 | Provisional liability: if the claim is low-value and not contested, start payments within the statutory window while assessment continues. *(Demo default: ≤ $10,000 and no EL-2/EL-3 flags.)* | Auto-start provisional, human-approved. |
| EL-5 | Pre-existing condition / aggravation — apportionment between the work injury and the underlying condition. | Always human; medical evidence required. |

### PY — Payout calculation (owner: Code — **never the agent**)

| ID | Rule |
|---|---|
| PY-1 | Medical costs: reimburse reasonably-necessary treatment up to the scheme fee schedule. Above-schedule amounts need prior approval. |
| PY-2 | Weekly benefits derive from PIAWE with a step-down over time. *(Demo default: 95% of PIAWE for weeks 1–13, 80% weeks 14–130, subject to a statutory weekly cap.)* |
| PY-3 | Weekly benefits reduce by actual earnings where the worker has partial capacity. |
| PY-4 | Apply the employer excess/deductible before payment. |
| PY-5 | Where `ThirdPartyInvolved`, flag recovery potential — pay the worker, pursue the third party separately. |
| PY-6 | Every computed amount is persisted with the rule version and the inputs used, so the figure can be reproduced exactly later. |

PY-6 is the one to not skip. "Why was this worker paid $4,812.50 in March 2025?" needs an answer
that doesn't depend on a model re-run.

### ES — Escalation triggers (owner: Code)

Any of these routes the claim to a human tier and **blocks agent-proposed actions**:

| ID | Trigger *(demo defaults)* |
|---|---|
| ES-1 | Reserve estimate or claim amount above a threshold (e.g. > $10,000 → Manager; > $50,000 → senior/technical). |
| ES-2 | Fatality, permanent impairment, or hospitalisation. |
| ES-3 | Legal representation appointed, or claim status `Disputed`. |
| ES-4 | Time off work exceeding a threshold (e.g. > 12 weeks) — these are where scheme costs concentrate. |
| ES-5 | Third claim by the same worker within 12 months. |
| ES-6 | Any coverage failure (CV-1/CV-2) on a claim that has *already had money paid* — that's a recovery situation, not a decline. |

### FR — Fraud and anomaly signals (owner: Agent flags, Human decides)

None of these should ever auto-decline. They raise a flag with evidence attached.

| ID | Signal |
|---|---|
| FR-1 | Long lag between `IncidentDate` and `ReportedDate` without explanation. |
| FR-2 | Claim lodged shortly after employment termination or a disciplinary event. |
| FR-3 | Description inconsistent with the injury type, the worker's role, or the mechanism claimed — **this is the agent's strongest contribution**; it reads text a rules engine can't. |
| FR-4 | Repeated similar claims by the same worker, or clustering by site/supervisor. |
| FR-5 | Incident on a Monday morning / immediately after a weekend or holiday (weak signal alone; meaningful combined with others). |
| FR-6 | No witnesses and no incident report where the workplace would normally produce both. |

A flag is an input to a human decision, and must be recorded as such — never as a reason
visible to the worker.

### SL — Timeliness / SLA (owner: Code)

| ID | Rule |
|---|---|
| SL-1 | Acknowledge lodgement within the statutory window from `ReportedDate`. |
| SL-2 | Make a liability decision within the statutory window, or commence provisional liability. |
| SL-3 | Track medical certificate expiry; prompt before benefits lapse. |
| SL-4 | Breaching, or about to breach, any statutory clock is itself an escalation trigger. |

---

## 5. How the agent uses all this

The claim-processing prompt should change from "give a recommendation" to something closer to:

> Call `CheckCoverage` and `AssessEligibility` for this claim. Quote their results exactly — do
> not recompute or second-guess them. Then read the claim description and the worker's history,
> and report: (a) anything in the description inconsistent with the structured data, (b) any
> FR-series signal you can evidence, (c) a plain-English summary a claims officer can act on.
> If the rules produced a payable amount, propose applying it. Never propose an amount yourself.

Concretely, per rule family:

- **CV / EL / PY / SL** → new **rule tools**. Agent calls, quotes, cannot override.
- **FR** → agent judgement, output as structured flags with the evidence quoted from the text.
- **ES** → evaluated in code after the rules run; if triggered, the agent's sensitive-tool calls
  are rejected and the claim is routed to a human tier instead.
- **Drafting** (worker email, escalation note) → agent, still gated through `PendingAction`.

Two guardrails worth adding to the existing approval gate:

1. **Block agent runs on `Disputed` claims outright** — not a warning, a hard stop.
2. **A `PendingAction` should carry the rule outputs that justified it**, so the approver sees
   "CV passed, EL-4 provisional applies, PY computed $2,480" rather than just the model's prose.
   The approver is accountable for the decision; they need the basis, not a summary.

---

## 6. Findings already sitting in the seed data

Worth knowing: implementing CV-1 and CV-2 would immediately flag **six of the twenty seeded
claims**. This is a ready-made test set — the rules can be validated against known-bad rows on
day one.

**CV-1, claim outside policy period:**

| Claim | Claim date | Worker | Policy period | Current status |
|---|---|---|---|---|
| CLM-240814-018 | 2024-08-14 | Ethan Brooks | POL-9006 ends 2024-01-01 | **Approved** |
| CLM-250506-019 | 2025-05-06 | Ethan Brooks | POL-9006 ends 2024-01-01 | UnderReview |
| CLM-260815-020 | 2026-08-15 | Ethan Brooks | POL-9006 ends 2024-01-01 | Pending |
| CLM-250830-009 | 2025-08-30 | Mia Chen | POL-9003 ends 2025-01-01 | UnderReview |

`CLM-240814-018` is the interesting one: **approved and outside cover** — under ES-6 that's a
recovery case, not a decline.

**CV-2, claim type not covered by the policy type:**

| Claim | Type | Policy coverage |
|---|---|---|
| CLM-240609-002 | Equipment Damage | Workers Compensation |
| CLM-230322-004 | Property Damage | Workers Compensation |
| CLM-250612-011 | Equipment Damage | Workers Compensation |
| CLM-250420-015 | Equipment Damage | Workers Compensation |
| CLM-240814-018 | Injury | Public Liability |
| CLM-260815-020 | Injury | Public Liability |

Either the seed data needs fixing, or — more usefully — leave it exactly as is and let it serve
as the regression fixture for the rules engine.

---

## 7. Worked example: CLM-260815-020

Ethan Brooks, back strain after an extended shift, $3,100, lodged 2026-08-15, status `Pending`.

1. `CheckCoverage` → **CV-1 fail** (POL-9006 expired 2024-01-01) *and* **CV-2 fail** (Injury vs
   Public Liability). Claim → `CoverageRejected`.
2. Downstream rules don't run — no eligibility assessment, no payout calculation.
3. Agent reads the history and reports: this is Ethan's **fourth** claim, and his third since the
   policy lapsed (**FR-4**, **ES-5**). It also notes `IsAvailable = false`, consistent with a
   worker already off work.
4. Agent drafts a decline-of-cover notification → queued as a `PendingAction`, **never sent
   automatically**.
5. Separately flags for ops: this worker has had claims accepted with no policy in force since
   January 2024 — the underlying problem is a lapsed-policy gap, not this one claim.

Note what the agent did and didn't do: it never decided the coverage question (code did), never
proposed a dollar figure, but it *did* produce the insight that actually matters — a systemic
lapsed-policy problem visible only by reading across claims.

---

## 8. Suggested build order

1. **Schema gaps** (§2) — `IncidentDate`, `ReportedDate`, `Jurisdiction`, split amounts. Cheap,
   and nothing else can be written without them.
2. **CV rules as the first rule tool.** Highest value per effort, fully deterministic, and it
   has a built-in test set (§6).
3. **The expanded lifecycle** (§3), including the hard block on `Disputed`.
4. **ES triggers** — pure thresholds over data that already exists.
5. **Rule outputs attached to `PendingAction`**, so approvers see the basis.
6. **PY calculation** — needs PIAWE and the amount split first; do it last because it's the one
   where being wrong costs real money.
7. **FR signals** — genuinely useful, but only once there's enough history for the patterns to
   mean anything.

Deliberately *not* on this list: letting the agent decide anything in CV/EL/PY. That should stay
off the roadmap permanently.

---

## 9. Operational notes

- **Cost per decision is now measurable.** `AgentRunLog` records tokens and cost per run, so
  "what does it cost us to triage a claim" is answerable. On a local model that's $0, but the
  fields exist for when it isn't — and cost-per-claim is the number that decides whether this
  scales.
- **The audit trail is the product.** In a regulated claims process, being able to show what was
  decided, on what basis, by which rule version, and who approved it, matters as much as the
  decision. `AgentRunLog` + `PendingAction` already cover the agent side; rule outputs and rule
  versions should join them.
- **PII.** Claim descriptions and medical information are sensitive. Prompts and reasoning text
  are currently persisted in full and shipped to Loki — fine for a local demo, but before this
  goes near production data it needs a retention policy and a decision about what gets logged.
