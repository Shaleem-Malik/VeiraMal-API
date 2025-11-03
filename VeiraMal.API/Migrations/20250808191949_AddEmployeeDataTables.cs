using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VeiraMal.API.Migrations
{
    /// <inheritdoc />
    public partial class AddEmployeeDataTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Headcounts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PersonnelNumber = table.Column<int>(type: "int", nullable: false),
                    LastName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FirstName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AgeOfEmployee = table.Column<int>(type: "int", nullable: false),
                    GenderKey = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Country = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PersonnelSubarea = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Lv = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PersonnelArea = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    EmployeeSubgroup = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NameOfSuperior = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    OrganizationalKey = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OrganizationalUnit = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    EmployeeGroup = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    WeeklyHours = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    EmploymentPercentage = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PositionNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PositionTitle = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CostCentreNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CostCentreDescription = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SalariedOrWaged = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Location = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Tenure = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Month = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    BusinessUnit = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Headcounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NHTs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PersonnelNumber = table.Column<int>(type: "int", nullable: false),
                    LastName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FirstName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AgeOfEmployee = table.Column<int>(type: "int", nullable: false),
                    GenderKey = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NameOfSuperior = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OrganizationalKey = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OrganizationalUnit = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PersonnelArea = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PersonnelSubarea = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    EmployeeGroup = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    EmployeeSubgroup = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Lv = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EmploymentPercentage = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PositionNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PositionTitle = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ActionType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CostCentreNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CostCentreDescription = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SalariedOrWaged = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Location = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    BusinessUnit = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Month = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NHTs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Terms",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PersonnelNumber = table.Column<int>(type: "int", nullable: false),
                    LastName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FirstName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AgeOfEmployee = table.Column<int>(type: "int", nullable: false),
                    GenderKey = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OrganizationalKey = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OrganizationalUnit = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PersonnelArea = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PersonnelSubarea = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    EmployeeGroup = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    EmployeeSubgroup = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Lv = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EmploymentPercentage = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ActionType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    StartDateAction = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReasonForAction = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CostCentreNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CostCentreDescription = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SalariedOrWaged = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Manager = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Action = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Location = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    BusinessUnit = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    GradeGrouping = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Month = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Terms", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Headcounts");

            migrationBuilder.DropTable(
                name: "NHTs");

            migrationBuilder.DropTable(
                name: "Terms");
        }
    }
}
