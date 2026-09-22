import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { approvalsApi } from "../../api/approvals";
import { apiErrorMessage } from "../../api/client";
import type { ApprovalBatchItemResult, PendingActionStatus } from "../../api/types";
import { LoadingState } from "../../components/LoadingState";
import { ErrorBanner } from "../../components/ErrorBanner";
import { statusBadgeClasses } from "../../utils/badgeColors";

const statuses: PendingActionStatus[] = [
  "AwaitingApproval",
  "Approved",
  "Rejected",
  "Executed",
  "ExecutionFailed",
];

export function ApprovalsPage() {
  const queryClient = useQueryClient();
  const [status, setStatus] = useState<PendingActionStatus>("AwaitingApproval");
  const [actionError, setActionError] = useState<string | null>(null);
  const [selected, setSelected] = useState<Set<number>>(new Set());
  const [batchResults, setBatchResults] = useState<ApprovalBatchItemResult[] | null>(null);

  const { data, isLoading, error } = useQuery({
    queryKey: ["approvals", status],
    queryFn: () => approvalsApi.listByStatus(status),
  });

  const invalidate = () => queryClient.invalidateQueries({ queryKey: ["approvals"] });

  const approve = useMutation({
    mutationFn: (id: number) => approvalsApi.approve(id),
    onSuccess: invalidate,
    onError: (err) => setActionError(apiErrorMessage(err)),
  });
  const reject = useMutation({
    mutationFn: (id: number) => approvalsApi.reject(id),
    onSuccess: invalidate,
    onError: (err) => setActionError(apiErrorMessage(err)),
  });

  // Batch: one decision applied to every selected id, with full per-id detail shown afterwards -
  // never a single collapsed "N processed" toast. See docs/plan.md §13 ("HITL gaps").
  const decideBatch = useMutation({
    mutationFn: (approveAll: boolean) =>
      approvalsApi.decideBatch([...selected].map((id) => ({ id, approve: approveAll }))),
    onSuccess: (results) => {
      setBatchResults(results);
      setSelected(new Set());
      invalidate();
    },
    onError: (err) => setActionError(apiErrorMessage(err)),
  });

  const toggleSelected = (id: number) =>
    setSelected((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });

  return (
    <div>
      <h1 className="text-2xl font-semibold text-slate-900">Approvals</h1>
      <p className="mt-2 max-w-2xl text-sm text-slate-500">
        Sensitive actions the agent proposes (emailing a worker, escalating, calculating a
        payout) land here instead of executing directly. Approving actually performs the
        effect - approving a payout writes the amount back onto the claim; approving an
        email/escalation logs a simulated send. Rejecting has no effect.
      </p>

      <label className="mt-5 block text-sm text-slate-600">
        Status
        <select
          value={status}
          onChange={(e) => {
            setStatus(e.target.value as PendingActionStatus);
            setSelected(new Set());
            setBatchResults(null);
          }}
          className="field-input mt-1 block w-56"
        >
          {statuses.map((s) => (
            <option key={s} value={s}>
              {s}
            </option>
          ))}
        </select>
      </label>

      {actionError && <ErrorBanner message={actionError} />}
      {isLoading && <LoadingState />}
      {error && <ErrorBanner message={apiErrorMessage(error)} />}

      {status === "AwaitingApproval" && selected.size > 0 && (
        <div className="mt-4 flex items-center gap-3 rounded-lg bg-slate-50 px-4 py-2.5 ring-1 ring-inset ring-slate-200">
          <span className="text-sm text-slate-600">{selected.size} selected</span>
          <button
            type="button"
            className="btn-primary"
            disabled={decideBatch.isPending}
            onClick={() => decideBatch.mutate(true)}
          >
            Approve selected
          </button>
          <button
            type="button"
            className="btn-danger"
            disabled={decideBatch.isPending}
            onClick={() => decideBatch.mutate(false)}
          >
            Reject selected
          </button>
        </div>
      )}

      {batchResults && (
        <div className="panel mt-4 p-4">
          <div className="flex items-center justify-between">
            <h3 className="text-sm font-semibold text-slate-900">
              Batch result ({batchResults.length} decision{batchResults.length === 1 ? "" : "s"})
            </h3>
            <button
              type="button"
              className="text-xs text-slate-400 hover:text-slate-600"
              onClick={() => setBatchResults(null)}
            >
              Dismiss
            </button>
          </div>
          <p className="mt-1 text-xs text-slate-500">
            Full detail per action, never a single collapsed confirmation.
          </p>
          <ul className="mt-2 space-y-1 text-sm">
            {batchResults.map((r) => (
              <li key={r.id} className="flex items-center gap-2">
                <span className="font-medium text-slate-900">#{r.id}</span>
                <span className={statusBadgeClasses(r.action?.status ?? r.outcome)}>{r.outcome}</span>
                {r.error && <span className="text-xs text-slate-500">— {r.error}</span>}
              </li>
            ))}
          </ul>
        </div>
      )}

      {data && (
        <div className="panel mt-4 overflow-hidden">
          <table className="data-table">
            <thead>
              <tr>
                {status === "AwaitingApproval" && <th></th>}
                <th>Claim</th>
                <th>Action</th>
                <th>Payload</th>
                <th>Requested</th>
                <th>Expires</th>
                <th>Decided by</th>
                {status === "ExecutionFailed" && <th>Error</th>}
                {status === "AwaitingApproval" && <th></th>}
              </tr>
            </thead>
            <tbody>
              {data.length === 0 && (
                <tr>
                  <td colSpan={8} className="text-slate-500">
                    Nothing with status "{status}".
                  </td>
                </tr>
              )}
              {data.map((a) => (
                <tr key={a.id}>
                  {status === "AwaitingApproval" && (
                    <td>
                      <input
                        type="checkbox"
                        checked={selected.has(a.id)}
                        onChange={() => toggleSelected(a.id)}
                        disabled={a.isExpired}
                      />
                    </td>
                  )}
                  <td className="font-medium text-slate-900">#{a.claimId}</td>
                  <td>{a.actionType}</td>
                  <td className="max-w-xs">
                    <code className="text-xs text-slate-500">{a.payload}</code>
                    {a.ruleOutputsJson && (
                      <details className="mt-1">
                        <summary className="cursor-pointer text-xs text-brand-700">Rule basis</summary>
                        <code className="mt-1 block whitespace-pre-wrap text-xs text-slate-500">
                          {JSON.stringify(JSON.parse(a.ruleOutputsJson), null, 2)}
                        </code>
                      </details>
                    )}
                  </td>
                  <td>{new Date(a.requestedAtUtc).toLocaleString()}</td>
                  <td>
                    {a.isExpired ? (
                      <span className={statusBadgeClasses("Rejected")}>Expired</span>
                    ) : (
                      new Date(a.expiresAt).toLocaleDateString()
                    )}
                  </td>
                  <td>{a.decidedByRole ?? "-"}</td>
                  {status === "ExecutionFailed" && (
                    <td className="max-w-xs text-xs text-slate-500">{a.executionError ?? "-"}</td>
                  )}
                  {status === "AwaitingApproval" && (
                    <td className="flex gap-2">
                      <button
                        type="button"
                        className="btn-primary"
                        disabled={approve.isPending || reject.isPending || a.isExpired}
                        title={a.isExpired ? "Expired - can no longer be approved" : undefined}
                        onClick={() => approve.mutate(a.id)}
                      >
                        Approve
                      </button>
                      <button
                        type="button"
                        className="btn-danger"
                        disabled={approve.isPending || reject.isPending}
                        onClick={() => reject.mutate(a.id)}
                      >
                        Reject
                      </button>
                    </td>
                  )}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
