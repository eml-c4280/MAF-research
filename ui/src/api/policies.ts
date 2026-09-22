import { apiClient } from "./client";
import type { InsurancePolicyDto } from "./types";

export const policiesApi = {
  list: () => apiClient.get<InsurancePolicyDto[]>("/api/policies").then((r) => r.data),
  getByWorkerId: (workerId: number) =>
    apiClient.get<InsurancePolicyDto>(`/api/policies/workers/${workerId}`).then((r) => r.data),
};
