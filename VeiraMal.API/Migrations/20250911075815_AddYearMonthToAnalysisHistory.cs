using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VeiraMal.API.Migrations
{
    /// <inheritdoc />
    public partial class AddYearMonthToAnalysisHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "AnalysisDate",
                table: "AnalysisHistory",
                newName: "CreatedAt");

            migrationBuilder.AddColumn<int>(
                name: "Month",
                table: "AnalysisHistory",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Year",
                table: "AnalysisHistory",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Month",
                table: "AnalysisHistory");

            migrationBuilder.DropColumn(
                name: "Year",
                table: "AnalysisHistory");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                table: "AnalysisHistory",
                newName: "AnalysisDate");
        }
    }
}
