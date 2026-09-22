import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { workersApi } from "../../api/workers";
import { usersApi } from "../../api/users";
import { apiErrorMessage } from "../../api/client";
import { useAuth } from "../../auth/RoleContext";
import { LoadingState } from "../../components/LoadingState";
import { ErrorBanner } from "../../components/ErrorBanner";

export function WorkersListPage() {
  const { hasRole } = useAuth();
  const isAdmin = hasRole("Admin");
  const queryClient = useQueryClient();

  const { data, isLoading, error } = useQuery({ queryKey: ["workers"], queryFn: workersApi.list });
  // Only an Admin+ can list users (GET /api/users is Admin+ only server-side) - and only an
  // Admin+ ever sees the assign-case-manager dropdown, so there's no point fetching this
  // otherwise; a CaseManager only ever sees their own assigned workers anyway (server-scoped).
  const { data: users } = useQuery({ queryKey: ["users"], queryFn: usersApi.list, enabled: isAdmin });

  const assign = useMutation({
    mutationFn: ({ workerId, caseManagerUserId }: { workerId: number; caseManagerUserId: number | null }) =>
      workersApi.assignCaseManager(workerId, { caseManagerUserId }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["workers"] }),
  });

  const caseManagers = (users ?? []).filter((u) => u.role === "CaseManager" && u.isActive);

  if (isLoading) return <LoadingState />;
  if (error) return <ErrorBanner message={apiErrorMessage(error)} />;

  return (
    <div>
      <h1 className="text-2xl font-semibold text-slate-900">Workers</h1>
      {assign.error && <ErrorBanner message={apiErrorMessage(assign.error)} />}
      <div className="panel mt-6 overflow-hidden">
        <table className="data-table">
          <thead>
            <tr>
              <th>Code</th>
              <th>Name</th>
              <th>Role</th>
              <th>Location</th>
              <th>Hourly rate</th>
              <th>Years exp.</th>
              <th>Available</th>
              <th>Case manager</th>
            </tr>
          </thead>
          <tbody>
            {data?.map((w) => (
              <tr key={w.id}>
                <td className="font-medium text-slate-900">{w.code}</td>
                <td>{w.name}</td>
                <td>{w.role}</td>
                <td>{w.location}</td>
                <td>${w.hourlyRate.toFixed(2)}</td>
                <td>{w.yearsOfExperience}</td>
                <td>{w.isAvailable ? "Yes" : "No"}</td>
                <td>
                  {isAdmin ? (
                    <select
                      value={w.assignedCaseManagerUserId ?? ""}
                      disabled={assign.isPending}
                      onChange={(e) =>
                        assign.mutate({
                          workerId: w.id,
                          caseManagerUserId: e.target.value === "" ? null : Number(e.target.value),
                        })
                      }
                      className="field-input"
                    >
                      <option value="">Unassigned</option>
                      {caseManagers.map((cm) => (
                        <option key={cm.id} value={cm.id}>
                          {cm.name}
                        </option>
                      ))}
                    </select>
                  ) : (
                    <span className="text-sm text-slate-500">
                      {w.assignedCaseManagerUserId ? "Assigned to you" : "Unassigned"}
                    </span>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}
