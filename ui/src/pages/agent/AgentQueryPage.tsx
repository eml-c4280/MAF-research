import { useEffect, useState, type FormEvent } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { agentCatalogApi, agentSessionsApi } from "../../api/agent";
import { apiErrorMessage } from "../../api/client";
import type { AgentRunLogDto } from "../../api/types";
import { ErrorBanner } from "../../components/ErrorBanner";
import { LoadingState } from "../../components/LoadingState";
import { outcomeBadgeClasses } from "../../utils/badgeColors";

/**
 * Two ways to talk to the agent: a multi-turn chat thread against the Claims Agent (docs/plan.md
 * §14, history carried server-side), or a one-shot question to any specialist in the Phase 13
 * catalog (docs/plan-agents.md §2/§8, no history between questions).
 */
export function AgentQueryPage() {
  const [mode, setMode] = useState<"chat" | "specialist">("chat");

  return (
    <div>
      <div className="mb-4 flex gap-2">
        <button
          type="button"
          className={mode === "chat" ? "btn-primary" : "btn-secondary"}
          onClick={() => setMode("chat")}
        >
          Chat with Claims Agent
        </button>
        <button
          type="button"
          className={mode === "specialist" ? "btn-primary" : "btn-secondary"}
          onClick={() => setMode("specialist")}
        >
          Ask a specialist
        </button>
      </div>
      {mode === "chat" ? <ChatWithClaimsAgent /> : <AskSpecialistPanel />}
    </div>
  );
}

/** The original multi-turn chat thread (docs/plan.md §14) - always against the Claims Agent
 * (ClaimAgentService.ContinueSessionAsync is hardcoded to it), history carried server-side. */
function ChatWithClaimsAgent() {
  const queryClient = useQueryClient();
  const [sessionId, setSessionId] = useState<number | null>(null);
  const [draft, setDraft] = useState("");

  const sessions = useQuery({ queryKey: ["agentSessions"], queryFn: agentSessionsApi.list });

  // Resume the most recently active chat on first load, if one exists.
  useEffect(() => {
    if (sessionId === null && sessions.data && sessions.data.length > 0) {
      setSessionId(sessions.data[0].id);
    }
  }, [sessionId, sessions.data]);

  const thread = useQuery({
    queryKey: ["agentSessionMessages", sessionId],
    queryFn: () => agentSessionsApi.getMessages(sessionId!),
    enabled: sessionId !== null,
  });

  const startChat = useMutation({
    mutationFn: () => agentSessionsApi.start(),
    onSuccess: (session) => {
      setSessionId(session.id);
      queryClient.invalidateQueries({ queryKey: ["agentSessions"] });
    },
  });

  const sendMessage = useMutation({
    mutationFn: (message: string) => agentSessionsApi.sendMessage(sessionId!, message),
    onSuccess: () => {
      setDraft("");
      queryClient.invalidateQueries({ queryKey: ["agentSessionMessages", sessionId] });
      queryClient.invalidateQueries({ queryKey: ["agentSessions"] });
    },
  });

  function handleSubmit(e: FormEvent) {
    e.preventDefault();
    if (draft.trim() && sessionId !== null) sendMessage.mutate(draft.trim());
  }

  return (
    <div className="flex h-[calc(100vh-8rem)] gap-6">
      <aside className="flex w-64 shrink-0 flex-col">
        <button
          type="button"
          className="btn-primary w-full"
          disabled={startChat.isPending}
          onClick={() => startChat.mutate()}
        >
          {startChat.isPending ? "Starting…" : "+ New chat"}
        </button>

        <div className="panel mt-4 flex-1 overflow-y-auto">
          {sessions.isLoading && <LoadingState />}
          {sessions.data?.length === 0 && (
            <p className="p-4 text-sm text-slate-500">No chats yet - start one above.</p>
          )}
          <ul>
            {sessions.data?.map((s) => (
              <li key={s.id}>
                <button
                  type="button"
                  onClick={() => setSessionId(s.id)}
                  className={`block w-full truncate px-3 py-2.5 text-left text-sm ${
                    s.id === sessionId ? "bg-brand-50 text-brand-700 font-medium" : "text-slate-600 hover:bg-slate-50"
                  }`}
                  title={s.title ?? "(empty chat)"}
                >
                  {s.title ?? "(empty chat)"}
                </button>
              </li>
            ))}
          </ul>
        </div>
      </aside>

      <div className="flex flex-1 flex-col">
        <h1 className="text-2xl font-semibold text-slate-900">Chat with agent</h1>
        <p className="mt-1 text-sm text-slate-500">
          Read-only tools only - this never queues an approval. History for this chat carries
          forward until you start a new one.
        </p>

        {sessionId === null ? (
          <div className="panel mt-4 flex flex-1 items-center justify-center text-sm text-slate-500">
            Start a new chat to begin.
          </div>
        ) : (
          <>
            <div className="panel mt-4 flex-1 space-y-4 overflow-y-auto p-6">
              {thread.isLoading && <LoadingState />}
              {thread.error && <ErrorBanner message={apiErrorMessage(thread.error)} />}
              {thread.data?.turns.length === 0 && (
                <p className="text-sm text-slate-500">Send a message below to begin.</p>
              )}
              {thread.data?.turns.map((turn) => (
                <div key={turn.id} className="space-y-2">
                  <div className="ml-auto max-w-lg rounded-lg bg-brand-600 px-4 py-2 text-sm text-white">
                    {turn.prompt}
                  </div>
                  <div className="max-w-lg space-y-1.5 rounded-lg bg-slate-100 px-4 py-2 text-sm text-slate-800">
                    <p>{turn.finalAnswer}</p>
                    {turn.reasoningText && (
                      <p className="whitespace-pre-wrap text-xs text-slate-500">
                        <span className="font-medium">Reasoning:</span> {turn.reasoningText}
                      </p>
                    )}
                    <div className="flex items-center gap-2 pt-1">
                      <span className={outcomeBadgeClasses(turn.outcome)}>{turn.outcome}</span>
                      <span className="text-xs text-slate-400">{turn.toolCallCount} tool call(s)</span>
                    </div>
                  </div>
                </div>
              ))}
            </div>

            {sendMessage.error && <ErrorBanner message={apiErrorMessage(sendMessage.error)} />}

            <form onSubmit={handleSubmit} className="mt-3 flex gap-3">
              <input
                value={draft}
                onChange={(e) => setDraft(e.target.value)}
                placeholder="Type a message…"
                className="field-input flex-1"
                disabled={sendMessage.isPending}
              />
              <button type="submit" className="btn-primary" disabled={sendMessage.isPending || !draft.trim()}>
                {sendMessage.isPending ? "Sending…" : "Send"}
              </button>
            </form>
          </>
        )}
      </div>
    </div>
  );
}

/**
 * One-shot Q&A against any named specialist (docs/plan-agents.md §2/§8: Claims, Worker
 * Management, Risk & Escalation, Notification) - GET /api/agents for the catalog,
 * POST /api/agents/{agentName}/query to ask. Always read-only regardless of which agent is
 * picked (queries never queue an approval), no session/history - each question stands alone.
 */
function AskSpecialistPanel() {
  const [agentName, setAgentName] = useState<string | null>(null);
  const [prompt, setPrompt] = useState("");
  const [result, setResult] = useState<AgentRunLogDto | null>(null);

  const catalog = useQuery({ queryKey: ["agentCatalog"], queryFn: agentCatalogApi.list });

  useEffect(() => {
    if (agentName === null && catalog.data && catalog.data.length > 0) {
      setAgentName(catalog.data[0].name);
    }
  }, [agentName, catalog.data]);

  const ask = useMutation({
    mutationFn: () => agentCatalogApi.query(agentName!, prompt.trim()),
    onSuccess: (data) => {
      setResult(data);
      setPrompt("");
    },
  });

  function handleSubmit(e: FormEvent) {
    e.preventDefault();
    if (agentName && prompt.trim()) ask.mutate();
  }

  const selected = catalog.data?.find((a) => a.name === agentName) ?? null;

  return (
    <div>
      <h1 className="text-2xl font-semibold text-slate-900">Ask a specialist</h1>
      <p className="mt-1 text-sm text-slate-500">
        Pick one of the specialist agents and ask it a single question - read-only, no memory
        between questions. For a running conversation, use "Chat with Claims Agent" instead.
      </p>

      {catalog.isLoading && <LoadingState />}
      {catalog.error && <ErrorBanner message={apiErrorMessage(catalog.error)} />}

      {catalog.data && (
        <div className="panel mt-4 p-6">
          <label className="block text-sm text-slate-600">
            Agent
            <select
              value={agentName ?? ""}
              onChange={(e) => setAgentName(e.target.value)}
              className="field-input mt-1 block w-full max-w-sm"
            >
              {catalog.data.map((a) => (
                <option key={a.name} value={a.name}>
                  {a.displayName}
                </option>
              ))}
            </select>
          </label>

          {selected && (
            <p className="mt-2 text-xs text-slate-400">Tools: {selected.toolNames.join(", ")}</p>
          )}

          <form onSubmit={handleSubmit} className="mt-4 flex gap-3">
            <input
              value={prompt}
              onChange={(e) => setPrompt(e.target.value)}
              placeholder="Ask a question…"
              className="field-input flex-1"
              disabled={ask.isPending}
            />
            <button type="submit" className="btn-primary" disabled={ask.isPending || !prompt.trim()}>
              {ask.isPending ? "Asking…" : "Ask"}
            </button>
          </form>

          {ask.error && <ErrorBanner message={apiErrorMessage(ask.error)} />}

          {result && (
            <div className="mt-5 space-y-1.5 rounded-lg bg-slate-100 px-4 py-3 text-sm text-slate-800">
              <p className="whitespace-pre-wrap">{result.finalAnswer}</p>
              {result.reasoningText && (
                <p className="whitespace-pre-wrap text-xs text-slate-500">
                  <span className="font-medium">Reasoning:</span> {result.reasoningText}
                </p>
              )}
              <div className="flex items-center gap-2 pt-1">
                <span className={outcomeBadgeClasses(result.outcome)}>{result.outcome}</span>
                <span className="text-xs text-slate-400">{result.toolCallCount} tool call(s)</span>
              </div>
            </div>
          )}
        </div>
      )}
    </div>
  );
}
