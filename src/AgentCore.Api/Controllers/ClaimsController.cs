using AgentCore.Agents;
using AgentCore.Api.Auth;
using AgentCore.Api.Contracts;
using AgentCore.Domain.Authorization;
using AgentCore.Domain.Common;
using AgentCore.Domain.Entities;
using AgentCore.Domain.Enums;
using AgentCore.Domain.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgentCore.Api.Controllers;

[ApiController]
[Route("api/claims")]
[Authorize(Roles = $"{Roles.Admin},{Roles.CaseManager}")]
public class ClaimsController : ControllerBase
{
    private readonly IClaimRepository _claims;
    private readonly IWorkerRepository _workers;

    public ClaimsController(IClaimRepository claims, IWorkerRepository workers)
    {
        _claims = claims;
        _workers = workers;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ClaimDto>>> GetAll(CancellationToken ct)
    {
        var scope = ScopeFor(User.ToCallerIdentity());
        var claims = await _claims.GetAllAsync(scope, ct);
        return Ok(claims.Select(ToDto));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ClaimDto>> GetById(int id, CancellationToken ct)
    {
        var claim = await _claims.GetByIdAsync(id, ct);
        if (claim is null)
        {
            return NotFound();
        }

        if (!await CanAccessWorkerAsync(claim.WorkerId, ct))
        {
            return Forbid();
        }

        return Ok(ToDto(claim));
    }

    [HttpGet("search")]
    public async Task<ActionResult<IReadOnlyList<ClaimDto>>> Search(
        [FromQuery] string? claimType, [FromQuery] string? status, [FromQuery] int years = 3, CancellationToken ct = default)
    {
        var since = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-years);
        var scope = ScopeFor(User.ToCallerIdentity());
        var claims = await _claims.SearchAsync(claimType, status, since, scope, ct);
        return Ok(claims.Select(ToDto));
    }

    [HttpGet("workers/{workerId:int}/history")]
    public async Task<ActionResult<ClaimsHistoryResponse>> GetWorkerHistory(int workerId, [FromQuery] int years = 3, CancellationToken ct = default)
    {
        var worker = await _workers.GetByIdAsync(workerId, ct);
        if (worker is null)
        {
            return NotFound($"No worker found with Id {workerId}.");
        }

        var caller = User.ToCallerIdentity();
        if (!WorkerAccessPolicy.CanAccessWorker(worker, caller.Roles, caller.UserId))
        {
            return Forbid();
        }

        var since = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-years);
        var claims = await _claims.GetByWorkerIdAsync(workerId, since, ct);

        var byYear = claims.GroupBy(c => c.ClaimDate.Year).ToDictionary(g => g.Key, g => g.Count());
        var byStatus = claims.GroupBy(c => c.Status.ToString()).ToDictionary(g => g.Key, g => g.Count());
        var byType = claims.GroupBy(c => c.ClaimType).ToDictionary(g => g.Key, g => g.Count());

        var response = new ClaimsHistoryResponse(
            worker.Id, worker.Code, worker.Name, years,
            claims.Count,
            claims.Sum(c => c.Amount),
            claims.Count == 0 ? 0 : claims.Average(c => c.Amount),
            claims.Select(ToDto).ToList(),
            byYear, byStatus, byType);

        return Ok(response);
    }

    [HttpPost]
    public async Task<ActionResult<ClaimDto>> Create(UpsertClaimRequest request, CancellationToken ct)
    {
        if (!Enum.TryParse<ClaimStatus>(request.Status, ignoreCase: true, out var status))
        {
            return BadRequest($"Invalid status '{request.Status}'. Expected one of: {string.Join(", ", Enum.GetNames<ClaimStatus>())}.");
        }

        if (!await CanAccessWorkerAsync(request.WorkerId, ct))
        {
            return Forbid();
        }

        var claim = new Claim
        {
            WorkerId = request.WorkerId,
            ClaimNumber = request.ClaimNumber,
            ClaimDate = request.ClaimDate,
            ClaimType = request.ClaimType,
            Amount = request.Amount,
            Status = status,
            Description = request.Description
        };

        await _claims.AddAsync(claim, ct);
        return CreatedAtAction(nameof(GetById), new { id = claim.Id }, ToDto(claim));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, UpsertClaimRequest request, CancellationToken ct)
    {
        if (!Enum.TryParse<ClaimStatus>(request.Status, ignoreCase: true, out var status))
        {
            return BadRequest($"Invalid status '{request.Status}'. Expected one of: {string.Join(", ", Enum.GetNames<ClaimStatus>())}.");
        }

        var claim = await _claims.GetByIdAsync(id, ct);
        if (claim is null)
        {
            return NotFound();
        }

        if (!await CanAccessWorkerAsync(claim.WorkerId, ct))
        {
            return Forbid();
        }

        claim.WorkerId = request.WorkerId;
        claim.ClaimNumber = request.ClaimNumber;
        claim.ClaimDate = request.ClaimDate;
        claim.ClaimType = request.ClaimType;
        claim.Amount = request.Amount;
        claim.Status = status;
        claim.Description = request.Description;

        await _claims.UpdateAsync(claim, ct);
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var deleted = await _claims.DeleteAsync(id, ct);
        return deleted ? NoContent() : NotFound();
    }

    private static int? ScopeFor(CallerIdentity caller) => caller.Roles.Contains(Roles.Admin) ? null : caller.UserId;

    private async Task<bool> CanAccessWorkerAsync(int workerId, CancellationToken ct)
    {
        var worker = await _workers.GetByIdAsync(workerId, ct);
        var caller = User.ToCallerIdentity();
        return worker is not null && WorkerAccessPolicy.CanAccessWorker(worker, caller.Roles, caller.UserId);
    }

    private static ClaimDto ToDto(Claim c) => new(
        c.Id, c.WorkerId, c.ClaimNumber, c.ClaimDate, c.ClaimType, c.Amount,
        c.Status.ToString(), c.Description, c.ReviewedBy, c.ReviewedAtUtc,
        c.AgentRecommendation, c.AgentConfidence);
}
