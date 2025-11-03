using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VeiraMal.API.Migrations
{
    /// <inheritdoc />
    public partial class AddPasswordChangedAtToSuperAdmin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PasswordChanged",
                table: "SuperAdmins");

            migrationBuilder.AddColumn<DateTime>(
                name: "PasswordChangedAt",
                table: "SuperAdmins",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PasswordChangedAt",
                table: "SuperAdmins");

            migrationBuilder.AddColumn<bool>(
                name: "PasswordChanged",
                table: "SuperAdmins",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }
    }
}
