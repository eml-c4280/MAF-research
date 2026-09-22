import { useQuery } from "@tanstack/react-query";
import { agentRunLogsApi } from "../../api/agent";
import { apiErrorMessage } from "../../api/client";
import { LoadingState } from "../../components/LoadingState";
import { ErrorBanner } from "../../components/ErrorBanner";
import { outcomeBadgeClasses } from "../../utils/badgeColors";

export function AgentRunLogsListPage() {
  const { data, isLoading, error } = useQuery({ queryKey: ["agentRuns"], queryFn: agentRunLogsApi.list });

  if (isLoading) return <LoadingState />;
  if (error) return <ErrorBanner message={apiErrorMessage(error)} />;

  const sorted = [...(data ?? [])].sort(
    (a, b) => new Date(b.createdAtUtc).getTime() - new Date(a.createdAtUtc).getTime(),
  );

  return (
    <div>
      <h1 className="text-2xl font-semibold text-slate-900">Agent run audit log</h1>
      <p className="mt-2 text-sm text-slate-500">Read-only history of every agent invocation, most recent first.</p>

      <div className="panel mt-6 overflow-hidden">
        <table className="data-table">
          <thead>
            <tr>
              <th>When</th>
              <th>Claim</th>
              <th>Trigger</th>
              <th>Model</th>
              <th>Outcome</th>
              <th>Tool calls</th>
              <th>Tokens (total)</th>
              <th>Cost (total)</th>
              <th>Final answer</th>
            </tr>
          </thead>
          <tbody>
            {sorted.map((r) => (
              <tr key={r.id}>
                <td className="whitespace-nowrap">{new Date(r.createdAtUtc).toLocaleString()}</td>
                <td>{r.claimId ?? "-"}</td>
                <td className="max-w-[160px] truncate" title={r.trigger}>
                  {r.trigger}
                </td>
                <td>{r.modelId}</td>
                <td>
                  <span className={outcomeBadgeClasses(r.outcome)}>{r.outcome}</span>
                </td>
                <td>{r.toolCallCount}</td>
                <td>{r.totalTokenCount ?? "-"}</td>
                <td>{r.totalCost ?? "-"}</td>
                <td className="max-w-[260px] truncate" title={r.finalAnswer}>
                  {r.finalAnswer}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}
