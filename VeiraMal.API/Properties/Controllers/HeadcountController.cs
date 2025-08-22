using Microsoft.AspNetCore.Mvc;
using VeiraMal.API.Services.Interfaces;

namespace VeiraMal.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class HeadcountController : ControllerBase
    {
        private readonly IHeadcountService _headcountService;

        public HeadcountController(IHeadcountService headcountService)
        {
            _headcountService = headcountService;
        }

        [HttpPost("upload")]
        public async Task<IActionResult> Upload(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest("File is required.");

            var message = await _headcountService.UploadAsync(file);
            return Ok(message);
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            return Ok(await _headcountService.GetAllAsync());
        }

        [HttpGet("analysis")]
        public async Task<IActionResult> GetAnalysis()
        {
            return Ok(await _headcountService.GetAnalysisAsync());
        }
    }
}
