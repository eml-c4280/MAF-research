// Mirrors src/AgentCore.Api/Contracts/*.cs. Keep these in sync by hand for now - there's no
// shared-schema generation step (see docs/plan-ui.md).

export interface WorkerDto {
  id: number;
  code: string;
  name: string;
  role: string;
  location: string;
  email: string;
  phoneNumber: string;
  hourlyRate: number;
  yearsOfExperience: number;
  isAvailable: boolean;
  // Single-assignment permission model (docs/plan.md §5) - null means no CaseManager can access
  // this worker yet (deny by default), not "anyone can".
  assignedCaseManagerUserId: number | null;
}

export interface AssignCaseManagerRequest {
  caseManagerUserId: number | null;
}

export interface InsurancePolicyDto {
  id: number;
  workerId: number;
  policyNumber: string;
  provider: string;
  coverageType: string;
  coverageAmount: number;
  startDate: string; // DateOnly -> "YYYY-MM-DD"
  endDate: string;
  isActive: boolean;
}

export type ClaimStatus =
  | "Pending"
  | "UnderReview"
  | "Approved"
  | "Rejected"
  // Rule-decided and terminal (docs/business-logic.md §3/§4) - set in code when CV-1/CV-2 fail.
  | "CoverageRejected"
  // Removes the claim from all automation - agent runs are hard-blocked, not just discouraged.
  | "Disputed";

export interface ClaimDto {
  id: number;
  workerId: number;
  claimNumber: string;
  claimDate: string;
  claimType: string;
  amount: number;
  status: ClaimStatus;
  description: string;
  reviewedBy: string | null;
  reviewedAtUtc: string | null;
  agentRecommendation: string | null;
  agentConfidence: number | null;
}

// "Success" | "GuardTripped" (hit AgentOptions.MaxToolCallsPerRun) | "Timeout" (hit
// AgentOptions.MaxRunDuration) | "Failed" (unhandled exception) - see docs/plan.md §13.
export type AgentRunOutcome = "Success" | "GuardTripped" | "Timeout" | "Failed";

export interface AgentRunLogDto {
  id: number;
  claimId: number | null;
  trigger: string;
  prompt: string;
  toolCallsJson: string;
  finalAnswer: string;
  modelId: string;
  toolCallCount: number;
  reasoningText: string | null;
  inputTokenCount: number | null;
  outputTokenCount: number | null;
  totalTokenCount: number | null;
  inputCost: number | null;
  outputCost: number | null;
  totalCost: number | null;
  createdAtUtc: string;
  outcome: AgentRunOutcome;
}

export type PendingActionType = "SendWorkerEmail" | "SendEscalationEmail" | "CalculatePayout";
export type PendingActionStatus =
  | "AwaitingApproval"
  | "Approved"
  | "Rejected"
  | "Executed"
  // Approved, but the side effect itself threw (e.g. the payout write-back failing) - see
  // ExecutionError below. docs/plan.md §13.
  | "ExecutionFailed";

export interface PendingActionDto {
  id: number;
  claimId: number;
  actionType: PendingActionType;
  payload: string;
  status: PendingActionStatus;
  requestedAtUtc: string;
  decidedByRole: string | null;
  decidedAtUtc: string | null;
  expiresAt: string;
  isExpired: boolean;
  executionError: string | null;
  /** JSON: the deterministic rule engine's basis for this action, e.g. EntitlementCalculator's
   * RuleVersion + inputs for a CalculatePayout action (docs/business-logic.md §5). Null for
   * actions with no rule basis, such as a worker email. */
  ruleOutputsJson: string | null;
}

export interface ProcessClaimResponse {
  run: AgentRunLogDto;
  claimId: number;
  recommendation: string;
  claimStatus: string;
  queuedActions: PendingActionDto[];
}

export type ConversationSessionStatus = "Active" | "Archived";

export interface ConversationSessionDto {
  id: number;
  status: ConversationSessionStatus;
  title: string | null;
  createdByRole: string;
  createdByName: string;
  createdAtUtc: string;
  lastActivityAtUtc: string;
}

export interface ConversationSessionMessagesResponse {
  session: ConversationSessionDto;
  turns: AgentRunLogDto[];
}

export interface ApprovalBatchDecision {
  id: number;
  approve: boolean;
}

export interface ApprovalBatchItemResult {
  id: number;
  outcome: "NotFound" | "AlreadyDecided" | "Success" | "Expired";
  action: PendingActionDto | null;
  error: string | null;
}

// Named, reusable playbooks (docs/plan.md §11) - a fixed prompt template + input schema +
// restricted tool set, not a general workflow/step builder. See WorkflowInputFieldSpec below for
// what InputSchemaJson actually contains once parsed.
export interface WorkflowDefinitionDto {
  id: number;
  name: string;
  description: string;
  inputSchemaJson: string;
  promptTemplate: string;
  allowedToolNamesJson: string;
  isChatTriggerable: boolean;
  chatTriggerHintsJson: string;
  isActive: boolean;
  createdByRole: string;
  createdAtUtc: string;
}

/** One parsed entry of a WorkflowDefinitionDto.inputSchemaJson array. */
export interface WorkflowInputFieldSpec {
  name: string;
  type: "int" | "string" | "date";
  required: boolean;
  description: string;
}

export interface CreateWorkflowRequest {
  name: string;
  description: string;
  inputSchemaJson: string;
  promptTemplate: string;
  allowedToolNamesJson: string;
  isChatTriggerable: boolean;
  chatTriggerHintsJson: string;
}

/** The full set of MCP tools a workflow's AllowedToolNamesJson can name (src/AgentCore.Agents
 * Tools/AgentToolsFactory.cs / mcp/ClaimsToolsServer/Tools). Kept in sync by hand. */
export const KNOWN_TOOL_NAMES = [
  "WorkerInformationFetcher",
  "ClaimsSearcher",
  "WorkerClaimsHistoryFetcher",
  "CoverageChecker",
  "EscalationEvaluator",
  "ClaimRiskScorer",
  "WorkerEmailSender",
  "EscalationEmailSender",
  "PayoutCalculator",
] as const;

export interface WorkflowRunDto {
  id: number;
  workflowDefinitionId: number;
  workflowDefinitionName: string;
  agentRunLogId: number;
  inputValuesJson: string;
  triggerSource: "Structured" | "Chat";
  rawChatInput: string | null;
  matchConfidence: number | null;
  createdAtUtc: string;
}

export interface WorkflowRunResponse {
  fellBackToQuery: boolean;
  run: AgentRunLogDto;
  workflowRun: WorkflowRunDto | null;
}

// JWT-based auth (docs/plan.md §5) - the strict SuperAdmin ⊃ Admin ⊃ CaseManager hierarchy.
export type UserRole = "SuperAdmin" | "Admin" | "CaseManager";

export interface UserDto {
  id: number;
  email: string;
  name: string;
  role: UserRole;
  isActive: boolean;
  createdAtUtc: string;
  lastLoginAtUtc: string | null;
}

export interface LoginRequest {
  email: string;
  password: string;
}

export interface LoginResponse {
  accessToken: string;
  expiresAtUtc: string;
  user: UserDto;
}

export interface CreateUserRequest {
  name: string;
  email: string;
  password: string;
  role: UserRole;
}

export interface UpdateUserRequest {
  name: string;
  email: string;
}

export interface ResetPasswordRequest {
  newPassword: string;
}
