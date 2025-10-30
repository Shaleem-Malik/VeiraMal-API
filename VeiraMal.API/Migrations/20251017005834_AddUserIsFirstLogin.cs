using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VeiraMal.API.Migrations
{
    /// <inheritdoc />
    public partial class AddUserIsFirstLogin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsFirstLogin",
                schema: "dbo",
                table: "Users",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsFirstLogin",
                schema: "dbo",
                table: "Users");
        }
    }
}
