// Controllers/NHTController.cs
using Microsoft.AspNetCore.Mvc;
using VeiraMal.API.Services.Interfaces;

namespace VeiraMal.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class NHTController : ControllerBase
    {
        private readonly INHTService _nhtService;

        public NHTController(INHTService nhtService)
        {
            _nhtService = nhtService;
        }

        [HttpPost("upload")]
        public async Task<IActionResult> UploadNhtExcel(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest("No file uploaded.");

            try
            {
                var message = await _nhtService.UploadAsync(file);
                return Ok(message);
            }
            catch (InvalidDataException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetNHTData()
        {
            return Ok(await _nhtService.GetAllAsync());
        }

        [HttpGet("analysis")]
        public async Task<IActionResult> GetNhtAnalysis()
        {
            return Ok(await _nhtService.GetAnalysisAsync());
        }
    }
}
