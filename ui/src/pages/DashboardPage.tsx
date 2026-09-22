import { useQuery } from "@tanstack/react-query";
import { workersApi } from "../api/workers";
import { claimsApi } from "../api/claims";
import { approvalsApi } from "../api/approvals";
import { apiErrorMessage } from "../api/client";
import { LoadingState } from "../components/LoadingState";
import { ErrorBanner } from "../components/ErrorBanner";
import { statusBadgeClasses } from "../utils/badgeColors";

export function DashboardPage() {
  const workers = useQuery({ queryKey: ["workers"], queryFn: workersApi.list });
  const claims = useQuery({ queryKey: ["claims"], queryFn: claimsApi.list });
  const pending = useQuery({
    queryKey: ["approvals", "AwaitingApproval"],
    queryFn: () => approvalsApi.listByStatus("AwaitingApproval"),
  });

  const loading = workers.isLoading || claims.isLoading || pending.isLoading;
  const error = workers.error ?? claims.error ?? pending.error;

  if (loading) return <LoadingState />;
  if (error) return <ErrorBanner message={apiErrorMessage(error)} />;

  const claimsByStatus = (claims.data ?? []).reduce<Record<string, number>>((acc, c) => {
    acc[c.status] = (acc[c.status] ?? 0) + 1;
    return acc;
  }, {});

  return (
    <div>
      <h1 className="text-2xl font-semibold text-slate-900">Dashboard</h1>

      <div className="mt-6 grid grid-cols-3 gap-4">
        <div className="panel p-5">
          <div className="text-3xl font-semibold text-slate-900">{workers.data?.length ?? 0}</div>
          <div className="mt-1 text-sm text-slate-500">Workers</div>
        </div>
        <div className="panel p-5">
          <div className="text-3xl font-semibold text-slate-900">{claims.data?.length ?? 0}</div>
          <div className="mt-1 text-sm text-slate-500">Claims total</div>
        </div>
        <div className="panel border-brand-200 p-5">
          <div className="text-3xl font-semibold text-brand-700">{pending.data?.length ?? 0}</div>
          <div className="mt-1 text-sm text-slate-500">Awaiting approval</div>
        </div>
      </div>

      <h2 className="mt-8 mb-3 text-base font-semibold text-slate-900">Claims by status</h2>
      <div className="panel divide-y divide-slate-100">
        {Object.entries(claimsByStatus).map(([status, count]) => (
          <div key={status} className="flex items-center justify-between px-4 py-2.5">
            <span className={statusBadgeClasses(status)}>{status}</span>
            <span className="text-sm font-medium text-slate-700">{count}</span>
          </div>
        ))}
      </div>
    </div>
  );
}
