using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VeiraMal.API.Migrations
{
    /// <inheritdoc />
    public partial class AddSecureHrAnalytics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "UploadBatches",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "LeaveTakens",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "EmployeeId",
                table: "Employees",
                type: "nvarchar(450)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "Employees",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CostCentre",
                table: "Employees",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "Employees",
                type: "nvarchar(320)",
                maxLength: 320,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmployeeName",
                table: "Employees",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmployeeStatus",
                table: "Employees",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmployeeType",
                table: "Employees",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Ethnicity",
                table: "Employees",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ExitDate",
                table: "Employees",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TerminationReason",
                table: "Employees",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WorkArrangement",
                table: "Employees",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "HrEngagementScores",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Score = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    SurveyDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HrEngagementScores", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HrOpenRoles",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Department = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RoleTitle = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    OpenRoles = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    AsOfDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HrOpenRoles", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UploadBatches_CompanyId_CreatedAt",
                table: "UploadBatches",
                columns: new[] { "CompanyId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LeaveTakens_CompanyId_PersonnelNumber_StartDate",
                table: "LeaveTakens",
                columns: new[] { "CompanyId", "PersonnelNumber", "StartDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Employees_CompanyId_EmployeeId",
                table: "Employees",
                columns: new[] { "CompanyId", "EmployeeId" },
                unique: true,
                filter: "[CompanyId] IS NOT NULL AND [EmployeeId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_HrEngagementScores_CompanyId_EmployeeId_SurveyDate",
                table: "HrEngagementScores",
                columns: new[] { "CompanyId", "EmployeeId", "SurveyDate" });

            migrationBuilder.CreateIndex(
                name: "IX_HrOpenRoles_CompanyId_Department_AsOfDate",
                table: "HrOpenRoles",
                columns: new[] { "CompanyId", "Department", "AsOfDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HrEngagementScores");

            migrationBuilder.DropTable(
                name: "HrOpenRoles");

            migrationBuilder.DropIndex(
                name: "IX_UploadBatches_CompanyId_CreatedAt",
                table: "UploadBatches");

            migrationBuilder.DropIndex(
                name: "IX_LeaveTakens_CompanyId_PersonnelNumber_StartDate",
                table: "LeaveTakens");

            migrationBuilder.DropIndex(
                name: "IX_Employees_CompanyId_EmployeeId",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "UploadBatches");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "LeaveTakens");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "CostCentre",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "Email",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "EmployeeName",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "EmployeeStatus",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "EmployeeType",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "Ethnicity",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "ExitDate",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "TerminationReason",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "WorkArrangement",
                table: "Employees");

            migrationBuilder.AlterColumn<string>(
                name: "EmployeeId",
                table: "Employees",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldNullable: true);
        }
    }
}
