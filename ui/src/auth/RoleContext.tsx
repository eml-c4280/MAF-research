import { createContext, useContext, useEffect, useMemo, useState, type ReactNode } from "react";
import { TOKEN_STORAGE_KEY } from "../api/client";
import type { UserDto, UserRole } from "../api/types";

const USER_STORAGE_KEY = "agentcore.user";

// The exact claim type System.Security.Claims.ClaimTypes.Role serializes to - confirmed against a
// real issued token (docs/plan.md §5). A CaseManager's token carries just ["CaseManager"], Admin
// carries ["Admin","CaseManager"], SuperAdmin carries all three, so decoding this array (rather
// than re-deriving the hierarchy from UserDto.role client-side) is both simpler and exactly what
// the API itself already computed.
const ROLE_CLAIM = "http://schemas.microsoft.com/ws/2008/06/identity/claims/role";

/** Decodes the JWT payload client-side for display/gating only - no signature check happens or
 * is needed here, since the API is the one that validates the token on every real request; a
 * forged decode here would just mean the UI shows the wrong buttons, not a security bypass. */
function decodeRoles(token: string): UserRole[] {
  try {
    const payload = token.split(".")[1];
    const json = JSON.parse(atob(payload.replace(/-/g, "+").replace(/_/g, "/")));
    const claim = json[ROLE_CLAIM];
    if (Array.isArray(claim)) return claim as UserRole[];
    return claim ? [claim as UserRole] : [];
  } catch {
    return [];
  }
}

interface AuthState {
  user: UserDto | null;
  roles: UserRole[];
  login: (token: string, user: UserDto) => void;
  signOut: () => void;
  hasRole: (role: UserRole) => boolean;
}

const AuthContext = createContext<AuthState | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<UserDto | null>(() => {
    const raw = localStorage.getItem(USER_STORAGE_KEY);
    return raw ? (JSON.parse(raw) as UserDto) : null;
  });
  const [roles, setRoles] = useState<UserRole[]>(() => {
    const token = localStorage.getItem(TOKEN_STORAGE_KEY);
    return token ? decodeRoles(token) : [];
  });

  // The axios interceptor (api/client.ts) clears the token and fires this event on a 401 - it
  // can't call this hook directly, so it goes through a plain DOM event instead.
  useEffect(() => {
    const onUnauthorized = () => {
      localStorage.removeItem(USER_STORAGE_KEY);
      setUser(null);
      setRoles([]);
    };
    window.addEventListener("agentcore:unauthorized", onUnauthorized);
    return () => window.removeEventListener("agentcore:unauthorized", onUnauthorized);
  }, []);

  const value = useMemo<AuthState>(
    () => ({
      user,
      roles,
      login: (token, newUser) => {
        localStorage.setItem(TOKEN_STORAGE_KEY, token);
        localStorage.setItem(USER_STORAGE_KEY, JSON.stringify(newUser));
        setUser(newUser);
        setRoles(decodeRoles(token));
      },
      signOut: () => {
        localStorage.removeItem(TOKEN_STORAGE_KEY);
        localStorage.removeItem(USER_STORAGE_KEY);
        setUser(null);
        setRoles([]);
      },
      hasRole: (role) => roles.includes(role),
    }),
    [user, roles],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthState {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error("useAuth must be used within an AuthProvider");
  return ctx;
}
