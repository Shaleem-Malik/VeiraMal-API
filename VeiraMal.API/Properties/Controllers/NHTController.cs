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

        [HttpGet("finance-analysis")]
        public async Task<IActionResult> GetAnalysis([FromQuery] string month)
        {
            var result = await _nhtService.GetFinanceAnalysisAsync(month);
            return Ok(result);
        }

        [HttpGet("analysis/export")]
        public async Task<IActionResult> ExportAnalysis()
        {
            var bytes = await _nhtService.ExportAnalysisAsync();
            if (bytes == null || bytes.Length == 0)
                return NoContent();

            var fileName = $"NHT_Analysis_{DateTime.UtcNow:yyyyMMddHHmmss}.xlsx";
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
        }

        [HttpGet("finance-analysis/export")]
        public async Task<IActionResult> ExportFinanceAnalysis([FromQuery] string month)
        {
            if (string.IsNullOrWhiteSpace(month))
                return BadRequest("Month is required as query parameter (e.g. ?month=2025-11).");

            var bytes = await _nhtService.ExportFinanceAnalysisAsync(month);
            if (bytes == null || bytes.Length == 0)
                return NoContent();

            var fileName = $"NHT_Finance_Analysis_{month}_{DateTime.UtcNow:yyyyMMddHHmmss}.xlsx";
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
        }

    }
}
