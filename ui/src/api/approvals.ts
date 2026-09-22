import { apiClient } from "./client";
import type { ApprovalBatchDecision, ApprovalBatchItemResult, PendingActionDto, PendingActionStatus } from "./types";

export const approvalsApi = {
  listByStatus: (status: PendingActionStatus = "AwaitingApproval") =>
    apiClient.get<PendingActionDto[]>("/api/approvals", { params: { status } }).then((r) => r.data),
  approve: (id: number) => apiClient.post<PendingActionDto>(`/api/approvals/${id}/approve`).then((r) => r.data),
  reject: (id: number) => apiClient.post<PendingActionDto>(`/api/approvals/${id}/reject`).then((r) => r.data),
  decideBatch: (decisions: ApprovalBatchDecision[]) =>
    apiClient
      .post<ApprovalBatchItemResult[]>("/api/approvals/batch", { decisions })
      .then((r) => r.data),
};
