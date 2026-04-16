using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VeiraMal.API.Migrations
{
    /// <inheritdoc />
    public partial class RemoveBatchfromLeaveTaken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LeaveTakens_UploadBatches_UploadBatchId",
                table: "LeaveTakens");

            migrationBuilder.DropIndex(
                name: "IX_LeaveTakens_UploadBatchId",
                table: "LeaveTakens");

            migrationBuilder.DropColumn(
                name: "UploadBatchId",
                table: "LeaveTakens");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "UploadBatchId",
                table: "LeaveTakens",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_LeaveTakens_UploadBatchId",
                table: "LeaveTakens",
                column: "UploadBatchId");

            migrationBuilder.AddForeignKey(
                name: "FK_LeaveTakens_UploadBatches_UploadBatchId",
                table: "LeaveTakens",
                column: "UploadBatchId",
                principalTable: "UploadBatches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
