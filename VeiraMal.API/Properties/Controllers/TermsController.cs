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
    }
}
