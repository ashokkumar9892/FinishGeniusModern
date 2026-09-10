using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinishGenius.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ProcessIntegrity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_ScheduleStepOverrides_ProcessStepValueId",
                schema: "fg",
                table: "ScheduleStepOverrides",
                column: "ProcessStepValueId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessStepEntries_PullDownId",
                schema: "fg",
                table: "ProcessStepEntries",
                column: "PullDownId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessSchedules_GroupId_Number",
                schema: "fg",
                table: "ProcessSchedules",
                columns: new[] { "GroupId", "Number" },
                unique: true,
                filter: "[IsArchived] = 0");

            migrationBuilder.AddForeignKey(
                name: "FK_ProcessStepEntries_SubStepPullDowns_PullDownId",
                schema: "fg",
                table: "ProcessStepEntries",
                column: "PullDownId",
                principalSchema: "fg",
                principalTable: "SubStepPullDowns",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_ScheduleStepOverrides_ProcessStepValues_ProcessStepValueId",
                schema: "fg",
                table: "ScheduleStepOverrides",
                column: "ProcessStepValueId",
                principalSchema: "fg",
                principalTable: "ProcessStepValues",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ProcessStepEntries_SubStepPullDowns_PullDownId",
                schema: "fg",
                table: "ProcessStepEntries");

            migrationBuilder.DropForeignKey(
                name: "FK_ScheduleStepOverrides_ProcessStepValues_ProcessStepValueId",
                schema: "fg",
                table: "ScheduleStepOverrides");

            migrationBuilder.DropIndex(
                name: "IX_ScheduleStepOverrides_ProcessStepValueId",
                schema: "fg",
                table: "ScheduleStepOverrides");

            migrationBuilder.DropIndex(
                name: "IX_ProcessStepEntries_PullDownId",
                schema: "fg",
                table: "ProcessStepEntries");

            migrationBuilder.DropIndex(
                name: "IX_ProcessSchedules_GroupId_Number",
                schema: "fg",
                table: "ProcessSchedules");
        }
    }
}
