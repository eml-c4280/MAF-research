import { apiClient } from "./client";
import type {
  CreateWorkflowRequest,
  WorkflowDefinitionDto,
  WorkflowRunDto,
  WorkflowRunResponse,
} from "./types";

export const workflowsApi = {
  list: () => apiClient.get<WorkflowDefinitionDto[]>("/api/workflows").then((r) => r.data),
  create: (request: CreateWorkflowRequest) =>
    apiClient.post<WorkflowDefinitionDto>("/api/workflows", request).then((r) => r.data),
  run: (id: number, inputs: Record<string, string | number>) =>
    apiClient.post<WorkflowRunResponse>(`/api/workflows/${id}/run`, { inputs }).then((r) => r.data),
  chat: (text: string) =>
    apiClient.post<WorkflowRunResponse>("/api/workflows/chat", { text }).then((r) => r.data),
  listRuns: () => apiClient.get<WorkflowRunDto[]>("/api/workflows/runs").then((r) => r.data),
};
