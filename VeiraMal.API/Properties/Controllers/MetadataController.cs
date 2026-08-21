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

        public MetadataController(AppDbContext db)
        {
            _db = db;
        }

        private Guid CompanyIdFromClaims()
        {
            var cid = User.Claims
                .FirstOrDefault(c => c.Type == "companyId")
                ?.Value;

            if (string.IsNullOrEmpty(cid))
                throw new UnauthorizedAccessException(
                    "CompanyId missing from token/claims."
                );

            if (!Guid.TryParse(cid, out var companyId))
                throw new UnauthorizedAccessException(
                    "Invalid CompanyId in token/claims."
                );

            return companyId;
        }

        // ============================================================
        // BUSINESS UNITS
        // ============================================================

        [HttpGet("businessunits")]
        [Authorize]
        public async Task<IActionResult> GetBusinessUnits()
        {
            var companyId = CompanyIdFromClaims();

            var list = await _db.BusinessUnits
                .Where(b => b.CompanyId == companyId)
                .OrderBy(b => b.Name)
                .ToListAsync();

            return Ok(list);
        }

        [HttpPost("businessunits")]
        [Authorize]
        public async Task<IActionResult> AddBusinessUnit(
            [FromBody] BusinessUnit model)
        {
            var companyId = CompanyIdFromClaims();

            if (model == null || string.IsNullOrWhiteSpace(model.Name))
            {
                return BadRequest(new
                {
                    Message = "Business unit name is required."
                });
            }

            var name = model.Name.Trim();

            // Prevent duplicate Business Units
            var duplicateExists = await _db.BusinessUnits
                .AnyAsync(b =>
                    b.CompanyId == companyId &&
                    b.Name.ToLower() == name.ToLower());

            if (duplicateExists)
            {
                return Conflict(new
                {
                    Message = $"Business Unit '{name}' already exists."
                });
            }

            model.BusinessUnitId = 0;
            model.CompanyId = companyId;
            model.Name = name;

            _db.BusinessUnits.Add(model);
            await _db.SaveChangesAsync();

            return Created("", model);
        }

        [HttpDelete("businessunits/{id}")]
        [Authorize]
        public async Task<IActionResult> DeleteBusinessUnit(int id)
        {
            var companyId = CompanyIdFromClaims();

            var businessUnit = await _db.BusinessUnits
                .FirstOrDefaultAsync(b =>
                    b.BusinessUnitId == id &&
                    b.CompanyId == companyId);

            if (businessUnit == null)
            {
                return NotFound(new
                {
                    Message = "Business Unit not found."
                });
            }

            // Delete only the Business Unit belonging to the
            // current user's company.
            _db.BusinessUnits.Remove(businessUnit);

            await _db.SaveChangesAsync();

            return Ok(new
            {
                Message = $"Business Unit '{businessUnit.Name}' deleted successfully."
            });
        }

        // ============================================================
        // ACCESS LEVELS
        // ============================================================

        [HttpGet("accesslevels")]
        [Authorize]
        public async Task<IActionResult> GetAccessLevels()
        {
            var companyId = CompanyIdFromClaims();

            var list = await _db.AccessLevels
                .Where(a => a.CompanyId == companyId)
                .OrderBy(a => a.Name)
                .ToListAsync();

            return Ok(list);
        }

        [HttpPost("accesslevels")]
        [Authorize]
        public async Task<IActionResult> AddAccessLevel(
            [FromBody] AccessLevel model)
        {
            var companyId = CompanyIdFromClaims();

            if (model == null || string.IsNullOrWhiteSpace(model.Name))
            {
                return BadRequest(new
                {
                    Message = "Access level name is required."
                });
            }

            var name = model.Name.Trim();

            // Prevent duplicate Access Levels
            var duplicateExists = await _db.AccessLevels
                .AnyAsync(a =>
                    a.CompanyId == companyId &&
                    a.Name.ToLower() == name.ToLower());

            if (duplicateExists)
            {
                return Conflict(new
                {
                    Message = $"Access Level '{name}' already exists."
                });
            }

            model.AccessLevelId = 0;
            model.CompanyId = companyId;
            model.Name = name;

            _db.AccessLevels.Add(model);
            await _db.SaveChangesAsync();

            return Created("", model);
        }
    }
}