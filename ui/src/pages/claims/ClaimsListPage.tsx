import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { claimsApi } from "../../api/claims";
import { workersApi } from "../../api/workers";
import { agentApi } from "../../api/agent";
import { apiErrorMessage } from "../../api/client";
import type { ProcessClaimResponse } from "../../api/types";
import { LoadingState } from "../../components/LoadingState";
import { ErrorBanner } from "../../components/ErrorBanner";
import { outcomeBadgeClasses, statusBadgeClasses } from "../../utils/badgeColors";

export function ClaimsListPage() {
  const queryClient = useQueryClient();
  const claims = useQuery({ queryKey: ["claims"], queryFn: claimsApi.list });
  const workers = useQuery({ queryKey: ["workers"], queryFn: workersApi.list });

  const [result, setResult] = useState<ProcessClaimResponse | null>(null);
  const [resultError, setResultError] = useState<string | null>(null);
  const [runningClaimId, setRunningClaimId] = useState<number | null>(null);

  const processClaim = useMutation({
    mutationFn: (claimId: number) => agentApi.processClaim(claimId),
    onMutate: (claimId) => {
      setRunningClaimId(claimId);
      setResult(null);
      setResultError(null);
    },
    onSuccess: (data) => {
      setResult(data);
      queryClient.invalidateQueries({ queryKey: ["claims"] });
      queryClient.invalidateQueries({ queryKey: ["approvals"] });
    },
    onError: (err) => setResultError(apiErrorMessage(err)),
    onSettled: () => setRunningClaimId(null),
  });

  if (claims.isLoading || workers.isLoading) return <LoadingState />;
  const error = claims.error ?? workers.error;
  if (error) return <ErrorBanner message={apiErrorMessage(error)} />;

  const workerName = (id: number) => workers.data?.find((w) => w.id === id)?.name ?? `#${id}`;

  return (
    <div>
      <h1 className="text-2xl font-semibold text-slate-900">Claims</h1>
      <p className="mt-2 max-w-2xl text-sm text-slate-500">
        <strong className="text-slate-700">Run agent</strong> calls the real AI agent against that
        claim (same as <code className="rounded bg-slate-100 px-1 py-0.5 text-xs">POST /api/agent/claims/&#123;id&#125;/process</code>).
        Anything sensitive it decides on - emailing the worker, escalating, calculating a payout -
        is queued for approval, never executed directly. See the{" "}
        <a href="/approvals" className="font-medium text-brand-600 hover:underline">
          Approvals
        </a>{" "}
        page.
      </p>

      <div className="panel mt-6 overflow-hidden">
        <table className="data-table">
          <thead>
            <tr>
              <th>Claim #</th>
              <th>Worker</th>
              <th>Type</th>
              <th>Amount</th>
              <th>Status</th>
              <th>Date</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            {claims.data?.map((c) => (
              <tr key={c.id}>
                <td className="font-medium text-slate-900">{c.claimNumber}</td>
                <td>{workerName(c.workerId)}</td>
                <td>{c.claimType}</td>
                <td>${c.amount.toLocaleString()}</td>
                <td>
                  <span className={statusBadgeClasses(c.status)}>{c.status}</span>
                </td>
                <td>{c.claimDate}</td>
                <td>
                  <button
                    type="button"
                    className="btn-primary"
                    disabled={runningClaimId === c.id}
                    onClick={() => processClaim.mutate(c.id)}
                  >
                    {runningClaimId === c.id ? "Running…" : "Run agent"}
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {resultError && <ErrorBanner message={resultError} />}

      {result && (
        <div className="panel mt-6 p-6">
          <h2 className="text-lg font-semibold text-slate-900">Agent result — claim #{result.claimId}</h2>
          <p className="mt-3 text-sm text-slate-700">
            <span className="font-medium text-slate-900">Recommendation:</span> {result.recommendation}
          </p>
          <p className="mt-1 text-sm text-slate-700">
            <span className="font-medium text-slate-900">Claim status is now:</span> {result.claimStatus}
          </p>

          <h3 className="mt-5 text-sm font-semibold text-slate-900">
            Queued actions ({result.queuedActions.length})
          </h3>
          {result.queuedActions.length === 0 ? (
            <p className="mt-1 text-sm text-slate-500">
              Nothing queued - no sensitive action was proposed for this claim.
            </p>
          ) : (
            <ul className="mt-2 space-y-1.5">
              {result.queuedActions.map((a) => (
                <li key={a.id} className="flex items-center gap-2 text-sm text-slate-700">
                  <span className="font-medium text-slate-900">{a.actionType}</span>
                  <span className={statusBadgeClasses(a.status)}>{a.status}</span>
                  <code className="rounded bg-slate-100 px-1 py-0.5 text-xs">{a.payload}</code>
                </li>
              ))}
            </ul>
          )}

          <h3 className="mt-5 text-sm font-semibold text-slate-900">Run details</h3>
          <table className="mt-2 text-sm">
            <tbody>
              <tr>
                <td className="py-0.5 pr-4 text-slate-500">Model</td>
                <td className="py-0.5 text-slate-700">{result.run.modelId}</td>
              </tr>
              <tr>
                <td className="py-0.5 pr-4 text-slate-500">Outcome</td>
                <td className="py-0.5 text-slate-700">
                  <span className={outcomeBadgeClasses(result.run.outcome)}>{result.run.outcome}</span>
                </td>
              </tr>
              <tr>
                <td className="py-0.5 pr-4 text-slate-500">Tool calls</td>
                <td className="py-0.5 text-slate-700">{result.run.toolCallCount}</td>
              </tr>
              <tr>
                <td className="py-0.5 pr-4 text-slate-500">Tokens (in/out/total)</td>
                <td className="py-0.5 text-slate-700">
                  {result.run.inputTokenCount ?? "-"} / {result.run.outputTokenCount ?? "-"} /{" "}
                  {result.run.totalTokenCount ?? "-"}
                </td>
              </tr>
              <tr>
                <td className="py-0.5 pr-4 text-slate-500">Cost (in/out/total)</td>
                <td className="py-0.5 text-slate-700">
                  {result.run.inputCost ?? "-"} / {result.run.outputCost ?? "-"} / {result.run.totalCost ?? "-"}
                </td>
              </tr>
              {result.run.reasoningText && (
                <tr>
                  <td className="py-0.5 pr-4 align-top text-slate-500">Model's reasoning</td>
                  <td className="whitespace-pre-wrap py-0.5 text-slate-600">{result.run.reasoningText}</td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
