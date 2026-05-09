using Microsoft.AspNetCore.Mvc;
using VeiraMal.API.DTOs;
using VeiraMal.API.Services.Interfaces;

namespace VeiraMal.API.Properties.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AbnController : ControllerBase
{
    private readonly IAbnLookupService _abnLookupService;

    public AbnController(IAbnLookupService abnLookupService)
    {
        _abnLookupService = abnLookupService;
    }

    [HttpPost("validate")]
    public async Task<IActionResult> Validate([FromBody] ValidateAbnRequestDto request, CancellationToken cancellationToken)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Abn))
            return BadRequest(new { message = "ABN is required." });

        var result = await _abnLookupService.ValidateAbnAsync(request.Abn, cancellationToken);

        if (!result.IsValid)
            return BadRequest(result);

        return Ok(result);
    }
}