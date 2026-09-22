namespace AgentCore.Domain.Enums;

public enum PendingActionType
{
    SendWorkerEmail,
    SendEscalationEmail,
    CalculatePayout,

    /// <summary>Phase 13: notifies a claim's worker's assigned CaseManager (Worker.
    /// AssignedCaseManagerUserId -> User.Email) - distinct from SendEscalationEmail, which goes to
    /// a supervisor/claims manager for risk review, not the worker's own case manager for a status
    /// update. Owned exclusively by the NotificationAgent - see docs/plan-agents.md.</summary>
    NotifyCaseManager
}
