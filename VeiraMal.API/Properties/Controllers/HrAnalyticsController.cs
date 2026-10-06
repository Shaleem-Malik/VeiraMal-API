using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VeiraMal.API.DTOs;
using VeiraMal.API.Services.Interfaces;

namespace VeiraMal.API.Controllers;

[ApiController]
[Authorize]
[Route("api/hr-analytics")]
public sealed class HrAnalyticsController : ControllerBase
{
    private readonly IHrAnalyticsService _service;
    private readonly ISubCompanyResolver _companyResolver;

    public HrAnalyticsController(IHrAnalyticsService service, ISubCompanyResolver companyResolver)
    {
        _service = service;
        _companyResolver = companyResolver;
    }

    [HttpGet("settings")]
    public async Task<ActionResult<HrAnalyticsSettingsDto>> GetSettings(
        [FromQuery] Guid? subCompanyId,
        CancellationToken cancellationToken)
    {
        if (!IsSuperUser()) return Forbid();

        var (companyId, userId) = GetCaller();

        var target = await _companyResolver.ResolveTargetCompanyIdAsync(
            companyId,
            userId,
            subCompanyId);

        return Ok(
            await _service.GetSettingsAsync(
                target,
                cancellationToken));
    }

    [HttpPut("settings")]
    public async Task<ActionResult<HrAnalyticsSettingsDto>> UpdateSettings(
        [FromBody] HrAnalyticsSettingsUpdateDto request,
        [FromQuery] Guid? subCompanyId,
        CancellationToken cancellationToken)
    {
        if (!IsSuperUser()) return Forbid();

        var (companyId, userId) = GetCaller();

        var target = await _companyResolver.ResolveTargetCompanyIdAsync(
            companyId,
            userId,
            subCompanyId);

        try
        {
            return Ok(
                await _service.UpdateSettingsAsync(
                    target,
                    request,
                    cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(
                new
                {
                    message = ex.Message
                });
        }
    }

    [HttpGet("dashboard")]
    public async Task<ActionResult<HrDashboardDto>> Dashboard([FromQuery] DateTime? asOfDate, [FromQuery] Guid? subCompanyId, CancellationToken cancellationToken)
    {
        if (!IsSuperUser()) return Forbid();
        var (companyId, userId) = GetCaller();
        var target = await _companyResolver.ResolveTargetCompanyIdAsync(companyId, userId, subCompanyId);
        var date = (asOfDate ?? DateTime.UtcNow.Date).Date;
        return Ok(await _service.GetDashboardAsync(target, date, cancellationToken));
    }

    [HttpGet("employees/search")]
    public async Task<ActionResult<IReadOnlyList<HrEmployeeSearchDto>>> SearchEmployees([FromQuery] string q, [FromQuery] Guid? subCompanyId, CancellationToken cancellationToken)
    {
        if (!IsSuperUser()) return Forbid();
        var (companyId, userId) = GetCaller();
        var target = await _companyResolver.ResolveTargetCompanyIdAsync(companyId, userId, subCompanyId);
        return Ok(await _service.SearchEmployeesAsync(target, q, cancellationToken));
    }

    [HttpPost("import")]
    [RequestSizeLimit(20 * 1024 * 1024)]
    public async Task<ActionResult<HrImportResultDto>> Import([FromForm] IFormFile file, [FromQuery] Guid? subCompanyId, CancellationToken cancellationToken)
    {
        if (!IsSuperUser()) return Forbid();
        var (companyId, userId) = GetCaller();
        var target = await _companyResolver.ResolveTargetCompanyIdAsync(companyId, userId, subCompanyId);
        try
        {
            return Ok(await _service.ImportWorkbookAsync(target, userId, file, cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    private (Guid CompanyId, int UserId) GetCaller()
    {
        var companyValue = User.FindFirstValue("companyId");
        var userValue = User.FindFirstValue("userId");
        if (!Guid.TryParse(companyValue, out var companyId) || !int.TryParse(userValue, out var userId))
            throw new UnauthorizedAccessException("The authenticated user context is invalid.");
        return (companyId, userId);
    }

    private bool IsSuperUser() => string.Equals(User.FindFirstValue("access"), "superUser", StringComparison.OrdinalIgnoreCase);
}
