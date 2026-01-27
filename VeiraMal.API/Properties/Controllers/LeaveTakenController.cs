using Microsoft.AspNetCore.Mvc;
using VeiraMal.API.Services.Interfaces;
using VeiraMal.API.DTOs;

namespace VeiraMal.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class LeaveTakenController : ControllerBase
    {
        private readonly ILeaveTakenService _service;

        public LeaveTakenController(ILeaveTakenService service)
        {
            _service = service;
        }

        [HttpPost("upload")]
        public async Task<ActionResult<UploadResultDto>> Upload(IFormFile file)
        {
            if (file == null) return BadRequest("No file provided.");
            var message = await _service.UploadAsync(file);
            // In the service we created an UploadBatch; return basic message (service may return batch id)
            return Ok(new UploadResultDto { Message = message });
        }
    }
}
