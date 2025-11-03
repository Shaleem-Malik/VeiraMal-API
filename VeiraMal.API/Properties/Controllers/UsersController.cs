using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VeiraMal.API.DTOs;
using VeiraMal.API.Services.Interfaces;
using VeiraMal.API.Models;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;   // for EF query extensions
using System;

namespace VeiraMal.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class UsersController : ControllerBase
    {
        private readonly IUserManagementService _manager;
        private readonly AppDbContext _db;

        public UsersController(IUserManagementService manager, AppDbContext db)
        {
            _manager = manager;
            _db = db;
        }

        // --- helpers to read claims ---
        private Guid BaseCompanyIdFromClaims()
        {
            var cid = User.Claims.FirstOrDefault(c => c.Type == "companyId")?.Value;
            if (string.IsNullOrEmpty(cid))
                throw new UnauthorizedAccessException("CompanyId missing from token/claims.");
            return Guid.Parse(cid);
        }

        private int CallerUserIdFromClaims()
        {
            var uid = User.Claims.FirstOrDefault(c => c.Type == "userId")?.Value;
            if (string.IsNullOrEmpty(uid)) throw new UnauthorizedAccessException("userId missing from token/claims.");
            return int.Parse(uid);
        }

        /// <summary>
        /// Determine which companyId the caller intends to act as:
        /// - If querySubCompanyId provided => validate caller has assignment to it (or it's the base company)
        /// - If no query and caller has 0 assignments => return baseCompanyId
        /// - If caller has exactly 1 assignment => return that assigned subcompany id
        /// - If multiple assignments and no query => throw InvalidOperationException (client must pass subCompanyId)
        /// </summary>
        // Update your ResolveTargetCompanyIdAsync method (likely in SubCompanyResolver service)
        public async Task<Guid> ResolveTargetCompanyIdAsync(Guid baseCompanyId, int callerUserId, Guid? querySubCompanyId)
        {
            // If client explicitly requested a target
            if (querySubCompanyId.HasValue)
            {
                var requested = querySubCompanyId.Value;

                // If requested equals base company -> allow (parent company superuser can always access parent)
                if (requested == baseCompanyId)
                    return baseCompanyId;

                // Else validate caller is assigned to that subcompany
                var assigned = await _db.CompanySuperUserAssignments
                    .AnyAsync(a => a.CompanyId == requested && a.UserId == callerUserId);

                if (!assigned)
                    throw new UnauthorizedAccessException("Caller is not assigned to the requested subcompany.");

                return requested;
            }

            // No explicit request -> see how many subcompany assignments the caller has
            var subCompanyAssignments = await _db.CompanySuperUserAssignments
                .Where(a => a.UserId == callerUserId)
                .Select(a => a.CompanyId)
                .Distinct()
                .ToListAsync();

            // If no subcompany assignments -> operate on base company
            if (subCompanyAssignments.Count == 0)
            {
                return baseCompanyId;
            }

            // If has subcompany assignments -> require explicit company selection
            throw new InvalidOperationException("Caller has multiple company assignments: specify subCompanyId query parameter.");
        }

        // -------------------------
        // POST: api/users?subCompanyId={optional}
        // -------------------------
        [HttpPost]
        [Authorize]
        public async Task<IActionResult> CreateUser([FromBody] CreateUserDto dto, [FromQuery] Guid? subCompanyId)
        {
            var baseCompanyId = BaseCompanyIdFromClaims();
            var callerUserId = CallerUserIdFromClaims();

            Guid targetCompanyId;
            try
            {
                targetCompanyId = await ResolveTargetCompanyIdAsync(baseCompanyId, callerUserId, subCompanyId);
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }

            try
            {
                var (user, tempPassword) = await _manager.CreateUserAsync(targetCompanyId, dto);
                if (user == null) return Conflict(new { message = "User with this email already exists." });

                // Optionally return tempPassword in dev mode; production you may omit it.
                return CreatedAtAction(nameof(GetUsers), new { id = user.UserId }, new { userId = user.UserId });
            }
            catch (InvalidOperationException ex)
            {
                // e.g., trying to create superUser in subcompany
                return Conflict(new { message = ex.Message });
            }
        }

        // -------------------------
        // GET: api/users?subCompanyId={optional}
        // -------------------------
        [HttpGet]
        [Authorize]
        public async Task<IActionResult> GetUsers([FromQuery] Guid? subCompanyId)
        {
            var baseCompanyId = BaseCompanyIdFromClaims();
            var callerUserId = CallerUserIdFromClaims();

            Guid targetCompanyId;
            try
            {
                targetCompanyId = await ResolveTargetCompanyIdAsync(baseCompanyId, callerUserId, subCompanyId);
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }

            var list = await _manager.ListUsersAsync(targetCompanyId);
            return Ok(list);
        }

        // PUT: api/users (the DTO contains UserId)
        [HttpPut]
        [Authorize]
        public async Task<IActionResult> UpdateUser([FromBody] UpdateUserDto dto, [FromQuery] Guid? subCompanyId)
        {
            // delegate to shared implementation
            return await UpdateUserInternal(dto, subCompanyId);
        }

        // PUT: api/users/{id}
        [HttpPut("{id:int}")]
        [Authorize]
        public async Task<IActionResult> UpdateUserById(int id, [FromBody] UpdateUserDto dto, [FromQuery] Guid? subCompanyId)
        {
            dto.UserId = id;
            return await UpdateUserInternal(dto, subCompanyId);
        }

        private async Task<IActionResult> UpdateUserInternal(UpdateUserDto dto, Guid? subCompanyId)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var baseCompanyId = BaseCompanyIdFromClaims();
            var callerUserId = CallerUserIdFromClaims();

            Guid targetCompanyId;
            try
            {
                targetCompanyId = await ResolveTargetCompanyIdAsync(baseCompanyId, callerUserId, subCompanyId);
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }

            // Important: ensure the user being updated actually belongs to the target company
            var user = await _db.Users.FirstOrDefaultAsync(u => u.UserId == dto.UserId);
            if (user == null) return NotFound();

            if (user.CompanyId != targetCompanyId)
            {
                // caller is trying to update a user in a different company than target => forbid
                return Forbid();
            }

            try
            {
                var updated = await _manager.UpdateUserAsync(targetCompanyId, dto);
                if (updated == null) return NotFound();
                return Ok(updated);
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { message = ex.Message });
            }
        }

        // PATCH: api/users/{id}/inactivate
        [HttpPatch("{id:int}/inactivate")]
        [Authorize]
        public async Task<IActionResult> Inactivate(int id, [FromQuery] Guid? subCompanyId)
        {
            var baseCompanyId = BaseCompanyIdFromClaims();
            var callerUserId = CallerUserIdFromClaims();

            Guid targetCompanyId;
            try
            {
                targetCompanyId = await ResolveTargetCompanyIdAsync(baseCompanyId, callerUserId, subCompanyId);
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }

            // ensure target user is in the targetCompany
            var user = await _db.Users.FirstOrDefaultAsync(u => u.UserId == id);
            if (user == null) return NotFound();
            if (user.CompanyId != targetCompanyId) return Forbid();

            var ok = await _manager.InactivateUserAsync(targetCompanyId, id);
            if (!ok) return NotFound(new { Message = "User not found" });
            return Ok(new { Message = "User has been inactivated successfully." });
        }

        // PATCH: api/users/{id}/activate
        [HttpPatch("{id:int}/activate")]
        [Authorize]
        public async Task<IActionResult> Activate(int id, [FromQuery] Guid? subCompanyId)
        {
            var baseCompanyId = BaseCompanyIdFromClaims();
            var callerUserId = CallerUserIdFromClaims();

            Guid targetCompanyId;
            try
            {
                targetCompanyId = await ResolveTargetCompanyIdAsync(baseCompanyId, callerUserId, subCompanyId);
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }

            var user = await _db.Users.FirstOrDefaultAsync(u => u.UserId == id);
            if (user == null) return NotFound();
            if (user.CompanyId != targetCompanyId) return Forbid();

            var ok = await _manager.ActivateUserAsync(targetCompanyId, id);
            if (!ok) return NotFound(new { Message = "User not found" });
            return Ok(new { Message = "User has been activated successfully." });
        }
    }
}
