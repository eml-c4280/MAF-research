import { apiClient } from "./client";
import type { AssignCaseManagerRequest, WorkerDto } from "./types";

export const workersApi = {
  list: () => apiClient.get<WorkerDto[]>("/api/workers").then((r) => r.data),
  get: (id: number) => apiClient.get<WorkerDto>(`/api/workers/${id}`).then((r) => r.data),
  assignCaseManager: (id: number, request: AssignCaseManagerRequest) =>
    apiClient.put(`/api/workers/${id}/assign-case-manager`, request).then((r) => r.data),
};
