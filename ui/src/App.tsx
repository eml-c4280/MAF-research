import { Route, Routes } from "react-router-dom";
import { RoleGate } from "./auth/RoleGate";
import { Sidebar } from "./components/Sidebar";
import { DashboardPage } from "./pages/DashboardPage";
import { WorkersListPage } from "./pages/workers/WorkersListPage";
import { PoliciesListPage } from "./pages/policies/PoliciesListPage";
import { ClaimsListPage } from "./pages/claims/ClaimsListPage";
import { ApprovalsPage } from "./pages/approvals/ApprovalsPage";
import { AgentQueryPage } from "./pages/agent/AgentQueryPage";
import { AgentRunLogsListPage } from "./pages/agent/AgentRunLogsListPage";
import { WorkflowsPage } from "./pages/workflows/WorkflowsPage";
import { UsersPage } from "./pages/users/UsersPage";

export function App() {
  return (
    <RoleGate>
      <div className="flex min-h-screen bg-slate-50">
        <Sidebar />
        <main className="flex-1 overflow-y-auto px-8 py-8">
          <div className="mx-auto max-w-5xl">
            <Routes>
              <Route path="/" element={<DashboardPage />} />
              <Route path="/workers" element={<WorkersListPage />} />
              <Route path="/policies" element={<PoliciesListPage />} />
              <Route path="/claims" element={<ClaimsListPage />} />
              <Route path="/approvals" element={<ApprovalsPage />} />
              <Route path="/agent/query" element={<AgentQueryPage />} />
              <Route path="/agent/runs" element={<AgentRunLogsListPage />} />
              <Route path="/workflows" element={<WorkflowsPage />} />
              <Route path="/users" element={<UsersPage />} />
            </Routes>
          </div>
        </main>
      </div>
    </RoleGate>
  );
}
