using System.Diagnostics;
using System.Text.Json;
using AgentCore.Agents;
using AgentCore.Domain.Authorization;
using AgentCore.Domain.Diagnostics;
using AgentCore.Domain.Entities;
using AgentCore.Domain.Enums;
using AgentCore.Domain.Realtime;
using AgentCore.Domain.Repositories;
using AgentCore.Domain.Rules;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentCore.Application.Agents;

public class ClaimAgentService
{
    // Kept in sync with AgentToolsFactory's own list (mcp/ClaimsToolsServer owns the tools;
    // AgentCore.Agents keeps this same set client-side to decide which tools a run may use).
    // Used here to know which tool results carry a PendingActionRef to backfill - see section 4
    // of docs/plan-mcp.md for why the tool itself can't stamp ProposedByAgentRunId anymore.
    private static readonly HashSet<string> SensitiveToolNames =
        ["WorkerEmailSender", "EscalationEmailSender", "PayoutCalculator", "CaseManagerNotifier"];

    private readonly AgentFactory _agentFactory;
    private readonly IAgentRunLogRepository _runLogs;
    private readonly IPendingActionRepository _pendingActions;
    private readonly IClaimRepository _claims;
    private readonly IWorkerRepository _workers;
    private readonly IInsurancePolicyRepository _policies;
    private readonly IConversationSessionRepository _conversationSessions;
    private readonly IAgentActivityPublisher _activityPublisher;
    private readonly AgentOptions _options;
    private readonly ILogger<ClaimAgentService> _logger;

    public ClaimAgentService(
        AgentFactory agentFactory,
        IAgentRunLogRepository runLogs,
        IPendingActionRepository pendingActions,
        IClaimRepository claims,
        IWorkerRepository workers,
        IInsurancePolicyRepository policies,
        IConversationSessionRepository conversationSessions,
        IAgentActivityPublisher activityPublisher,
        IOptions<AgentOptions> options,
        ILogger<ClaimAgentService> logger)
    {
        _agentFactory = agentFactory;
        _runLogs = runLogs;
        _pendingActions = pendingActions;
        _claims = claims;
        _workers = workers;
        _policies = policies;
        _conversationSessions = conversationSessions;
        _activityPublisher = activityPublisher;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>Free-form Q&amp;A against a named specialist (Phase 13, docs/plan-agents.md §8) -
    /// always read-only regardless of which agent is asked (includeSensitiveTools: false), the
    /// same way this endpoint never let the model act even before there was more than one agent
    /// to pick from. <paramref name="caller"/> is asserted to mcp/ClaimsToolsServer (docs/plan.md
    /// section 5, OBO) so its tools enforce the same row-level permission a CaseManager is bound
    /// by everywhere else. Throws KeyNotFoundException for an unknown agentName - the controller
    /// translates that to 404.</summary>
    public async Task<AgentRunLog> QueryAsync(string agentName, string prompt, string trigger, CallerIdentity caller, CancellationToken ct = default)
    {
        var definition = AgentCatalog.GetByName(agentName);
        var agent = await _agentFactory.CreateAgentAsync(definition, caller, ct, includeSensitiveTools: false);
        return await ExecuteRunAsync($"query:{agentName}", prompt, trigger, claimId: null, agent, ct, agentName: agentName);
    }

    /// <summary>Runs an already-built agent (docs/plan.md §11 - `WorkflowExecutionService` builds
    /// a workflow-specific, tool-restricted agent and hands it here) through the exact same guard/
    /// observability/approval-backfill path every other run goes through - same `PendingAction`
    /// interception, same SignalR events, same `AgentRunLog`. Not for session-aware calls (there's
    /// no session-state persistence step here) - use `ContinueSessionAsync` for those.</summary>
    public Task<AgentRunLog> RunWithCustomAgentAsync(
        AIAgent agent, string prompt, string trigger, int? claimId, CancellationToken ct = default, string? agentName = null) =>
        ExecuteRunAsync("workflow", prompt, trigger, claimId, agent, ct, agentName: agentName);

    /// <summary>Starts a new, empty multi-turn chat session (docs/plan.md section 14) - read-only
    /// tools only, same as QueryAsync. Persists the framework's own (empty) serialized session
    /// state immediately so ContinueSessionAsync always has something to deserialize.</summary>
    public async Task<ConversationSession> StartSessionAsync(string createdByRole, string createdByName, CallerIdentity caller, CancellationToken ct = default)
    {
        var agent = await _agentFactory.CreateAgentAsync(AgentCatalog.GetByName(AgentCatalog.ClaimsAgent), caller, ct, includeSensitiveTools: false);
        var agentSession = await agent.CreateSessionAsync(ct);
        var state = await agent.SerializeSessionAsync(agentSession, cancellationToken: ct);

        var session = new ConversationSession
        {
            CreatedByRole = createdByRole,
            CreatedByName = createdByName,
            SerializedStateJson = state.GetRawText(),
        };
        await _conversationSessions.AddAsync(session, ct);
        return session;
    }

    /// <summary>Continues an existing chat session with one more message: rehydrates the
    /// framework's session state, runs, then re-serializes and persists the updated state so the
    /// *next* message sees this turn's history too. Returns null if the session doesn't exist.</summary>
    public async Task<ConversationTurnResult?> ContinueSessionAsync(int sessionId, string message, string trigger, CallerIdentity caller, CancellationToken ct = default)
    {
        var session = await _conversationSessions.GetByIdAsync(sessionId, ct);
        if (session is null)
        {
            return null;
        }

        var agent = await _agentFactory.CreateAgentAsync(AgentCatalog.GetByName(AgentCatalog.ClaimsAgent), caller, ct, includeSensitiveTools: false);
        var stateElement = JsonDocument.Parse(session.SerializedStateJson).RootElement;
        var agentSession = await agent.DeserializeSessionAsync(stateElement, cancellationToken: ct);

        var run = await ExecuteRunAsync(
            "query-session", message, trigger, claimId: null, agent, ct,
            session: agentSession, conversationSessionId: session.Id, agentName: AgentCatalog.ClaimsAgent);

        return new ConversationTurnResult(run, session);
    }

    /// <summary>Most-recently-active session first, for a "your chats" list.</summary>
    public Task<IReadOnlyList<ConversationSession>> ListSessionsAsync(CancellationToken ct = default) =>
        _conversationSessions.GetAllAsync(ct);

    /// <summary>The full turn-by-turn history of one session (its AgentRunLogs, oldest first),
    /// for rendering a chat thread. Returns null if the session doesn't exist.</summary>
    public async Task<ConversationSessionMessagesResult?> GetSessionMessagesAsync(int sessionId, CancellationToken ct = default)
    {
        var session = await _conversationSessions.GetByIdAsync(sessionId, ct);
        if (session is null)
        {
            return null;
        }

        var turns = await _runLogs.GetByConversationSessionIdAsync(sessionId, ct);
        return new ConversationSessionMessagesResult(session, turns);
    }

    /// <summary>Phase 13 (docs/plan-agents.md §7): pre-flight checks (NotFound/Forbidden/Disputed,
    /// unchanged from before) plus the deterministic CV/ES/FR rule computation
    /// (docs/business-logic.md §4 - "rules decide, the agent explains"), resolved once into an
    /// initial context dictionary every step of the "Process Claim" pipeline can draw on. Does
    /// NOT run any agent itself - that's RunAgentPipelineAsync, called separately by
    /// WorkflowExecutionService.ProcessClaimAsync so the permission/rule-computation half stays
    /// here (where the Claim/Worker/Policy repositories already are) while the pipeline-execution
    /// half stays where the WorkflowRun/WorkflowRunStep audit rows are written.</summary>
    public async Task<ClaimProcessingPrep> PrepareClaimForProcessingAsync(int claimId, CallerIdentity caller, CancellationToken ct = default)
    {
        var claim = await _claims.GetByIdAsync(claimId, ct);
        if (claim is null)
        {
            return new ClaimProcessingPrep(ClaimProcessingStatus.NotFound, null, null, null);
        }

        var worker = await _workers.GetByIdAsync(claim.WorkerId, ct);
        if (worker is null || !WorkerAccessPolicy.CanAccessWorker(worker, caller.Roles, caller.UserId))
        {
            _logger.LogInformation("Agent run denied for claim {ClaimId}: caller {UserId} lacks permission for worker {WorkerId}", claimId, caller.UserId, claim.WorkerId);
            return new ClaimProcessingPrep(
                ClaimProcessingStatus.Forbidden,
                $"You do not have permission to process claim #{claimId} - its worker is not assigned to you.",
                null, null);
        }

        if (claim.Status == ClaimStatus.Disputed)
        {
            _logger.LogInformation("Agent run blocked for claim {ClaimId}: status is Disputed", claimId);
            return new ClaimProcessingPrep(
                ClaimProcessingStatus.Blocked,
                $"Claim #{claimId} is Disputed - agent runs are blocked outright once a matter is contested (docs/business-logic.md §3), not just discouraged.",
                null, null);
        }

        var policy = await _policies.GetByWorkerIdAsync(claim.WorkerId, ct);
        var history = await _claims.GetByWorkerIdAsync(claim.WorkerId, DateOnly.MinValue, ct);

        var coverage = CoverageValidator.Validate(claim, policy, history);
        var escalation = EscalationEvaluator.Evaluate(claim, history, coverage);
        var risk = ClaimRiskScorer.Score(claim, history);

        // CV is rule-decided and terminal - the status transition doesn't wait on, or depend on,
        // what any agent does with this information (docs/business-logic.md §3). Not persisted
        // yet - ApplyClaimDecisionAsync saves the claim once, after the pipeline completes.
        if (!coverage.Passed && claim.Status is ClaimStatus.Pending or ClaimStatus.UnderReview)
        {
            claim.Status = ClaimStatus.CoverageRejected;
        }

        var initialContext = new Dictionary<string, string>
        {
            ["claimId"] = claimId.ToString(),
            ["coverageSummary"] = BuildCoverageSummary(coverage),
            ["escalationSummary"] = BuildEscalationSummary(escalation),
            ["riskSummary"] = BuildRiskSummary(risk)
        };

        return new ClaimProcessingPrep(ClaimProcessingStatus.Completed, null, claim, initialContext);
    }

    /// <summary>The tail of what used to be ProcessClaimAsync's single-agent version: stamps the
    /// claim with the pipeline's decision text and advances Pending -> UnderReview, then persists
    /// once. Called after the whole pipeline completes, not per step.</summary>
    public async Task ApplyClaimDecisionAsync(Claim claim, string claimDecisionText, CancellationToken ct = default)
    {
        claim.AgentRecommendation = claimDecisionText;
        if (claim.Status == ClaimStatus.Pending)
        {
            claim.Status = ClaimStatus.UnderReview;
        }
        await _claims.UpdateAsync(claim, ct);
    }

    /// <summary>Phase 13 (docs/plan-agents.md §7): runs a fixed, ordered sequence of specialist-
    /// agent steps, each through the exact same ExecuteRunAsync path every other run goes through
    /// (one AgentRunLog per step - just as fully audited as a standalone agent call). A step's
    /// PromptTemplate may reference {input} placeholders from <paramref name="initialContext"/> or
    /// {steps.OutputKey} placeholders from an earlier step's answer; each step's own answer is
    /// stored under its OutputKey for whichever later step wants it.</summary>
    public async Task<List<PipelineStepResult>> RunAgentPipelineAsync(
        IReadOnlyList<WorkflowStepDefinition> steps, Dictionary<string, string> initialContext,
        string trigger, int? claimId, CallerIdentity caller, CancellationToken ct = default)
    {
        var context = new Dictionary<string, string>(initialContext);
        var results = new List<PipelineStepResult>();

        foreach (var step in steps.OrderBy(s => s.StepIndex))
        {
            var definition = AgentCatalog.GetByName(step.AgentName);
            var resolvedPrompt = ResolveStepPlaceholders(step.PromptTemplate, context);
            var agent = await _agentFactory.CreateAgentAsync(definition, caller, ct);
            var stepRun = await RunWithCustomAgentAsync(agent, resolvedPrompt, trigger, claimId, ct, agentName: step.AgentName);

            results.Add(new PipelineStepResult(step, stepRun, resolvedPrompt));
            context[step.OutputKey] = stepRun.FinalAnswer;
        }

        return results;
    }

    /// <summary>Same blind string-replace WorkflowExecutionService.FillTemplate already uses for
    /// top-level inputs, extended with a second placeholder namespace: {steps.X} resolves the same
    /// way {X} does (both read from the one running context dictionary), it's just written that
    /// way in a step's own PromptTemplate to make "this came from an earlier step" visually
    /// explicit to whoever authors the workflow.</summary>
    private static string ResolveStepPlaceholders(string template, Dictionary<string, string> context)
    {
        var result = template;
        foreach (var (key, value) in context)
        {
            result = result.Replace("{" + key + "}", value).Replace("{steps." + key + "}", value);
        }
        return result;
    }

    /// <summary>docs/business-logic.md §5's suggested prompt shape: rule results are handed to
    /// each step as already-settled facts to quote, not something to re-derive or second-guess.</summary>
    private static string BuildCoverageSummary(CoverageValidationResult coverage) =>
        coverage.Passed
            ? "COVERED. " + string.Join(" ", coverage.Details)
            : $"NOT COVERED ({coverage.RejectionReason}) - claim status has already been set to CoverageRejected. " + string.Join(" ", coverage.Details);

    private static string BuildEscalationSummary(EscalationResult escalation) =>
        escalation.Triggered
            ? $"TRIGGERED ({string.Join(", ", escalation.TriggeredRuleIds)}) -> route to the {escalation.Tier} tier."
            : "No escalation triggers.";

    private static string BuildRiskSummary(ClaimRiskResult risk) =>
        risk.Flags.Count == 0
            ? "No automated risk flags."
            : string.Join(" ", risk.Flags.Select(f => $"{f.RuleId}: {f.Evidence}"));

    /// <summary>
    /// Runs one agent invocation end to end: persists the AgentRunLog first (so sensitive tools
    /// have an id to stamp), wraps the call in a trace span + duration/outcome metrics, publishes
    /// live progress for a UI to watch, and always records the outcome even on failure.
    /// </summary>
    private async Task<AgentRunLog> ExecuteRunAsync(
        string mode, string prompt, string trigger, int? claimId, AIAgent agent, CancellationToken ct,
        AgentSession? session = null, int? conversationSessionId = null, string? agentName = null)
    {
        var run = new AgentRunLog
        {
            ClaimId = claimId,
            ConversationSessionId = conversationSessionId,
            Trigger = trigger,
            Prompt = prompt,
            ModelId = _options.ModelId
        };
        await _runLogs.AddAsync(run, ct);

        using var activity = AgentCoreDiagnostics.ActivitySource.StartActivity("agentcore.agent.run");
        activity?.SetTag("agentcore.run.id", run.Id);
        activity?.SetTag("agentcore.run.mode", mode);
        activity?.SetTag("agentcore.run.model", run.ModelId);
        if (claimId.HasValue)
        {
            activity?.SetTag("agentcore.claim.id", claimId.Value);
        }

        var stopwatch = Stopwatch.StartNew();
        var outcome = "success";

        _logger.LogInformation("Agent run {RunId} started (mode={Mode}, model={ModelId}, trigger={Trigger})", run.Id, mode, run.ModelId, trigger);
        await _activityPublisher.RunStartedAsync(run.Id, claimId, prompt, ct);

        // Loop guard (docs/plan.md section 13): a wall-clock cap on this one run, independent of
        // the caller's own ct, so a model that stalls or the tool-call loop that never converges
        // can't hang the caller/a SignalR watcher indefinitely. The tool-call-count guard itself is
        // enforced inside the model call via FunctionInvokingChatClient.MaximumIterationsPerRequest
        // (wired in AgentFactory) - it can't be observed until the call returns, so it's
        // detected here from the resulting ToolCallCount, not by cancelling anything.
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(_options.MaxRunDuration);

        try
        {
            dynamic response = await agent.RunAsync(prompt, session, cancellationToken: timeoutCts.Token);
            string finalAnswer = response.Text ?? string.Empty;

            // Cast to object at the call site: passing a dynamic argument makes the whole call
            // (and therefore `extracted`) dynamically typed too, even though the method's
            // declared return type is concrete - the object cast suppresses that contagion.
            var extracted = await ExtractResponseDetailsAsync((object)response, run.Id, claimId, ct);

            run.FinalAnswer = finalAnswer;
            run.ToolCallsJson = extracted.ToolCallsJson;
            run.ToolCallCount = extracted.ToolCallCount;
            run.ReasoningText = extracted.ReasoningText;
            run.InputTokenCount = extracted.InputTokenCount;
            run.OutputTokenCount = extracted.OutputTokenCount;
            run.TotalTokenCount = extracted.TotalTokenCount;
            (run.InputCost, run.OutputCost, run.TotalCost) = CalculateCost(extracted.InputTokenCount, extracted.OutputTokenCount);

            if (extracted.ToolCallCount >= _options.MaxToolCallsPerRun)
            {
                run.Outcome = "GuardTripped";
                outcome = "guard_tripped";
                _logger.LogWarning(
                    "Agent run {RunId} hit MaxToolCallsPerRun ({Max}) - the model may not have converged",
                    run.Id, _options.MaxToolCallsPerRun);
            }

            await _runLogs.UpdateAsync(run, ct);

            // Persist the framework's updated session state (docs/plan.md section 14) so the
            // *next* message in this chat sees this turn's history too - only meaningful on a
            // genuinely completed turn, not the timeout/failure paths below, since nothing there
            // advanced the conversation. Best-effort: a save failure here shouldn't fail an
            // otherwise-successful run, the same principle as BackfillPendingActionRunIdAsync.
            if (conversationSessionId.HasValue && session is not null)
            {
                try
                {
                    var conversationSession = await _conversationSessions.GetByIdAsync(conversationSessionId.Value, ct);
                    if (conversationSession is not null)
                    {
                        var updatedState = await agent.SerializeSessionAsync(session, cancellationToken: ct);
                        conversationSession.SerializedStateJson = updatedState.GetRawText();
                        conversationSession.LastActivityAtUtc = DateTime.UtcNow;
                        conversationSession.Title ??= prompt.Length > 60 ? prompt[..60] + "…" : prompt;
                        await _conversationSessions.UpdateAsync(conversationSession, ct);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not persist updated session state for conversation {SessionId} (run {RunId})", conversationSessionId, run.Id);
                }
            }

            activity?.SetTag("agentcore.run.tool_call_count", run.ToolCallCount);
            activity?.SetTag("agentcore.run.outcome", run.Outcome);
            if (run.TotalTokenCount is int totalTokens)
            {
                activity?.SetTag("agentcore.run.total_token_count", totalTokens);
            }

            await _activityPublisher.RunCompletedAsync(run, ct);
            return run;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // The linked token, not the caller's own ct, fired - MaxRunDuration was exceeded.
            // This is a guard trip, not a crash: record it and hand back a non-crashing result
            // rather than letting the exception propagate to the caller/SignalR watcher.
            outcome = "timeout";
            run.Outcome = "Timeout";
            run.FinalAnswer = "The agent did not respond within the allotted time and the run was cancelled.";
            activity?.SetStatus(ActivityStatusCode.Error, "timeout");
            _logger.LogWarning(
                "Agent run {RunId} exceeded MaxRunDuration ({Timeout}) and was cancelled", run.Id, _options.MaxRunDuration);
            await _runLogs.UpdateAsync(run, ct);
            await _activityPublisher.RunFailedAsync(run.Id, claimId, run.FinalAnswer, ct);
            return run;
        }
        catch (Exception ex)
        {
            outcome = "failure";
            run.Outcome = "Failed";
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.LogError(ex, "Agent run {RunId} failed", run.Id);
            try
            {
                await _runLogs.UpdateAsync(run, ct);
            }
            catch (Exception updateEx)
            {
                _logger.LogWarning(updateEx, "Could not persist Outcome=Failed for run {RunId}", run.Id);
            }
            await _activityPublisher.RunFailedAsync(run.Id, claimId, ex.Message, ct);
            throw;
        }
        finally
        {
            stopwatch.Stop();
            // Phase 13 (docs/plan-agents.md §12): agent_name lets Grafana break "which specialist
            // is slow/failing" out by agent, not just by mode - "unknown" only for a call site
            // that predates the agent catalog and hasn't supplied one (there shouldn't be any).
            var tags = new TagList { { "mode", mode }, { "outcome", outcome }, { "agent_name", agentName ?? "unknown" } };
            AgentCoreDiagnostics.AgentRuns.Add(1, tags);
            AgentCoreDiagnostics.AgentRunDuration.Record(stopwatch.Elapsed.TotalSeconds, tags);
            _logger.LogInformation(
                "Agent run {RunId} finished in {ElapsedMs}ms (outcome={Outcome})", run.Id, stopwatch.ElapsedMilliseconds, outcome);
        }
    }

    private (decimal? Input, decimal? Output, decimal? Total) CalculateCost(int? inputTokens, int? outputTokens)
    {
        if (inputTokens is null && outputTokens is null)
        {
            return (null, null, null);
        }

        var inputCost = inputTokens is int i ? i / 1_000_000m * _options.InputPricePerMillionTokens : 0m;
        var outputCost = outputTokens is int o ? o / 1_000_000m * _options.OutputPricePerMillionTokens : 0m;
        return (inputCost, outputCost, inputCost + outputCost);
    }

    /// <summary>
    /// Walks the completed response for tool calls/results (publishing each as a live activity
    /// event - see docs/plan.md section 9 for why this is sequential, not token-level streaming),
    /// the model's "thinking" output if it exposes one (TextReasoningContent - confirmed present
    /// for qwen3's reasoning mode via Ollama), and token usage (response.Usage - confirmed
    /// populated by OllamaSharp).
    /// </summary>
    private async Task<ExtractedResponseDetails> ExtractResponseDetailsAsync(dynamic response, int runId, int? claimId, CancellationToken ct)
    {
        var calls = new List<object>();
        var toolCallCount = 0;
        string? lastToolName = null;
        string? reasoningText = null;

        foreach (var message in response.Messages)
        {
            foreach (var content in message.Contents)
            {
                switch (content)
                {
                    case FunctionCallContent call:
                        lastToolName = call.Name;
                        toolCallCount++;
                        var argumentsJson = JsonSerializer.Serialize(call.Arguments);
                        calls.Add(new { type = "call", tool = call.Name, arguments = call.Arguments });
                        await _activityPublisher.ToolCallStartedAsync(runId, claimId, call.Name, argumentsJson, ct);
                        break;

                    case FunctionResultContent result:
                        var resultText = result.Result?.ToString();
                        calls.Add(new { type = "result", result = resultText });
                        await _activityPublisher.ToolCallCompletedAsync(runId, claimId, lastToolName ?? "unknown", resultText, ct);

                        if (lastToolName is not null && SensitiveToolNames.Contains(lastToolName))
                        {
                            await BackfillPendingActionRunIdAsync(result.Result, lastToolName, runId, ct);
                        }
                        break;

                    case TextReasoningContent reasoning:
                        reasoningText = reasoningText is null ? reasoning.Text : reasoningText + reasoning.Text;
                        break;
                }
            }
        }

        int? inputTokens = null, outputTokens = null, totalTokens = null;
        try
        {
            var usage = response.Usage;
            if (usage is not null)
            {
                inputTokens = (int?)usage.InputTokenCount;
                outputTokens = (int?)usage.OutputTokenCount;
                totalTokens = (int?)usage.TotalTokenCount;
            }
        }
        catch
        {
            // Not every provider/response shape exposes Usage - token fields just stay null.
        }

        return new ExtractedResponseDetails(
            JsonSerializer.Serialize(calls), toolCallCount, reasoningText, inputTokens, outputTokens, totalTokens);
    }

    /// <summary>
    /// Sensitive MCP tools (mcp/ClaimsToolsServer) create the PendingAction row themselves but
    /// can't stamp ProposedByAgentRunId - they run in a different process with no visibility into
    /// this run's id. Instead they return a small { "pendingActionId": ... } result; this parses
    /// that out and stamps the FK from here, where run.Id is already known. See docs/plan-mcp.md
    /// section 4. Never throws - a parse failure just means the audit FK stays unset, which isn't
    /// worth failing an otherwise-successful agent run over.
    /// </summary>
    private async Task BackfillPendingActionRunIdAsync(object? toolResult, string toolName, int runId, CancellationToken ct)
    {
        try
        {
            var json = toolResult switch
            {
                JsonElement element => element,
                string text => JsonSerializer.Deserialize<JsonElement>(text),
                // MCP tool results for AIFunction/FunctionResultContent come back wrapped as
                // TextContent (confirmed against a real run - a plain string/JsonElement is
                // never what's actually here), with the real JSON payload in .Text.
                TextContent textContent => JsonSerializer.Deserialize<JsonElement>(textContent.Text),
                null => default,
                _ => JsonSerializer.SerializeToElement(toolResult)
            };

            if (json.ValueKind != JsonValueKind.Object ||
                !json.TryGetProperty("pendingActionId", out var idProperty) ||
                !idProperty.TryGetInt32(out var pendingActionId))
            {
                _logger.LogWarning("Tool '{Tool}' result for run {RunId} did not contain a pendingActionId - ProposedByAgentRunId left unset", toolName, runId);
                return;
            }

            var action = await _pendingActions.GetByIdAsync(pendingActionId, ct);
            if (action is null)
            {
                _logger.LogWarning("PendingAction #{PendingActionId} from tool '{Tool}' (run {RunId}) not found - cannot stamp ProposedByAgentRunId", pendingActionId, toolName, runId);
                return;
            }

            action.ProposedByAgentRunId = runId;
            await _pendingActions.UpdateAsync(action, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not backfill ProposedByAgentRunId for run {RunId} from tool '{Tool}' result", runId, toolName);
        }
    }

    private sealed record ExtractedResponseDetails(
        string ToolCallsJson,
        int ToolCallCount,
        string? ReasoningText,
        int? InputTokenCount,
        int? OutputTokenCount,
        int? TotalTokenCount);
}
