using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VeiraMal.API.Migrations
{
    /// <inheritdoc />
    public partial class AddSubcompanySupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CompanyUserRoles",
                schema: "dbo");

            migrationBuilder.CreateTable(
                name: "CompanySuperUserAssignments",
                schema: "dbo",
                columns: table => new
                {
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    AssignedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanySuperUserAssignments", x => new { x.CompanyId, x.UserId });
                    table.ForeignKey(
                        name: "FK_CompanySuperUserAssignments_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalSchema: "dbo",
                        principalTable: "Companies",
                        principalColumn: "CompanyId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CompanySuperUserAssignments_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "dbo",
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Companies_ParentCompanyId",
                schema: "dbo",
                table: "Companies",
                column: "ParentCompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_CompanySuperUserAssignments_UserId",
                schema: "dbo",
                table: "CompanySuperUserAssignments",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Companies_Companies_ParentCompanyId",
                schema: "dbo",
                table: "Companies",
                column: "ParentCompanyId",
                principalSchema: "dbo",
                principalTable: "Companies",
                principalColumn: "CompanyId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Companies_Companies_ParentCompanyId",
                schema: "dbo",
                table: "Companies");

            migrationBuilder.DropTable(
                name: "CompanySuperUserAssignments",
                schema: "dbo");

            migrationBuilder.DropIndex(
                name: "IX_Companies_ParentCompanyId",
                schema: "dbo",
                table: "Companies");

            migrationBuilder.CreateTable(
                name: "CompanyUserRoles",
                schema: "dbo",
                columns: table => new
                {
                    CompanyUserRoleId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AccessLevel = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Active = table.Column<bool>(type: "bit", nullable: false),
                    AssignedByUserId = table.Column<int>(type: "int", nullable: true),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false)
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
    }
}
