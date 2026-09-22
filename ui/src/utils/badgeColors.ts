/** Soft, "pill" status badges - same visual language across Dashboard/Claims/Approvals. */
export function statusBadgeClasses(status: string): string {
  const base = "inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-medium";
  switch (status) {
    case "Approved":
    case "Executed":
      return `${base} bg-emerald-50 text-emerald-700 ring-1 ring-inset ring-emerald-600/20`;
    case "Rejected":
    // Rule-decided and terminal (docs/business-logic.md §3) - grouped visually with Rejected
    // since both mean "this claim doesn't proceed", even though the reason differs.
    case "CoverageRejected":
      return `${base} bg-red-50 text-red-700 ring-1 ring-inset ring-red-600/20`;
    case "Pending":
    case "UnderReview":
    case "AwaitingApproval":
      return `${base} bg-amber-50 text-amber-700 ring-1 ring-inset ring-amber-600/20`;
    // Distinct from Rejected (red): the decision to approve stood, only the side effect itself
    // threw afterwards - see PendingAction.ExecutionError, docs/plan.md §13.
    case "ExecutionFailed":
    // Distinct color: removed from all automation, needs a human to look, not simply declined.
    case "Disputed":
      return `${base} bg-orange-50 text-orange-700 ring-1 ring-inset ring-orange-600/20`;
    default:
      return `${base} bg-slate-100 text-slate-600 ring-1 ring-inset ring-slate-500/10`;
  }
}

/** AgentRunLog.Outcome badges - a distinct vocabulary from claim/action status above (Success /
 * GuardTripped / Timeout / Failed), so it gets its own color mapping rather than reusing
 * statusBadgeClasses' cases by coincidence. */
export function outcomeBadgeClasses(outcome: string): string {
  const base = "inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-medium";
  switch (outcome) {
    case "Success":
      return `${base} bg-emerald-50 text-emerald-700 ring-1 ring-inset ring-emerald-600/20`;
    case "GuardTripped":
      return `${base} bg-amber-50 text-amber-700 ring-1 ring-inset ring-amber-600/20`;
    case "Timeout":
      return `${base} bg-orange-50 text-orange-700 ring-1 ring-inset ring-orange-600/20`;
    case "Failed":
      return `${base} bg-red-50 text-red-700 ring-1 ring-inset ring-red-600/20`;
    default:
      return `${base} bg-slate-100 text-slate-600 ring-1 ring-inset ring-slate-500/10`;
  }
}

/** Solid role badges - identity, not state, so they get a filled pill instead of a soft one.
 * Three tiers now (SuperAdmin ⊃ Admin ⊃ CaseManager - docs/plan.md §5), each its own color so a
 * SuperAdmin account is visually distinct from a plain Admin at a glance. */
export function roleBadgeClasses(role: string | null): string {
  const base = "inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-semibold text-white";
  switch (role) {
    case "SuperAdmin":
      return `${base} bg-slate-900`;
    case "Admin":
      return `${base} bg-brand-600`;
    default:
      return `${base} bg-violet-600`;
  }
}
