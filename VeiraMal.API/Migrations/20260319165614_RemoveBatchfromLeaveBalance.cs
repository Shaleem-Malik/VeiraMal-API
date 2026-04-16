using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VeiraMal.API.Migrations
{
    /// <inheritdoc />
    public partial class RemoveBatchfromLeaveBalance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LeaveBalances_UploadBatches_UploadBatchId",
                table: "LeaveBalances");

            migrationBuilder.DropIndex(
                name: "IX_LeaveBalances_UploadBatchId",
                table: "LeaveBalances");

            migrationBuilder.DropColumn(
                name: "UploadBatchId",
                table: "LeaveBalances");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "UploadBatchId",
                table: "LeaveBalances",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_LeaveBalances_UploadBatchId",
                table: "LeaveBalances",
                column: "UploadBatchId");

            migrationBuilder.AddForeignKey(
                name: "FK_LeaveBalances_UploadBatches_UploadBatchId",
                table: "LeaveBalances",
                column: "UploadBatchId",
                principalTable: "UploadBatches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
