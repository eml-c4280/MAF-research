import { useQuery } from "@tanstack/react-query";
import { policiesApi } from "../../api/policies";
import { workersApi } from "../../api/workers";
import { apiErrorMessage } from "../../api/client";
import { LoadingState } from "../../components/LoadingState";
import { ErrorBanner } from "../../components/ErrorBanner";

export function PoliciesListPage() {
  const policies = useQuery({ queryKey: ["policies"], queryFn: policiesApi.list });
  const workers = useQuery({ queryKey: ["workers"], queryFn: workersApi.list });

  if (policies.isLoading || workers.isLoading) return <LoadingState />;
  const error = policies.error ?? workers.error;
  if (error) return <ErrorBanner message={apiErrorMessage(error)} />;

  const workerName = (id: number) => workers.data?.find((w) => w.id === id)?.name ?? `#${id}`;

  return (
    <div>
      <h1 className="text-2xl font-semibold text-slate-900">Policies</h1>
      <div className="panel mt-6 overflow-hidden">
        <table className="data-table">
          <thead>
            <tr>
              <th>Policy #</th>
              <th>Worker</th>
              <th>Provider</th>
              <th>Coverage type</th>
              <th>Coverage amount</th>
              <th>Start</th>
              <th>End</th>
              <th>Active</th>
            </tr>
          </thead>
          <tbody>
            {policies.data?.map((p) => (
              <tr key={p.id}>
                <td className="font-medium text-slate-900">{p.policyNumber}</td>
                <td>{workerName(p.workerId)}</td>
                <td>{p.provider}</td>
                <td>{p.coverageType}</td>
                <td>${p.coverageAmount.toLocaleString()}</td>
                <td>{p.startDate}</td>
                <td>{p.endDate}</td>
                <td>{p.isActive ? "Yes" : "No"}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}
