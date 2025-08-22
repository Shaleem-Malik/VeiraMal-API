using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using VeiraMal.API.Models;
using VeiraMal.API.DTOs;

namespace VeiraMal.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AnalysisHistoryController : ControllerBase
    {
        private readonly AppDbContext _context;

        public AnalysisHistoryController(AppDbContext context)
        {
            _context = context;
        }

        [HttpPost("save")]
        public async Task<IActionResult> SaveAnalysis([FromBody] SaveAnalysisRequest request)
        {
            var history = new AnalysisHistory
            {
                AnalysisDate = DateTime.Now,
                HeadcountData = JsonSerializer.Serialize(request.Headcount),
                NHTData = JsonSerializer.Serialize(request.NHT),
                TermsData = JsonSerializer.Serialize(request.Terms)
            };

            _context.AnalysisHistory.Add(history);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Analysis saved successfully", historyId = history.Id });
        }

        [HttpGet("all")]
        public async Task<IActionResult> GetAllHistory()
        {
            var histories = await _context.AnalysisHistory
                .OrderByDescending(h => h.AnalysisDate)
                .Select(h => new
                {
                    h.Id,
                    h.AnalysisDate
                })
                .ToListAsync();

            return Ok(histories);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetAnalysisById(int id)
        {
            var history = await _context.AnalysisHistory.FindAsync(id);
            if (history == null) return NotFound();

            return Ok(new
            {
                history.AnalysisDate,
                Headcount = JsonSerializer.Deserialize<object>(history.HeadcountData),
                NHT = JsonSerializer.Deserialize<object>(history.NHTData),
                Terms = JsonSerializer.Deserialize<object>(history.TermsData)
            });
        }
    }
}
