using System.Text.Json;

namespace AgentCore.Api.Contracts;

public record WorkflowDefinitionDto(
    int Id,
    string Name,
    string Description,
    string InputSchemaJson,
    string PromptTemplate,
    string AllowedToolNamesJson,
    bool IsChatTriggerable,
    string ChatTriggerHintsJson,
    bool IsActive,
    string CreatedByRole,
    DateTime CreatedAtUtc);

public record CreateWorkflowRequest(
    string Name,
    string Description,
    string InputSchemaJson,
    string PromptTemplate,
    string AllowedToolNamesJson,
    bool IsChatTriggerable,
    string ChatTriggerHintsJson);

public record RunWorkflowRequest(Dictionary<string, JsonElement> Inputs);

public record WorkflowChatRequest(string Text);

public record WorkflowRunDto(
    int Id,
    int WorkflowDefinitionId,
    string WorkflowDefinitionName,
    int AgentRunLogId,
    string InputValuesJson,
    string TriggerSource,
    string? RawChatInput,
    double? MatchConfidence,
    DateTime CreatedAtUtc);

/// <summary>Response shape for both POST /api/workflows/{id}/run and POST /api/workflows/chat -
/// FellBackToQuery is only ever true for the chat endpoint.</summary>
public record WorkflowRunResponse(
    bool FellBackToQuery,
    AgentRunLogDto Run,
    WorkflowRunDto? WorkflowRun);
