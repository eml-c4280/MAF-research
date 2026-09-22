import { apiClient } from "./client";
import type { LoginRequest, LoginResponse, UserDto } from "./types";

export const authApi = {
  login: (request: LoginRequest) => apiClient.post<LoginResponse>("/api/auth/login", request).then((r) => r.data),
  me: () => apiClient.get<UserDto>("/api/auth/me").then((r) => r.data),
};
