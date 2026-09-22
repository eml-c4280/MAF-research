namespace AgentCore.Domain.Enums;

public enum PendingActionStatus
{
    AwaitingApproval,
    Approved,
    Rejected,
    Executed,

    /// <summary>Approved, but the side effect itself threw when ApprovalService tried to execute
    /// it (e.g. the payout write-back failing) - see PendingAction.ExecutionError for detail.</summary>
    ExecutionFailed
}
