using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VeiraMal.API.Migrations
{
    /// <inheritdoc />
    public partial class AddCompanyUserRolesAndParentCompany : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SuperuserAssignments",
                schema: "dbo");

            migrationBuilder.CreateTable(
                name: "CompanyUserRoles",
                schema: "dbo",
                columns: table => new
                {
                    CompanyUserRoleId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    AccessLevel = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AssignedByUserId = table.Column<int>(type: "int", nullable: true),
                    Active = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanyUserRoles", x => x.CompanyUserRoleId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CompanyUserRoles_CompanyId",
                schema: "dbo",
                table: "CompanyUserRoles",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyUserRoles_UserId",
                schema: "dbo",
                table: "CompanyUserRoles",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CompanyUserRoles",
                schema: "dbo");

            migrationBuilder.CreateTable(
                name: "SuperuserAssignments",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SuperuserAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SuperuserAssignments_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "dbo",
                        principalTable: "Companies",
                        principalColumn: "CompanyId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SuperuserAssignments_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "dbo",
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SuperuserAssignments_CompanyId",
                schema: "dbo",
                table: "SuperuserAssignments",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_SuperuserAssignments_UserId",
                schema: "dbo",
                table: "SuperuserAssignments",
                column: "UserId");
        }
    }
}
