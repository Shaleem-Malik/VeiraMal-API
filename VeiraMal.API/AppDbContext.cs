using Microsoft.EntityFrameworkCore;
using VeiraMal.API.Models;
using System;

namespace VeiraMal.API
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options)
            : base(options)
        {
        }

        // existing DbSets...
        public DbSet<SuperAdmin> SuperAdmins { get; set; }
        public DbSet<Employee> Employees { get; set; }

        //tables for employee data
        public DbSet<Headcount> Headcounts { get; set; }
        public DbSet<NHT> NHTs { get; set; }
        public DbSet<Terms> Terms { get; set; }

        public DbSet<AnalysisHistory> AnalysisHistory { get; set; }

        // company + user
        public DbSet<Company> Companies { get; set; }
        public DbSet<User> Users { get; set; }


        // subscription tables
        public DbSet<SubscriptionPlan> SubscriptionPlans { get; set; }
        public DbSet<CompanySubscription> CompanySubscriptions { get; set; }

        public DbSet<BusinessUnit> BusinessUnits { get; set; }
        public DbSet<AccessLevel> AccessLevels { get; set; }

        public DbSet<CompanySuperUserAssignment> CompanySuperUserAssignments { get; set; }

        public DbSet<RevokedToken> RevokedTokens { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Existing table mappings
            modelBuilder.Entity<Headcount>().ToTable("Headcounts");
            modelBuilder.Entity<NHT>().ToTable("NHTs");
            modelBuilder.Entity<Terms>().ToTable("Terms");

            // Company table config
            modelBuilder.Entity<Company>().ToTable("Companies", "dbo");
            modelBuilder.Entity<User>().ToTable("Users", "dbo");

            // Subscription tables mapping
            modelBuilder.Entity<SubscriptionPlan>().ToTable("SubscriptionPlans", "dbo");
            modelBuilder.Entity<CompanySubscription>().ToTable("CompanySubscriptions", "dbo");

            modelBuilder.Entity<BusinessUnit>().ToTable("BusinessUnits", "dbo");
            modelBuilder.Entity<AccessLevel>().ToTable("AccessLevels", "dbo");

            modelBuilder.Entity<Company>().ToTable("Companies", "dbo");

            // new one-to-many for parent-child companies
            modelBuilder.Entity<Company>()
                .HasMany(c => c.ChildCompanies)
                .WithOne(c => c.ParentCompany)
                .HasForeignKey(c => c.ParentCompanyId)
                .OnDelete(DeleteBehavior.Restrict);

            // CompanySuperUserAssignment mapping with composite PK
            modelBuilder.Entity<CompanySuperUserAssignment>()
                .ToTable("CompanySuperUserAssignments", "dbo");

            modelBuilder.Entity<CompanySuperUserAssignment>()
                .HasKey(a => new { a.CompanyId, a.UserId });

            modelBuilder.Entity<CompanySuperUserAssignment>()
                .HasOne(a => a.Company)
                .WithMany(c => c.AssignedSuperUsers)
                .HasForeignKey(a => a.CompanyId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<CompanySuperUserAssignment>()
                .HasOne(a => a.User)
                .WithMany()
                .HasForeignKey(a => a.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // Seed subscription plans (GUIDs are fixed so migrations stay stable)
            var demoId = Guid.Parse("11111111-1111-1111-1111-111111111111");
            var starterId = Guid.Parse("22222222-2222-2222-2222-222222222222");
            var businessId = Guid.Parse("33333333-3333-3333-3333-333333333333");
            var enterpriseId = Guid.Parse("44444444-4444-4444-4444-444444444444");
            var enterprisePlusId = Guid.Parse("55555555-5555-5555-5555-555555555555");

            modelBuilder.Entity<SubscriptionPlan>().HasData(
                new SubscriptionPlan
                {
                    SubscriptionPlanId = demoId,
                    Package = "Demo",
                    PricePerMonth = 0m,
                    IdealFor = "Overview",
                    KeyFeatures = "Can explore by seeing dummy reports etc",
                    BaseUserSeats = 1,
                    MaxHC = 50,
                    SuperUsers = 0,
                    AdditionalSeatsAllowed = false,
                    AdditionalSeatPrice = 0m,
                    ReportingLevel = "Overview",
                    HasApi = false
                },
                new SubscriptionPlan
                {
                    SubscriptionPlanId = starterId,
                    Package = "Starter",
                    PricePerMonth = 1000m,
                    IdealFor = "Startups or small teams that want to self-serve",
                    KeyFeatures = "Basic Reports; Automated onboarding; Report upload",
                    BaseUserSeats = 10,
                    MaxHC = 200,
                    SuperUsers = 1,
                    AdditionalSeatsAllowed = true,
                    AdditionalSeatPrice = 100m,
                    ReportingLevel = "Basic",
                    HasApi = false
                },
                new SubscriptionPlan
                {
                    SubscriptionPlanId = businessId,
                    Package = "Business",
                    PricePerMonth = 2500m,
                    IdealFor = "Growing SMEs needing more capacity",
                    KeyFeatures = "Everything in Starter; Advanced reporting; Dedicated account manager; Optional addons",
                    BaseUserSeats = 20,
                    MaxHC = 400,
                    SuperUsers = 2,
                    AdditionalSeatsAllowed = true,
                    AdditionalSeatPrice = 90m,
                    ReportingLevel = "Advanced",
                    HasApi = false
                },
                new SubscriptionPlan
                {
                    SubscriptionPlanId = enterpriseId,
                    Package = "Enterprise",
                    PricePerMonth = 3500m,
                    IdealFor = "Large organisations with complex requirements",
                    KeyFeatures = "4 Superusers; Premium support (Non-Tech); Custom contract and SLA; Dedicated account manager; Custom onboarding support; Unlimited seats; Unlimited HC",
                    BaseUserSeats = 0,
                    MaxHC = null,
                    SuperUsers = 4,
                    AdditionalSeatsAllowed = false,
                    AdditionalSeatPrice = 0m,
                    ReportingLevel = "Advanced",
                    HasApi = false
                },
                new SubscriptionPlan
                {
                    SubscriptionPlanId = enterprisePlusId,
                    Package = "Enterprise Plus",
                    PricePerMonth = null, // custom pricing - route to sales
                    IdealFor = "Large organisations with complex requirements",
                    KeyFeatures = "6 Superusers; Premium support; Custom contract and SLA; Dedicated account manager; Premium Reports; Custom onboarding support; Unlimited seats; APIs",
                    BaseUserSeats = 0,
                    MaxHC = null,
                    SuperUsers = 6,
                    AdditionalSeatsAllowed = false,
                    AdditionalSeatPrice = 0m,
                    ReportingLevel = "Premium",
                    HasApi = true
                }
            );
        }
    }
}
