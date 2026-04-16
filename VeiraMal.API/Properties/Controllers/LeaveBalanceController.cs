using Microsoft.AspNetCore.Mvc;
using VeiraMal.API.DTOs;
using VeiraMal.API.Services.Interfaces;

namespace VeiraMal.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class LeaveBalanceController : ControllerBase
    {
        private readonly ILeaveBalanceService _service;

        public LeaveBalanceController(ILeaveBalanceService service)
        {
            _service = service;
        }

        [HttpPost("upload")]
        public async Task<ActionResult<UploadResultDto>> Upload(IFormFile file)
        {
            if (file == null) return BadRequest("No file provided.");

            var message = await _service.UploadAsync(file);
            return Ok(new UploadResultDto { Message = message });
        }
    }
}