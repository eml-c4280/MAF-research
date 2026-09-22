import { useState, type FormEvent } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { usersApi } from "../../api/users";
import { apiErrorMessage } from "../../api/client";
import { useAuth } from "../../auth/RoleContext";
import type { UserDto, UserRole } from "../../api/types";
import { LoadingState } from "../../components/LoadingState";
import { ErrorBanner } from "../../components/ErrorBanner";
import { roleBadgeClasses } from "../../utils/badgeColors";

/** Mirrors UserManagementService.CanAssign (docs/plan.md §5): SuperAdmin -> any role, Admin ->
 * CaseManager only. This is a UI convenience (fewer useless options in the dropdown) - the real
 * enforcement is server-side in UserManagementService, same as everywhere else in this app. */
function assignableRoles(hasRole: (role: UserRole) => boolean): UserRole[] {
  if (hasRole("SuperAdmin")) return ["SuperAdmin", "Admin", "CaseManager"];
  if (hasRole("Admin")) return ["CaseManager"];
  return [];
}

export function UsersPage() {
  const { hasRole } = useAuth();
  const queryClient = useQueryClient();
  const [showCreateForm, setShowCreateForm] = useState(false);
  const [actionError, setActionError] = useState<string | null>(null);

  const { data, isLoading, error } = useQuery({ queryKey: ["users"], queryFn: usersApi.list });

  const invalidate = () => queryClient.invalidateQueries({ queryKey: ["users"] });

  const deactivate = useMutation({
    mutationFn: (id: number) => usersApi.deactivate(id),
    onSuccess: invalidate,
    onError: (err) => setActionError(apiErrorMessage(err)),
  });

  const resetPassword = useMutation({
    mutationFn: ({ id, newPassword }: { id: number; newPassword: string }) =>
      usersApi.resetPassword(id, { newPassword }),
    onSuccess: () => setActionError(null),
    onError: (err) => setActionError(apiErrorMessage(err)),
  });

  if (!hasRole("Admin")) {
    return <ErrorBanner message="You do not have permission to manage users." />;
  }

  function handleResetPassword(user: UserDto) {
    const newPassword = window.prompt(`New password for ${user.email}:`);
    if (newPassword) resetPassword.mutate({ id: user.id, newPassword });
  }

  return (
    <div>
      <div className="flex items-start justify-between">
        <div>
          <h1 className="text-2xl font-semibold text-slate-900">Users</h1>
          <p className="mt-2 max-w-2xl text-sm text-slate-500">
            AgentCore staff accounts. A SuperAdmin can create any role; an Admin can only create
            CaseManager accounts.
          </p>
        </div>
        <button type="button" className="btn-primary shrink-0" onClick={() => setShowCreateForm((v) => !v)}>
          {showCreateForm ? "Cancel" : "+ New user"}
        </button>
      </div>

      {showCreateForm && (
        <CreateUserForm
          onCreated={() => {
            setShowCreateForm(false);
            invalidate();
          }}
        />
      )}

      {actionError && <ErrorBanner message={actionError} />}
      {isLoading && <LoadingState />}
      {error && <ErrorBanner message={apiErrorMessage(error)} />}

      {data && (
        <div className="panel mt-4 overflow-hidden">
          <table className="data-table">
            <thead>
              <tr>
                <th>Email</th>
                <th>Name</th>
                <th>Role</th>
                <th>Status</th>
                <th>Last login</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              {data.map((u) => (
                <tr key={u.id}>
                  <td className="font-medium text-slate-900">{u.email}</td>
                  <td>{u.name}</td>
                  <td>
                    <span className={roleBadgeClasses(u.role)}>{u.role}</span>
                  </td>
                  <td>{u.isActive ? "Active" : "Deactivated"}</td>
                  <td>{u.lastLoginAtUtc ? new Date(u.lastLoginAtUtc).toLocaleString() : "Never"}</td>
                  <td className="flex gap-2">
                    <button type="button" className="btn-secondary" onClick={() => handleResetPassword(u)}>
                      Reset password
                    </button>
                    {u.isActive && (
                      <button
                        type="button"
                        className="btn-danger"
                        disabled={deactivate.isPending}
                        onClick={() => deactivate.mutate(u.id)}
                      >
                        Deactivate
                      </button>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}

function CreateUserForm({ onCreated }: { onCreated: () => void }) {
  const { hasRole } = useAuth();
  const roles = assignableRoles(hasRole);
  const [name, setName] = useState("");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [role, setRole] = useState<UserRole>(roles[roles.length - 1] ?? "CaseManager");

  const create = useMutation({
    mutationFn: () => usersApi.create({ name, email, password, role }),
    onSuccess: onCreated,
  });

  function handleSubmit(e: FormEvent) {
    e.preventDefault();
    create.mutate();
  }

  return (
    <form onSubmit={handleSubmit} className="panel mt-4 space-y-4 p-6">
      <h3 className="text-base font-semibold text-slate-900">New user</h3>

      <label className="block text-sm text-slate-600">
        Name
        <input value={name} onChange={(e) => setName(e.target.value)} required className="field-input mt-1 block w-full" />
      </label>

      <label className="block text-sm text-slate-600">
        Email
        <input
          type="email"
          value={email}
          onChange={(e) => setEmail(e.target.value)}
          required
          className="field-input mt-1 block w-full"
        />
      </label>

      <label className="block text-sm text-slate-600">
        Initial password
        <input
          type="password"
          value={password}
          onChange={(e) => setPassword(e.target.value)}
          required
          className="field-input mt-1 block w-full"
        />
      </label>

      <label className="block text-sm text-slate-600">
        Role
        <select value={role} onChange={(e) => setRole(e.target.value as UserRole)} className="field-input mt-1 block w-56">
          {roles.map((r) => (
            <option key={r} value={r}>
              {r}
            </option>
          ))}
        </select>
      </label>

      {create.error && <ErrorBanner message={apiErrorMessage(create.error)} />}

      <button type="submit" className="btn-primary" disabled={create.isPending}>
        {create.isPending ? "Creating…" : "Create user"}
      </button>
    </form>
  );
}
