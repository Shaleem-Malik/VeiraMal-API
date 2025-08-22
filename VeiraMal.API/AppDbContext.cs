using Microsoft.EntityFrameworkCore;
using VeiraMal.API.Models;

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
        public DbSet<Employee> Employees { get; set; }

        //tables for employee data
        public DbSet<Headcount> Headcounts { get; set; }
        public DbSet<NHT> NHTs { get; set; }
        public DbSet<Terms> Terms { get; set; }

        public DbSet<AnalysisHistory> AnalysisHistory { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Configure table names (optional but recommended)
            modelBuilder.Entity<Headcount>().ToTable("Headcounts");
            modelBuilder.Entity<NHT>().ToTable("NHTs");
            modelBuilder.Entity<Terms>().ToTable("Terms");

            // Configure any additional model relationships or constraints here
            // Example for your existing entities:
            // modelBuilder.Entity<SuperAdmin>().HasIndex(s => s.Email).IsUnique();

            // You can add any additional configuration for your new models here
            // Example for Headcount:
            // modelBuilder.Entity<Headcount>().Property(h => h.PersonnelNumber).IsRequired();
        }
    }
}
