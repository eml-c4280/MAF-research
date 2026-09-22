import { apiClient } from "./client";
import type { ClaimDto } from "./types";

export const claimsApi = {
  list: () => apiClient.get<ClaimDto[]>("/api/claims").then((r) => r.data),
  get: (id: number) => apiClient.get<ClaimDto>(`/api/claims/${id}`).then((r) => r.data),
};
