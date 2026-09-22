using System.Text.Json;
using AgentCore.Agents;
using AgentCore.Application.Agents;
using AgentCore.Domain.Entities;
using AgentCore.Domain.Enums;
using AgentCore.Domain.Repositories;
using Microsoft.Extensions.Options;

namespace AgentCore.Application.Workflows;

/// <summary>
/// Resolves a WorkflowDefinition's inputs, restricts the agent's tool set to
/// AllowedToolNamesJson, fills the prompt template, and hands off to ClaimAgentService's existing
/// run path unchanged - same PendingAction interception, same SignalR events, same AgentRunLog
/// (docs/plan.md §11). A WorkflowRun row is written alongside purely as trigger metadata.
///
/// "Process Claim" is special-cased to delegate directly to
/// ClaimAgentService.ProcessClaimAsync instead of the generic template-fill path below, since
/// claim processing has its own deterministic rule-integration (CV/ES/FR pre-computation, the
/// Disputed hard block, escalation-triggered tool restriction) that a generic prompt template
/// can't express - see docs/plan.md §11 and Phase 8's checklist note on this decision.
/// </summary>
public class WorkflowExecutionService
{
    public const string ProcessClaimWorkflowName = "Process Claim";

    private const double MinChatMatchConfidence = 0.6;

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly IWorkflowDefinitionRepository _definitions;
    private readonly IWorkflowRunRepository _runs;
    private readonly WorkerClaimAgentFactory _agentFactory;
    private readonly ClaimAgentService _claimAgentService;
    private readonly AgentOptions _options;

    public WorkflowExecutionService(
        IWorkflowDefinitionRepository definitions,
        IWorkflowRunRepository runs,
        WorkerClaimAgentFactory agentFactory,
        ClaimAgentService claimAgentService,
        IOptions<AgentOptions> options)
    {
        _definitions = definitions;
        _runs = runs;
        _agentFactory = agentFactory;
        _claimAgentService = claimAgentService;
        _options = options.Value;
    }

    public Task<IReadOnlyList<WorkflowDefinition>> ListDefinitionsAsync(CancellationToken ct = default) =>
        _definitions.GetAllAsync(ct);

    public Task<IReadOnlyList<WorkflowRun>> ListRunsAsync(CancellationToken ct = default) =>
        _runs.GetAllAsync(ct);

    public Task AddDefinitionAsync(WorkflowDefinition definition, CancellationToken ct = default) =>
        _definitions.AddAsync(definition, ct);

    public Task<WorkflowRunOutcome> RunStructuredAsync(
        int workflowDefinitionId, Dictionary<string, JsonElement> rawInputs, string trigger, CallerIdentity caller, CancellationToken ct = default) =>
        RunAsync(workflowDefinitionId, rawInputs, trigger, caller, WorkflowTriggerSource.Structured, rawChatInput: null, matchConfidence: null, ct);

    /// <summary>docs/plan.md §11's chat trigger: an intent-matching step picks the best-matching
    /// chat-triggerable workflow and extracts its inputs from free text. Falls back to
    /// ClaimAgentService.QueryAsync whenever the match is low-confidence, no workflow is
    /// catalogued, or a required input can't be resolved - never guesses.</summary>
    public async Task<WorkflowChatOutcome> RunFromChatAsync(string chatText, string trigger, CallerIdentity caller, CancellationToken ct = default)
    {
        var candidates = await _definitions.GetChatTriggerableAsync(ct);
        if (candidates.Count > 0)
        {
            var (matchedId, confidence, extractedInputs) = await MatchIntentAsync(candidates, chatText, caller, ct);

            if (matchedId.HasValue && confidence >= MinChatMatchConfidence)
            {
                var rawInputs = extractedInputs.ToDictionary(kv => kv.Key, kv => JsonSerializer.SerializeToElement(kv.Value));
                var outcome = await RunAsync(matchedId.Value, rawInputs, trigger, caller, WorkflowTriggerSource.Chat, chatText, confidence, ct);

                if (outcome.Status == WorkflowRunStatus.Completed)
                {
                    return new WorkflowChatOutcome(FellBackToQuery: false, outcome, FallbackRun: null);
                }
                // A validation error here means a required input couldn't be resolved from the
                // text - fall through to the query fallback rather than surface a confusing
                // structured-workflow error for what was a free-text chat message.
            }
        }

        var fallbackRun = await _claimAgentService.QueryAsync(chatText, trigger, caller, ct);
        return new WorkflowChatOutcome(FellBackToQuery: true, WorkflowResult: null, fallbackRun);
    }

    private async Task<WorkflowRunOutcome> RunAsync(
        int workflowDefinitionId, Dictionary<string, JsonElement> rawInputs, string trigger, CallerIdentity caller,
        WorkflowTriggerSource source, string? rawChatInput, double? matchConfidence, CancellationToken ct)
    {
        var definition = await _definitions.GetByIdAsync(workflowDefinitionId, ct);
        if (definition is null || !definition.IsActive)
        {
            return WorkflowRunOutcome.NotFoundResult($"No active workflow found with Id {workflowDefinitionId}.");
        }

        var (resolved, error) = ResolveInputs(definition, rawInputs);
        if (error is not null)
        {
            return WorkflowRunOutcome.ValidationErrorResult(error);
        }

        if (definition.Name == ProcessClaimWorkflowName)
        {
            if (!resolved.TryGetValue("claimId", out var claimIdText) || !int.TryParse(claimIdText, out var claimId))
            {
                return WorkflowRunOutcome.ValidationErrorResult("claimId is required and must be an integer.");
            }

            var processingOutcome = await _claimAgentService.ProcessClaimAsync(claimId, trigger, caller, ct);
            return processingOutcome.Status switch
            {
                ClaimProcessingStatus.NotFound => WorkflowRunOutcome.NotFoundResult($"No claim found with Id {claimId}."),
                ClaimProcessingStatus.Blocked => WorkflowRunOutcome.BlockedResult(processingOutcome.BlockReason!),
                ClaimProcessingStatus.Forbidden => WorkflowRunOutcome.ForbiddenResult(processingOutcome.BlockReason!),
                _ => await RecordRunAsync(definition, resolved, processingOutcome.Result!.Run, source, rawChatInput, matchConfidence, ct)
            };
        }

        var prompt = FillTemplate(definition.PromptTemplate, resolved);
        var allowedToolNames = JsonSerializer.Deserialize<HashSet<string>>(definition.AllowedToolNamesJson) ?? [];
        var agent = await _agentFactory.CreateWorkflowAgentAsync(allowedToolNames, caller, ct);

        int? claimIdForRun = resolved.TryGetValue("claimId", out var cid) && int.TryParse(cid, out var parsedClaimId)
            ? parsedClaimId
            : null;

        var run = await _claimAgentService.RunWithCustomAgentAsync(agent, prompt, trigger, claimIdForRun, ct);
        return await RecordRunAsync(definition, resolved, run, source, rawChatInput, matchConfidence, ct);
    }

    private async Task<WorkflowRunOutcome> RecordRunAsync(
        WorkflowDefinition definition, Dictionary<string, string> resolved, AgentRunLog run,
        WorkflowTriggerSource source, string? rawChatInput, double? matchConfidence, CancellationToken ct)
    {
        var workflowRun = new WorkflowRun
        {
            WorkflowDefinitionId = definition.Id,
            AgentRunLogId = run.Id,
            InputValuesJson = JsonSerializer.Serialize(resolved),
            TriggerSource = source,
            RawChatInput = rawChatInput,
            MatchConfidence = matchConfidence
        };
        await _runs.AddAsync(workflowRun, ct);

        // Set only after the insert completes, not in the object initializer above: `definition`
        // came from a different, already-disposed-by-now AsNoTracking() query, so attaching it to
        // the navigation property *before* AddAsync's SaveChangesAsync would pull it into this
        // save's change-tracker graph as a new "Added" entity too, and EF would try to re-insert
        // an already-existing WorkflowDefinition row with the same Id - a duplicate-key failure.
        // Setting it on the plain POCO after the save is just an in-memory assignment for the
        // immediate API response (which otherwise shows a "(unknown)" name until the caller
        // re-fetches via GET /api/workflows/runs, which loads it correctly via .Include()).
        workflowRun.WorkflowDefinition = definition;
        return WorkflowRunOutcome.CompletedResult(definition, workflowRun, run);
    }

    /// <summary>Every declared field always ends up with a resolved value (missing optional
    /// fields get a sensible default phrase) so FillTemplate's blind string-replace never leaves
    /// a dangling {placeholder} in the final prompt.</summary>
    private static (Dictionary<string, string> Resolved, string? Error) ResolveInputs(
        WorkflowDefinition definition, Dictionary<string, JsonElement> rawInputs)
    {
        var schema = JsonSerializer.Deserialize<List<WorkflowInputFieldSpec>>(definition.InputSchemaJson, JsonOptions) ?? [];
        var resolved = new Dictionary<string, string>();

        foreach (var field in schema)
        {
            if (rawInputs.TryGetValue(field.Name, out var value) && value.ValueKind != JsonValueKind.Null)
            {
                resolved[field.Name] = value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.GetRawText();
            }
            else if (field.Required)
            {
                return (resolved, $"Missing required input '{field.Name}' ({field.Description}).");
            }
            else
            {
                resolved[field.Name] = field.Type == "date"
                    ? (field.Name.Contains("From", StringComparison.OrdinalIgnoreCase) ? "the earliest available date" : "today")
                    : "unspecified";
            }
        }

        return (resolved, null);
    }

    private static string FillTemplate(string template, Dictionary<string, string> resolved)
    {
        var result = template;
        foreach (var (key, value) in resolved)
        {
            result = result.Replace("{" + key + "}", value);
        }
        return result;
    }

    /// <summary>A lightweight, one-shot classification call - deliberately not routed through
    /// ClaimAgentService.ExecuteRunAsync (this is routing infrastructure, not itself a decision
    /// worth an AgentRunLog audit row), but still carries the same MaxRunDuration guard so a
    /// stalled model can't hang the chat endpoint indefinitely.</summary>
    private async Task<(int? WorkflowId, double Confidence, Dictionary<string, string> Inputs)> MatchIntentAsync(
        IReadOnlyList<WorkflowDefinition> candidates, string chatText, CallerIdentity caller, CancellationToken ct)
    {
        var catalogue = candidates.Select(d => new
        {
            id = d.Id,
            name = d.Name,
            description = d.Description,
            inputSchema = d.InputSchemaJson,
            hints = JsonSerializer.Deserialize<List<string>>(d.ChatTriggerHintsJson) ?? new List<string>()
        });

        var intentPrompt =
            "You are a routing classifier, not a claims assistant. Given this catalogue of workflows " +
            "(JSON) and a user's request, pick the single best-matching workflow (or none if nothing " +
            "clearly matches) and extract input values for its input schema from the request text.\n\n" +
            $"Catalogue: {JsonSerializer.Serialize(catalogue)}\n\n" +
            $"User request: \"{chatText}\"\n\n" +
            "Respond with ONLY a single JSON object, no other text, no markdown fences: " +
            "{\"workflowId\": <int or null>, \"confidence\": <0.0-1.0>, \"inputs\": {\"<name>\": \"<value>\"}}. " +
            "Use null for workflowId if no workflow clearly matches.";

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(_options.MaxRunDuration);

        try
        {
            var agent = await _agentFactory.CreateReadOnlyAgentAsync(caller, timeoutCts.Token);
            var response = await agent.RunAsync(intentPrompt, cancellationToken: timeoutCts.Token);
            return ParseIntentResponse(response.Text ?? string.Empty);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return (null, 0, []); // timed out - treat as no confident match, safe to fall back
        }
        catch
        {
            return (null, 0, []); // any other failure - same safe fallback
        }
    }

    private static (int? WorkflowId, double Confidence, Dictionary<string, string> Inputs) ParseIntentResponse(string text)
    {
        var jsonStart = text.IndexOf('{');
        var jsonEnd = text.LastIndexOf('}');
        if (jsonStart < 0 || jsonEnd <= jsonStart)
        {
            return (null, 0, []);
        }

        using var doc = JsonDocument.Parse(text[jsonStart..(jsonEnd + 1)]);
        var root = doc.RootElement;

        int? workflowId = root.TryGetProperty("workflowId", out var idEl) && idEl.ValueKind == JsonValueKind.Number
            ? idEl.GetInt32()
            : null;
        var confidence = root.TryGetProperty("confidence", out var confEl) && confEl.TryGetDouble(out var c) ? c : 0.0;

        var inputs = new Dictionary<string, string>();
        if (root.TryGetProperty("inputs", out var inputsEl) && inputsEl.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in inputsEl.EnumerateObject())
            {
                inputs[prop.Name] = prop.Value.ValueKind == JsonValueKind.String ? prop.Value.GetString() ?? "" : prop.Value.GetRawText();
            }
        }

        return (workflowId, confidence, inputs);
    }
}
