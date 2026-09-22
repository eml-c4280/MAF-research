import { useEffect, useState, type FormEvent } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { agentSessionsApi } from "../../api/agent";
import { apiErrorMessage } from "../../api/client";
import { ErrorBanner } from "../../components/ErrorBanner";
import { LoadingState } from "../../components/LoadingState";
import { outcomeBadgeClasses } from "../../utils/badgeColors";

/**
 * A real multi-turn chat thread (docs/plan.md §14) - distinct from the one-shot Q&A this page
 * used to be. Each session's history is carried server-side (the agent framework's own session
 * state, persisted and replayed by the backend); this page just lists sessions, shows one
 * session's turns (its AgentRunLogs, oldest first), and lets you send the next message or start
 * a new chat with no memory of the previous one.
 */
export function AgentQueryPage() {
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
    <div className="flex h-[calc(100vh-4rem)] gap-6">
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
