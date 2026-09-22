import { apiClient } from "./client";
import type {
  AgentCatalogEntryDto,
  AgentRunLogDto,
  ConversationSessionDto,
  ConversationSessionMessagesResponse,
  ProcessClaimResponse,
} from "./types";

export const agentApi = {
  query: (prompt: string) =>
    apiClient.post<AgentRunLogDto>("/api/agent/query", { prompt }).then((r) => r.data),
  processClaim: (claimId: number) =>
    apiClient.post<ProcessClaimResponse>(`/api/agent/claims/${claimId}/process`).then((r) => r.data),
};

// Phase 13 (docs/plan-agents.md §8) - the specialist agent catalog. Query is always read-only
// regardless of which agent is asked, same guarantee agentApi.query has always made.
export const agentCatalogApi = {
  list: () => apiClient.get<AgentCatalogEntryDto[]>("/api/agents").then((r) => r.data),
  query: (agentName: string, prompt: string) =>
    apiClient.post<AgentRunLogDto>(`/api/agents/${agentName}/query`, { prompt }).then((r) => r.data),
};

// Multi-turn chat (docs/plan.md §14) - distinct from agentApi.query's one-shot Q&A above.
export const agentSessionsApi = {
  start: () => apiClient.post<ConversationSessionDto>("/api/agent/sessions").then((r) => r.data),
  list: () => apiClient.get<ConversationSessionDto[]>("/api/agent/sessions").then((r) => r.data),
  getMessages: (sessionId: number) =>
    apiClient
      .get<ConversationSessionMessagesResponse>(`/api/agent/sessions/${sessionId}/messages`)
      .then((r) => r.data),
  sendMessage: (sessionId: number, message: string) =>
    apiClient
      .post<AgentRunLogDto>(`/api/agent/sessions/${sessionId}/messages`, { message })
      .then((r) => r.data),
};

export const agentRunLogsApi = {
  list: () => apiClient.get<AgentRunLogDto[]>("/api/agent/runs").then((r) => r.data),
  get: (id: number) => apiClient.get<AgentRunLogDto>(`/api/agent/runs/${id}`).then((r) => r.data),
};
