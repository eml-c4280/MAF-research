using System.ComponentModel;
using AgentCore.Domain.Authorization;
using AgentCore.Domain.Repositories;
using ClaimsToolsServer.Authorization;
using ModelContextProtocol.Server;

namespace ClaimsToolsServer.Tools;

/// <summary>Auto tool: read-only, safe for the agent to call without approval. Row-level scoped
/// (docs/plan.md section 5): denies a CaseManager caller a worker not assigned to them.</summary>
[McpServerToolType]
public class FetchWorkerTool
{
    private readonly IWorkerRepository _workers;
    private readonly IInsurancePolicyRepository _policies;
    private readonly CallerContext _caller;

    public FetchWorkerTool(IWorkerRepository workers, IInsurancePolicyRepository policies, CallerContext caller)
    {
        _workers = workers;
        _policies = policies;
        _caller = caller;
    }

    [McpServerTool(Name = "WorkerInformationFetcher", ReadOnly = true)]
    [Description("Fetch a worker's record (role, location, contact info, years of experience, availability, insurance policy) by worker Code, ID, or name, so claims made about the worker can be verified against the actual record.")]
    public async Task<string> FetchWorkerAsync(
        [Description("The worker's Code (e.g. 'ABC-901'), numeric ID, or full/partial name if neither is known.")] string workerId)
    {
        var worker = await _workers.FindByCodeOrIdOrNameAsync(workerId);
        if (worker is null)
        {
            return $"No worker found matching '{workerId}'.";
        }

        if (!WorkerAccessPolicy.CanAccessWorker(worker, _caller.CallerRoles, _caller.UserId))
        {
            return $"You do not have permission to access worker '{workerId}' - they are not assigned to you.";
        }

        var policy = await _policies.GetByWorkerIdAsync(worker.Id);
        var policyInfo = policy is null
            ? "No insurance policy on file."
            : $"Policy {policy.PolicyNumber} with {policy.Provider} ({policy.CoverageType}, coverage {policy.CoverageAmount:C}, " +
              $"{policy.StartDate:yyyy-MM-dd} to {policy.EndDate:yyyy-MM-dd}, {(policy.IsActive ? "Active" : "Expired")}).";

        return $"Worker {worker.Code} (#{worker.Id}): {worker.Name}, Role: {worker.Role}, Location: {worker.Location}, " +
               $"Email: {worker.Email}, Phone: {worker.PhoneNumber}, HourlyRate: {worker.HourlyRate:C}, " +
               $"YearsOfExperience: {worker.YearsOfExperience}, Available: {worker.IsAvailable}. Insurance: {policyInfo}";
    }
}
