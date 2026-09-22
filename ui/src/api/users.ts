import { apiClient } from "./client";
import type { CreateUserRequest, ResetPasswordRequest, UpdateUserRequest, UserDto } from "./types";

export const usersApi = {
  list: () => apiClient.get<UserDto[]>("/api/users").then((r) => r.data),
  create: (request: CreateUserRequest) => apiClient.post<UserDto>("/api/users", request).then((r) => r.data),
  update: (id: number, request: UpdateUserRequest) =>
    apiClient.put<UserDto>(`/api/users/${id}`, request).then((r) => r.data),
  deactivate: (id: number) => apiClient.post(`/api/users/${id}/deactivate`).then((r) => r.data),
  resetPassword: (id: number, request: ResetPasswordRequest) =>
    apiClient.post(`/api/users/${id}/reset-password`, request).then((r) => r.data),
};
