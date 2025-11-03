using OfficeOpenXml;
using Microsoft.AspNetCore.Mvc;
using VeiraMal.API.Models;
using VeiraMal.API;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization; // ✅ ADD THIS

namespace VeiraMal.API.Controllers
{
    //[Authorize] // ⬅️ Add this above your controller or specific actions
    [ApiController]
    [Route("api/[controller]")]
    public class SuperAdminController : ControllerBase
    {
        private readonly AppDbContext _context;

        public SuperAdminController(AppDbContext context)
        {
            _context = context;
        }

[HttpPost]
public async Task<IActionResult> CreateSuperAdmin([FromBody] SuperAdmin admin)
{
    if (!ModelState.IsValid)
        return BadRequest(ModelState);

    // 🔐 Secure password hashing
    admin.PasswordHash = BCrypt.Net.BCrypt.HashPassword(admin.PasswordHash);
    admin.CreatedAt = DateTime.UtcNow;

    _context.SuperAdmins.Add(admin);
    await _context.SaveChangesAsync();

    return Ok(new { message = "Super Admin created successfully", admin });
}

    }
}
