import axios from "axios";

export const TOKEN_STORAGE_KEY = "agentcore.accessToken";

const baseURL = import.meta.env.VITE_API_BASE_URL ?? "http://localhost:8080";

export const apiClient = axios.create({ baseURL });

// Real JWT auth (docs/plan.md §5) - replaces the old X-Role header trust model entirely. Read
// straight from localStorage rather than a React context, since axios interceptors run outside
// the component tree.
apiClient.interceptors.request.use((config) => {
  const token = localStorage.getItem(TOKEN_STORAGE_KEY);
  if (token) {
    config.headers["Authorization"] = `Bearer ${token}`;
  }
  return config;
});

// On 401 the stored token is missing/expired/invalid server-side - clear it and tell AuthContext
// (which owns the in-memory user state) to fall back to the login screen. AuthContext listens
// for this event rather than being imported here, since an axios interceptor can't call a React
// hook directly.
apiClient.interceptors.response.use(
  (response) => response,
  (error) => {
    if (error.response?.status === 401) {
      localStorage.removeItem(TOKEN_STORAGE_KEY);
      window.dispatchEvent(new Event("agentcore:unauthorized"));
    }
    return Promise.reject(error);
  },
);

/** Best-effort extraction of a readable message from an axios error, for ErrorBanner. */
export function apiErrorMessage(error: unknown): string {
  if (axios.isAxiosError(error)) {
    const data = error.response?.data;
    if (typeof data === "string" && data.trim().length > 0) return data;
    if (data && typeof data === "object" && "title" in data) return String((data as { title: unknown }).title);
    if (error.response?.status) return `Request failed (HTTP ${error.response.status}).`;
    return error.message;
  }
  return error instanceof Error ? error.message : "Something went wrong.";
}
