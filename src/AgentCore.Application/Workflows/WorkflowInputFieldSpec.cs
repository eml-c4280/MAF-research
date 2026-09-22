namespace AgentCore.Application.Workflows;

/// <summary>One entry of a WorkflowDefinition.InputSchemaJson array.</summary>
public record WorkflowInputFieldSpec(string Name, string Type, bool Required, string Description);
