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
[Route("api/workers")]
[Authorize(Roles = $"{Roles.Admin},{Roles.CaseManager}")]
public class WorkersController : ControllerBase
{
    private readonly IWorkerRepository _workers;

    public WorkersController(IWorkerRepository workers) => _workers = workers;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<WorkerDto>>> GetAll(CancellationToken ct)
    {
        var caller = User.ToCallerIdentity();
        var scope = caller.Roles.Contains(Roles.Admin) ? null : (int?)caller.UserId;
        var workers = await _workers.GetAllAsync(scope, ct);
        return Ok(workers.Select(ToDto));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<WorkerDto>> GetById(int id, CancellationToken ct)
    {
        var worker = await _workers.GetByIdAsync(id, ct);
        if (worker is null)
        {
            return NotFound();
        }

        var caller = User.ToCallerIdentity();
        if (!WorkerAccessPolicy.CanAccessWorker(worker, caller.Roles, caller.UserId))
        {
            return Forbid();
        }

        return Ok(ToDto(worker));
    }

    [HttpPost]
    [Authorize(Roles = Roles.Admin)]
    public async Task<ActionResult<WorkerDto>> Create(UpsertWorkerRequest request, CancellationToken ct)
    {
        var worker = new Worker
        {
            Code = request.Code,
            Name = request.Name,
            Role = request.Role,
            Location = request.Location,
            Email = request.Email,
            PhoneNumber = request.PhoneNumber,
            HourlyRate = request.HourlyRate,
            YearsOfExperience = request.YearsOfExperience,
            IsAvailable = request.IsAvailable
        };

        await _workers.AddAsync(worker, ct);
        return CreatedAtAction(nameof(GetById), new { id = worker.Id }, ToDto(worker));
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Update(int id, UpsertWorkerRequest request, CancellationToken ct)
    {
        var worker = await _workers.GetByIdAsync(id, ct);
        if (worker is null)
        {
            return NotFound();
        }

        worker.Code = request.Code;
        worker.Name = request.Name;
        worker.Role = request.Role;
        worker.Location = request.Location;
        worker.Email = request.Email;
        worker.PhoneNumber = request.PhoneNumber;
        worker.HourlyRate = request.HourlyRate;
        worker.YearsOfExperience = request.YearsOfExperience;
        worker.IsAvailable = request.IsAvailable;

        await _workers.UpdateAsync(worker, ct);
        return NoContent();
    }

    /// <summary>The single-assignment permission model (docs/plan.md section 5): a CaseManager
    /// can only access a Worker whose AssignedCaseManagerUserId matches their own user id. Only
    /// an Admin/SuperAdmin can change that assignment.</summary>
    [HttpPut("{id:int}/assign-case-manager")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> AssignCaseManager(int id, AssignCaseManagerRequest request, CancellationToken ct)
    {
        var worker = await _workers.GetByIdAsync(id, ct);
        if (worker is null)
        {
            return NotFound();
        }

        worker.AssignedCaseManagerUserId = request.CaseManagerUserId;
        await _workers.UpdateAsync(worker, ct);
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var deleted = await _workers.DeleteAsync(id, ct);
        return deleted ? NoContent() : NotFound();
    }

    private static WorkerDto ToDto(Worker w) => new(
        w.Id, w.Code, w.Name, w.Role, w.Location, w.Email, w.PhoneNumber,
        w.HourlyRate, w.YearsOfExperience, w.IsAvailable, w.AssignedCaseManagerUserId);
}
