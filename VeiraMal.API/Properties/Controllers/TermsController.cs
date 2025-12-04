using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading.Tasks;
using VeiraMal.API.Services.Interfaces;

namespace VeiraMal.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class TermsController : ControllerBase
    {
        private readonly ITermsService _termsService;

        public TermsController(ITermsService termsService)
        {
            _termsService = termsService;
        }

        [HttpPost("upload")]
        public async Task<IActionResult> UploadTermsExcel([FromForm] IFormFile file)
        {
            try
            {
                var count = await _termsService.UploadTermsExcelAsync(file);
                return Ok($"{count} Terms records successfully uploaded.");
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetTermsData()
        {
            var data = await _termsService.GetAllTermsAsync();
            return Ok(data);
        }

        [HttpGet("analysis")]
        public async Task<IActionResult> GetTurnoverAnalysis()
        {
            var result = await _termsService.GetTurnoverAnalysisAsync();
            return Ok(result);
        }

        [HttpGet("finance-analysis")]
        public async Task<IActionResult> GetFinanceAnalysis([FromQuery] string month)
        {
            var result = await _termsService.GetFinanceAnalysisAsync(month);
            return Ok(result);
        }

        [HttpGet("analysis/export")]
        public async Task<IActionResult> ExportTurnoverAnalysis()
        {
            var bytes = await _termsService.ExportTurnoverAnalysisAsync();
            if (bytes == null || bytes.Length == 0)
                return NoContent();

            var fileName = $"Terms_Turnover_Analysis_{DateTime.UtcNow:yyyyMMddHHmmss}.xlsx";
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
        }

        [HttpGet("finance-analysis/export")]
        public async Task<IActionResult> ExportFinanceAnalysis([FromQuery] string month)
        {
            var bytes = await _termsService.ExportFinanceAnalysisAsync(month);
            if (bytes == null || bytes.Length == 0)
                return NoContent();

            var fileName = $"Terms_Finance_Analysis_{month}_{DateTime.UtcNow:yyyyMMddHHmmss}.xlsx";
            return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
        }

    }
}
