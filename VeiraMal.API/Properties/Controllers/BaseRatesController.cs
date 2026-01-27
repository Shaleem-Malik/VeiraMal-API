using Microsoft.AspNetCore.Mvc;
using VeiraMal.API.Services.Interfaces;
using VeiraMal.API.DTOs;

namespace VeiraMal.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class BaseRatesController : ControllerBase
    {
        private readonly IBaseRatesService _service;

        public BaseRatesController(IBaseRatesService service)
        {
            _service = service;
        }

        [HttpPost("upload")]
        public async Task<ActionResult<UploadResultDto>> Upload(IFormFile? file)
        {
            // file is optional - service will create defaults if missing
            var message = await _service.EnsureBaseRatesAsync(file);
            return Ok(new UploadResultDto { Message = message });
        }
    }
}
