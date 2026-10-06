using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VeiraMal.API.Migrations
{
    /// <inheritdoc />
    public partial class AddHrAnalyticsSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HrAnalyticsSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TurnoverHealthyThreshold = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    TurnoverWatchThreshold = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    AbsenceHealthyThreshold = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    AbsenceWatchThreshold = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    WorkingDaysPerYear = table.Column<int>(type: "int", nullable: false),
                    RollingAverageWindowMonths = table.Column<int>(type: "int", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    RequisitionAgeDays = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HrAnalyticsSettings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HrAnalyticsSettings_CompanyId",
                table: "HrAnalyticsSettings",
                column: "CompanyId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HrAnalyticsSettings");
        }
    }
}
