using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VeiraMal.API.Migrations
{
    /// <inheritdoc />
    public partial class updatedCompanySubscriptionForStripe : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsPaid",
                schema: "dbo",
                table: "CompanySubscriptions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "TempPasswordProtected",
                schema: "dbo",
                table: "CompanySubscriptions",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsPaid",
                schema: "dbo",
                table: "CompanySubscriptions");

            migrationBuilder.DropColumn(
                name: "TempPasswordProtected",
                schema: "dbo",
                table: "CompanySubscriptions");
        }
    }
}
