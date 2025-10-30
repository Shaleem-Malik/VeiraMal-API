using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VeiraMal.API.Models;

namespace VeiraMal.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class MetadataController : ControllerBase
    {
        private readonly AppDbContext _db;
        public MetadataController(AppDbContext db) => _db = db;

        private Guid CompanyIdFromClaims()
        {
            var cid = User.Claims.FirstOrDefault(c => c.Type == "companyId")?.Value;
            if (string.IsNullOrEmpty(cid)) throw new UnauthorizedAccessException("CompanyId missing from token/claims.");
            return Guid.Parse(cid);
        }

        [HttpGet("businessunits")]
        [Authorize]
        public async Task<IActionResult> GetBusinessUnits()
        {
            var companyId = CompanyIdFromClaims();
            var list = await _db.BusinessUnits.Where(b => b.CompanyId == companyId).ToListAsync();
            return Ok(list);
        }

        [HttpPost("businessunits")]
        [Authorize]
        public async Task<IActionResult> AddBusinessUnit([FromBody] BusinessUnit model)
        {
            var companyId = CompanyIdFromClaims();
            model.CompanyId = companyId;
            _db.BusinessUnits.Add(model);
            await _db.SaveChangesAsync();
            return Created("", model);
        }

        [HttpGet("accesslevels")]
        [Authorize]
        public async Task<IActionResult> GetAccessLevels()
        {
            var companyId = CompanyIdFromClaims();
            var list = await _db.AccessLevels.Where(a => a.CompanyId == companyId).ToListAsync();
            return Ok(list);
        }

        [HttpPost("accesslevels")]
        [Authorize]
        public async Task<IActionResult> AddAccessLevel([FromBody] AccessLevel model)
        {
            var companyId = CompanyIdFromClaims();
            model.CompanyId = companyId;
            _db.AccessLevels.Add(model);
            await _db.SaveChangesAsync();
            return Created("", model);
        }
    }
}
