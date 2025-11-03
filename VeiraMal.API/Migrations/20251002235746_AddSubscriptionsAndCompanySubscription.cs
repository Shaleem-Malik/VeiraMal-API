using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace VeiraMal.API.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriptionsAndCompanySubscription : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SubscriptionPlans",
                schema: "dbo",
                columns: table => new
                {
                    SubscriptionPlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Package = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PricePerMonth = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    IdealFor = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    KeyFeatures = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    BaseUserSeats = table.Column<int>(type: "int", nullable: false),
                    MaxHC = table.Column<int>(type: "int", nullable: true),
                    SuperUsers = table.Column<int>(type: "int", nullable: false),
                    AdditionalSeatsAllowed = table.Column<bool>(type: "bit", nullable: false),
                    AdditionalSeatPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    HasApi = table.Column<bool>(type: "bit", nullable: false),
                    ReportingLevel = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionPlans", x => x.SubscriptionPlanId);
                });

            migrationBuilder.CreateTable(
                name: "CompanySubscriptions",
                schema: "dbo",
                columns: table => new
                {
                    CompanySubscriptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubscriptionPlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlanNameSnapshot = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    BaseUserSeatsSnapshot = table.Column<int>(type: "int", nullable: false),
                    AdditionalSeatsPurchased = table.Column<int>(type: "int", nullable: false),
                    AdditionalSeatPriceSnapshot = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    MonthlyPriceSnapshot = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    StartDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanySubscriptions", x => x.CompanySubscriptionId);
                    table.ForeignKey(
                        name: "FK_CompanySubscriptions_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "dbo",
                        principalTable: "Companies",
                        principalColumn: "CompanyId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CompanySubscriptions_SubscriptionPlans_SubscriptionPlanId",
                        column: x => x.SubscriptionPlanId,
                        principalSchema: "dbo",
                        principalTable: "SubscriptionPlans",
                        principalColumn: "SubscriptionPlanId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                schema: "dbo",
                table: "SubscriptionPlans",
                columns: new[] { "SubscriptionPlanId", "AdditionalSeatPrice", "AdditionalSeatsAllowed", "BaseUserSeats", "HasApi", "IdealFor", "KeyFeatures", "MaxHC", "Package", "PricePerMonth", "ReportingLevel", "SuperUsers" },
                values: new object[,]
                {
                    { new Guid("11111111-1111-1111-1111-111111111111"), 0m, false, 1, false, "Overview", "Can explore by seeing dummy reports etc", 50, "Demo", 0m, "Overview", 0 },
                    { new Guid("22222222-2222-2222-2222-222222222222"), 100m, true, 10, false, "Startups or small teams that want to self-serve", "Basic Reports; Automated onboarding; Report upload", 200, "Starter", 1000m, "Basic", 1 },
                    { new Guid("33333333-3333-3333-3333-333333333333"), 90m, true, 20, false, "Growing SMEs needing more capacity", "Everything in Starter; Advanced reporting; Dedicated account manager; Optional addons", 400, "Business", 2500m, "Advanced", 2 },
                    { new Guid("44444444-4444-4444-4444-444444444444"), 0m, false, 0, false, "Large organisations with complex requirements", "4 Superusers; Premium support (Non-Tech); Custom contract and SLA; Dedicated account manager; Custom onboarding support; Unlimited seats; Unlimited HC", null, "Enterprise", 3500m, "Advanced", 4 },
                    { new Guid("55555555-5555-5555-5555-555555555555"), 0m, false, 0, true, "Large organisations with complex requirements", "6 Superusers; Premium support; Custom contract and SLA; Dedicated account manager; Premium Reports; Custom onboarding support; Unlimited seats; APIs", null, "Enterprise Plus", null, "Premium", 6 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_CompanySubscriptions_CompanyId",
                schema: "dbo",
                table: "CompanySubscriptions",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_CompanySubscriptions_SubscriptionPlanId",
                schema: "dbo",
                table: "CompanySubscriptions",
                column: "SubscriptionPlanId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CompanySubscriptions",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "SubscriptionPlans",
                schema: "dbo");
        }
    }
}
