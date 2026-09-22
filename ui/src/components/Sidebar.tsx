import { NavLink } from "react-router-dom";
import { useAuth } from "../auth/RoleContext";
import { roleBadgeClasses } from "../utils/badgeColors";

const links = [
  { to: "/", label: "Dashboard" },
  { to: "/workers", label: "Workers" },
  { to: "/policies", label: "Policies" },
  { to: "/claims", label: "Claims" },
  { to: "/approvals", label: "Approvals" },
  { to: "/agent/query", label: "Chat with agent" },
  { to: "/agent/runs", label: "Agent runs" },
  { to: "/workflows", label: "Workflows" },
];

export function Sidebar() {
  const { user, roles, hasRole, signOut } = useAuth();
  // A token's role claims are ordered highest-first by JwtTokenService (SuperAdmin tokens list
  // ["SuperAdmin","Admin","CaseManager"]), so roles[0] is always the user's own actual UserRole.
  const primaryRole = roles[0] ?? null;

  const visibleLinks = hasRole("Admin") ? [...links, { to: "/users", label: "Users" }] : links;

  return (
    <aside className="flex h-screen w-60 shrink-0 flex-col border-r border-slate-200 bg-white">
      <div className="flex items-center gap-2 px-5 py-5">
        <div className="h-7 w-7 rounded-md bg-brand-600" />
        <span className="text-base font-semibold text-slate-900">AgentCore</span>
      </div>

      <nav className="flex-1 space-y-0.5 px-3">
        {visibleLinks.map((link) => (
          <NavLink
            key={link.to}
            to={link.to}
            end={link.to === "/"}
            className={({ isActive }) =>
              [
                "flex items-center gap-3 rounded-md px-3 py-2 text-sm font-medium transition-colors",
                isActive ? "bg-brand-50 text-brand-700" : "text-slate-600 hover:bg-slate-50 hover:text-slate-900",
              ].join(" ")
            }
          >
            {({ isActive }) => (
              <>
                <span className={`h-1.5 w-1.5 rounded-full ${isActive ? "bg-brand-600" : "bg-slate-300"}`} />
                {link.label}
              </>
            )}
          </NavLink>
        ))}
      </nav>

      <div className="border-t border-slate-200 px-4 py-4">
        <div className="mb-2 flex items-center gap-2">
          <span className={roleBadgeClasses(primaryRole)}>{primaryRole}</span>
          <span className="truncate text-sm text-slate-500">{user?.name}</span>
        </div>
        <button type="button" onClick={signOut} className="btn-secondary w-full">
          Sign out
        </button>
      </div>
    </aside>
  );
}
