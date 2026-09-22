import { useState, type FormEvent } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { workflowsApi } from "../../api/workflows";
import { apiErrorMessage } from "../../api/client";
import { useAuth } from "../../auth/RoleContext";
import type {
  CreateWorkflowRequest,
  WorkflowDefinitionDto,
  WorkflowInputFieldSpec,
  WorkflowRunResponse,
} from "../../api/types";
import { KNOWN_TOOL_NAMES } from "../../api/types";
import { LoadingState } from "../../components/LoadingState";
import { ErrorBanner } from "../../components/ErrorBanner";
import { outcomeBadgeClasses } from "../../utils/badgeColors";

export function WorkflowsPage() {
  const { hasRole } = useAuth();
  const queryClient = useQueryClient();
  const [selected, setSelected] = useState<WorkflowDefinitionDto | null>(null);
  const [showCreateForm, setShowCreateForm] = useState(false);

  const definitions = useQuery({ queryKey: ["workflows"], queryFn: workflowsApi.list });
  const runs = useQuery({ queryKey: ["workflowRuns"], queryFn: workflowsApi.listRuns });

  const invalidateAll = () => {
    queryClient.invalidateQueries({ queryKey: ["workflows"] });
    queryClient.invalidateQueries({ queryKey: ["workflowRuns"] });
  };

  return (
    <div>
      <div className="flex items-start justify-between">
        <div>
          <h1 className="text-2xl font-semibold text-slate-900">Workflows</h1>
          <p className="mt-2 max-w-2xl text-sm text-slate-500">
            Named, parameterized playbooks - not a general workflow builder. Each one is a fixed
            prompt template + a restricted tool set; trigger it with structured inputs or free text.
          </p>
        </div>
        {hasRole("Admin") && (
          <button type="button" className="btn-primary shrink-0" onClick={() => setShowCreateForm((v) => !v)}>
            {showCreateForm ? "Cancel" : "+ New workflow"}
          </button>
        )}
      </div>

      {showCreateForm && (
        <CreateWorkflowForm
          onCreated={() => {
            setShowCreateForm(false);
            invalidateAll();
          }}
        />
      )}

      <ChatTriggerBox onRan={invalidateAll} />

      <h2 className="mt-8 text-lg font-semibold text-slate-900">Catalog</h2>
      {definitions.isLoading && <LoadingState />}
      {definitions.error && <ErrorBanner message={apiErrorMessage(definitions.error)} />}
      {definitions.data && (
        <div className="panel mt-3 overflow-hidden">
          <table className="data-table">
            <thead>
              <tr>
                <th>Name</th>
                <th>Description</th>
                <th>Chat-triggerable</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              {definitions.data.map((d) => (
                <tr key={d.id}>
                  <td className="font-medium text-slate-900">{d.name}</td>
                  <td className="max-w-md text-sm text-slate-500">{d.description}</td>
                  <td>{d.isChatTriggerable ? "Yes" : "No"}</td>
                  <td>
                    <button type="button" className="btn-primary" onClick={() => setSelected(d)}>
                      Run
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {selected && (
        <RunWorkflowPanel
          definition={selected}
          onClose={() => setSelected(null)}
          onRan={invalidateAll}
        />
      )}

      <h2 className="mt-8 text-lg font-semibold text-slate-900">Run history</h2>
      {runs.isLoading && <LoadingState />}
      {runs.error && <ErrorBanner message={apiErrorMessage(runs.error)} />}
      {runs.data && (
        <div className="panel mt-3 overflow-hidden">
          <table className="data-table">
            <thead>
              <tr>
                <th>When</th>
                <th>Workflow</th>
                <th>Trigger</th>
                <th>Inputs</th>
                <th>Confidence</th>
              </tr>
            </thead>
            <tbody>
              {runs.data.length === 0 && (
                <tr>
                  <td colSpan={5} className="text-slate-500">
                    No workflow runs yet.
                  </td>
                </tr>
              )}
              {runs.data.map((r) => (
                <tr key={r.id}>
                  <td className="whitespace-nowrap">{new Date(r.createdAtUtc).toLocaleString()}</td>
                  <td className="font-medium text-slate-900">{r.workflowDefinitionName}</td>
                  <td>{r.triggerSource}</td>
                  <td className="max-w-xs">
                    <code className="text-xs text-slate-500">{r.inputValuesJson}</code>
                  </td>
                  <td>{r.matchConfidence !== null ? r.matchConfidence.toFixed(2) : "-"}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}

/** Renders one input per WorkflowInputFieldSpec (parsed from InputSchemaJson) and runs the
 * workflow with whatever's filled in - optional fields can be left blank. */
function RunWorkflowPanel({
  definition,
  onClose,
  onRan,
}: {
  definition: WorkflowDefinitionDto;
  onClose: () => void;
  onRan: () => void;
}) {
  let fields: WorkflowInputFieldSpec[] = [];
  try {
    fields = JSON.parse(definition.inputSchemaJson) as WorkflowInputFieldSpec[];
  } catch {
    fields = [];
  }

  const [values, setValues] = useState<Record<string, string>>({});
  const [result, setResult] = useState<WorkflowRunResponse | null>(null);

  const run = useMutation({
    mutationFn: () => {
      const inputs: Record<string, string | number> = {};
      for (const field of fields) {
        const raw = values[field.name];
        if (raw === undefined || raw === "") continue;
        inputs[field.name] = field.type === "int" ? Number(raw) : raw;
      }
      return workflowsApi.run(definition.id, inputs);
    },
    onSuccess: (data) => {
      setResult(data);
      onRan();
    },
  });

  function handleSubmit(e: FormEvent) {
    e.preventDefault();
    run.mutate();
  }

  return (
    <div className="panel mt-4 p-6">
      <div className="flex items-center justify-between">
        <h3 className="text-base font-semibold text-slate-900">Run "{definition.name}"</h3>
        <button type="button" className="text-sm text-slate-400 hover:text-slate-600" onClick={onClose}>
          Close
        </button>
      </div>

      <form onSubmit={handleSubmit} className="mt-4 space-y-3">
        {fields.map((field) => (
          <label key={field.name} className="block text-sm text-slate-600">
            {field.name}
            {field.required && <span className="text-red-500"> *</span>}
            <span className="ml-1 text-xs text-slate-400">({field.description})</span>
            <input
              type={field.type === "int" ? "number" : field.type === "date" ? "date" : "text"}
              required={field.required}
              value={values[field.name] ?? ""}
              onChange={(e) => setValues((v) => ({ ...v, [field.name]: e.target.value }))}
              className="field-input mt-1 block w-full max-w-xs"
            />
          </label>
        ))}
        <button type="submit" className="btn-primary" disabled={run.isPending}>
          {run.isPending ? "Running…" : "Run"}
        </button>
      </form>

      {run.error && <ErrorBanner message={apiErrorMessage(run.error)} />}

      {result && (
        <div className="mt-5 border-t border-slate-200 pt-4">
          <div className="flex items-center gap-2">
            <span className={outcomeBadgeClasses(result.run.outcome)}>{result.run.outcome}</span>
            <span className="text-xs text-slate-400">{result.run.toolCallCount} tool call(s)</span>
          </div>
          <p className="mt-2 whitespace-pre-wrap text-sm text-slate-700">{result.run.finalAnswer}</p>
        </div>
      )}
    </div>
  );
}

function ChatTriggerBox({ onRan }: { onRan: () => void }) {
  const [text, setText] = useState("");
  const [result, setResult] = useState<WorkflowRunResponse | null>(null);

  const send = useMutation({
    mutationFn: (t: string) => workflowsApi.chat(t),
    onSuccess: (data) => {
      setResult(data);
      onRan();
    },
  });

  function handleSubmit(e: FormEvent) {
    e.preventDefault();
    if (text.trim()) send.mutate(text.trim());
  }

  return (
    <div className="panel mt-6 p-6">
      <h2 className="text-lg font-semibold text-slate-900">Chat trigger</h2>
      <p className="mt-1 text-sm text-slate-500">
        Free text is routed to the best-matching workflow above, or falls back to a plain agent
        query if nothing matches confidently.
      </p>
      <form onSubmit={handleSubmit} className="mt-3 flex gap-3">
        <input
          value={text}
          onChange={(e) => setText(e.target.value)}
          placeholder='e.g. "show me the claims history for worker 1"'
          className="field-input flex-1"
          disabled={send.isPending}
        />
        <button type="submit" className="btn-primary" disabled={send.isPending || !text.trim()}>
          {send.isPending ? "Sending…" : "Send"}
        </button>
      </form>

      {send.error && <ErrorBanner message={apiErrorMessage(send.error)} />}

      {result && (
        <div className="mt-4 border-t border-slate-200 pt-4">
          <p className="text-sm">
            {result.fellBackToQuery ? (
              <span className="text-amber-700">Fell back to a plain agent query (no confident workflow match).</span>
            ) : (
              <span className="text-emerald-700">
                Matched workflow "{result.workflowRun?.workflowDefinitionName}" (confidence{" "}
                {result.workflowRun?.matchConfidence?.toFixed(2)}).
              </span>
            )}
          </p>
          <div className="mt-2 flex items-center gap-2">
            <span className={outcomeBadgeClasses(result.run.outcome)}>{result.run.outcome}</span>
          </div>
          <p className="mt-2 whitespace-pre-wrap text-sm text-slate-700">{result.run.finalAnswer}</p>
        </div>
      )}
    </div>
  );
}

function CreateWorkflowForm({ onCreated }: { onCreated: () => void }) {
  const [name, setName] = useState("");
  const [description, setDescription] = useState("");
  const [promptTemplate, setPromptTemplate] = useState("");
  const [isChatTriggerable, setIsChatTriggerable] = useState(true);
  const [chatHints, setChatHints] = useState("");
  const [fields, setFields] = useState<WorkflowInputFieldSpec[]>([
    { name: "", type: "int", required: true, description: "" },
  ]);
  const [allowedTools, setAllowedTools] = useState<Set<string>>(new Set());

  const create = useMutation({
    mutationFn: (request: CreateWorkflowRequest) => workflowsApi.create(request),
    onSuccess: onCreated,
  });

  function updateField(index: number, patch: Partial<WorkflowInputFieldSpec>) {
    setFields((prev) => prev.map((f, i) => (i === index ? { ...f, ...patch } : f)));
  }

  function toggleTool(toolName: string) {
    setAllowedTools((prev) => {
      const next = new Set(prev);
      if (next.has(toolName)) next.delete(toolName);
      else next.add(toolName);
      return next;
    });
  }

  function handleSubmit(e: FormEvent) {
    e.preventDefault();
    create.mutate({
      name,
      description,
      promptTemplate,
      isChatTriggerable,
      inputSchemaJson: JSON.stringify(fields.filter((f) => f.name.trim())),
      allowedToolNamesJson: JSON.stringify([...allowedTools]),
      chatTriggerHintsJson: JSON.stringify(
        chatHints
          .split(",")
          .map((h) => h.trim())
          .filter(Boolean),
      ),
    });
  }

  return (
    <form onSubmit={handleSubmit} className="panel mt-4 space-y-4 p-6">
      <h3 className="text-base font-semibold text-slate-900">New workflow</h3>

      <label className="block text-sm text-slate-600">
        Name
        <input value={name} onChange={(e) => setName(e.target.value)} required className="field-input mt-1 block w-full" />
      </label>

      <label className="block text-sm text-slate-600">
        Description
        <input
          value={description}
          onChange={(e) => setDescription(e.target.value)}
          required
          className="field-input mt-1 block w-full"
        />
      </label>

      <label className="block text-sm text-slate-600">
        Prompt template ({"{"}fieldName{"}"} slots filled from the inputs below)
        <textarea
          value={promptTemplate}
          onChange={(e) => setPromptTemplate(e.target.value)}
          required
          rows={3}
          className="field-input mt-1 block w-full"
        />
      </label>

      <div>
        <span className="text-sm text-slate-600">Inputs</span>
        <div className="mt-1 space-y-2">
          {fields.map((field, i) => (
            <div key={i} className="flex items-center gap-2">
              <input
                placeholder="name"
                value={field.name}
                onChange={(e) => updateField(i, { name: e.target.value })}
                className="field-input w-32"
              />
              <select
                value={field.type}
                onChange={(e) => updateField(i, { type: e.target.value as WorkflowInputFieldSpec["type"] })}
                className="field-input w-28"
              >
                <option value="int">int</option>
                <option value="string">string</option>
                <option value="date">date</option>
              </select>
              <label className="flex items-center gap-1 text-xs text-slate-500">
                <input
                  type="checkbox"
                  checked={field.required}
                  onChange={(e) => updateField(i, { required: e.target.checked })}
                />
                required
              </label>
              <input
                placeholder="description"
                value={field.description}
                onChange={(e) => updateField(i, { description: e.target.value })}
                className="field-input flex-1"
              />
              <button
                type="button"
                className="text-xs text-red-500"
                onClick={() => setFields((prev) => prev.filter((_, idx) => idx !== i))}
              >
                Remove
              </button>
            </div>
          ))}
        </div>
        <button
          type="button"
          className="mt-2 text-xs text-brand-700"
          onClick={() => setFields((prev) => [...prev, { name: "", type: "int", required: true, description: "" }])}
        >
          + Add input
        </button>
      </div>

      <div>
        <span className="text-sm text-slate-600">Allowed tools (the agent can only call these)</span>
        <div className="mt-1 grid grid-cols-3 gap-1.5">
          {KNOWN_TOOL_NAMES.map((tool) => (
            <label key={tool} className="flex items-center gap-1.5 text-xs text-slate-600">
              <input type="checkbox" checked={allowedTools.has(tool)} onChange={() => toggleTool(tool)} />
              {tool}
            </label>
          ))}
        </div>
      </div>

      <label className="flex items-center gap-2 text-sm text-slate-600">
        <input type="checkbox" checked={isChatTriggerable} onChange={(e) => setIsChatTriggerable(e.target.checked)} />
        Chat-triggerable
      </label>

      {isChatTriggerable && (
        <label className="block text-sm text-slate-600">
          Chat trigger hints (comma-separated example phrases)
          <input
            value={chatHints}
            onChange={(e) => setChatHints(e.target.value)}
            className="field-input mt-1 block w-full"
          />
        </label>
      )}

      {create.error && <ErrorBanner message={apiErrorMessage(create.error)} />}

      <button type="submit" className="btn-primary" disabled={create.isPending}>
        {create.isPending ? "Creating…" : "Create workflow"}
      </button>
    </form>
  );
}
