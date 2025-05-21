using Microsoft.EntityFrameworkCore;
using VeiraMal.API.Models; // ✅ Add this

namespace VeiraMal.API
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options)
            : base(options)
        {
        }

        // Define your tables (DbSets) here
        // Example:
        public DbSet<SuperAdmin> SuperAdmins { get; set; }
    }
}
