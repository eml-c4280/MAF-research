using AgentCore.Api.Auth;
using AgentCore.Api.Contracts;
using AgentCore.Domain.Authorization;
using AgentCore.Domain.Common;
using AgentCore.Domain.Entities;
using AgentCore.Domain.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgentCore.Api.Controllers;

[ApiController]
[Route("api/policies")]
[Authorize(Roles = $"{Roles.Admin},{Roles.CaseManager}")]
public class PoliciesController : ControllerBase
{
    private readonly IInsurancePolicyRepository _policies;
    private readonly IWorkerRepository _workers;

    public PoliciesController(IInsurancePolicyRepository policies, IWorkerRepository workers)
    {
        _policies = policies;
        _workers = workers;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<InsurancePolicyDto>>> GetAll(CancellationToken ct)
    {
        var caller = User.ToCallerIdentity();
        var scope = caller.Roles.Contains(Roles.Admin) ? null : (int?)caller.UserId;
        var policies = await _policies.GetAllAsync(scope, ct);
        return Ok(policies.Select(ToDto));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<InsurancePolicyDto>> GetById(int id, CancellationToken ct)
    {
        var policy = await _policies.GetByIdAsync(id, ct);
        if (policy is null)
        {
            return NotFound();
        }

        if (!await CanAccessWorkerAsync(policy.WorkerId, ct))
        {
            return Forbid();
        }

        return Ok(ToDto(policy));
    }

    [HttpGet("workers/{workerId:int}")]
    public async Task<ActionResult<InsurancePolicyDto>> GetByWorkerId(int workerId, CancellationToken ct)
    {
        if (!await CanAccessWorkerAsync(workerId, ct))
        {
            return Forbid();
        }

        var policy = await _policies.GetByWorkerIdAsync(workerId, ct);
        return policy is null ? NotFound() : Ok(ToDto(policy));
    }

    [HttpPost]
    [Authorize(Roles = Roles.Admin)]
    public async Task<ActionResult<InsurancePolicyDto>> Create(UpsertInsurancePolicyRequest request, CancellationToken ct)
    {
        var policy = new InsurancePolicy
        {
            WorkerId = request.WorkerId,
            PolicyNumber = request.PolicyNumber,
            Provider = request.Provider,
            CoverageType = request.CoverageType,
            CoverageAmount = request.CoverageAmount,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            IsActive = request.IsActive
        };

        await _policies.AddAsync(policy, ct);
        return CreatedAtAction(nameof(GetById), new { id = policy.Id }, ToDto(policy));
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Update(int id, UpsertInsurancePolicyRequest request, CancellationToken ct)
    {
        var policy = await _policies.GetByIdAsync(id, ct);
        if (policy is null)
        {
            return NotFound();
        }

        policy.WorkerId = request.WorkerId;
        policy.PolicyNumber = request.PolicyNumber;
        policy.Provider = request.Provider;
        policy.CoverageType = request.CoverageType;
        policy.CoverageAmount = request.CoverageAmount;
        policy.StartDate = request.StartDate;
        policy.EndDate = request.EndDate;
        policy.IsActive = request.IsActive;

        await _policies.UpdateAsync(policy, ct);
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var deleted = await _policies.DeleteAsync(id, ct);
        return deleted ? NoContent() : NotFound();
    }

    private async Task<bool> CanAccessWorkerAsync(int workerId, CancellationToken ct)
    {
        var worker = await _workers.GetByIdAsync(workerId, ct);
        var caller = User.ToCallerIdentity();
        return worker is not null && WorkerAccessPolicy.CanAccessWorker(worker, caller.Roles, caller.UserId);
    }

    private static InsurancePolicyDto ToDto(InsurancePolicy p) => new(
        p.Id, p.WorkerId, p.PolicyNumber, p.Provider, p.CoverageType,
        p.CoverageAmount, p.StartDate, p.EndDate, p.IsActive);
}
