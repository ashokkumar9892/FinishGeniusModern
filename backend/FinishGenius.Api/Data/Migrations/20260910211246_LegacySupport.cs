using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinishGenius.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class LegacySupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProcessSchedules_GroupId_Number",
                schema: "fg",
                table: "ProcessSchedules");

            migrationBuilder.AlterColumn<int>(
                name: "GroupId",
                schema: "fg",
                table: "MaterialCategories",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessSchedules_GroupId_Number",
                schema: "fg",
                table: "ProcessSchedules",
                columns: new[] { "GroupId", "Number" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProcessSchedules_GroupId_Number",
                schema: "fg",
                table: "ProcessSchedules");

            migrationBuilder.AlterColumn<int>(
                name: "GroupId",
                schema: "fg",
                table: "MaterialCategories",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProcessSchedules_GroupId_Number",
                schema: "fg",
                table: "ProcessSchedules",
                columns: new[] { "GroupId", "Number" },
                unique: true,
                filter: "[IsArchived] = 0");
        }
    }
}
