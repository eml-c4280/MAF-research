import { useState, type FormEvent, type ReactNode } from "react";
import { useMutation } from "@tanstack/react-query";
import { authApi } from "../api/auth";
import { apiErrorMessage } from "../api/client";
import { ErrorBanner } from "../components/ErrorBanner";
import { useAuth } from "./RoleContext";

/** Real JWT login (docs/plan.md §5) - replaces the old X-Role picker entirely. Blocks rendering
 * the app until a token is present, same gate shape as before, now backed by a real credential
 * check server-side instead of a client-asserted role. */
export function RoleGate({ children }: { children: ReactNode }) {
  const { user, login } = useAuth();
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");

  const signIn = useMutation({
    mutationFn: () => authApi.login({ email, password }),
    onSuccess: (data) => login(data.accessToken, data.user),
  });

  if (user) return <>{children}</>;

  function handleSubmit(e: FormEvent) {
    e.preventDefault();
    signIn.mutate();
  }

  return (
    <div className="flex min-h-screen items-center justify-center bg-slate-50">
      <form onSubmit={handleSubmit} className="panel w-[380px] p-8">
        <div className="mb-1 flex items-center gap-2">
          <div className="h-7 w-7 rounded-md bg-brand-600" />
          <h1 className="text-lg font-semibold text-slate-900">AgentCore</h1>
        </div>
        <p className="mb-6 text-sm text-slate-500">Sign in with your AgentCore account.</p>

        <label className="field-label mb-4">
          Email
          <input
            type="email"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            required
            autoFocus
            className="field-input"
          />
        </label>

        <label className="field-label mb-6">
          Password
          <input
            type="password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            required
            className="field-input"
          />
        </label>

        {signIn.error && <ErrorBanner message={apiErrorMessage(signIn.error)} />}

        <button type="submit" className="btn-primary w-full" disabled={signIn.isPending}>
          {signIn.isPending ? "Signing in…" : "Sign in"}
        </button>
      </form>
    </div>
  );
}
