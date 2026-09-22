namespace AgentCore.Domain.Enums;

public enum ClaimStatus
{
    Pending,
    UnderReview,
    Approved,
    Rejected,

    /// <summary>Rule-decided and terminal (docs/business-logic.md §3/§4, CV-1/CV-2) - no policy
    /// covers the incident, or the claim type isn't covered by the policy type. Set in code by
    /// CoverageValidator, never left to agent judgement.</summary>
    CoverageRejected,

    /// <summary>Removes the claim from all automation (docs/business-logic.md §3) - once
    /// contested, anything the agent drafts is potentially discoverable. ClaimAgentService hard-
    /// blocks agent runs on a Disputed claim rather than just discouraging them.</summary>
    Disputed
}
