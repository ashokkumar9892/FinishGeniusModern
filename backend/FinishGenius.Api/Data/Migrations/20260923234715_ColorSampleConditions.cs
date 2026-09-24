using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinishGenius.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ColorSampleConditions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ColorantsJson",
                schema: "fg",
                table: "ColorSamples",
                type: "nvarchar(max)",
                maxLength: 2147483647,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DryingConditions",
                schema: "fg",
                table: "ColorSamples",
                type: "nvarchar(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExistingFinish",
                schema: "fg",
                table: "ColorSamples",
                type: "nvarchar(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GrainDirection",
                schema: "fg",
                table: "ColorSamples",
                type: "nvarchar(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GrowthRings",
                schema: "fg",
                table: "ColorSamples",
                type: "nvarchar(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "MoisturePercent",
                schema: "fg",
                table: "ColorSamples",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Porosity",
                schema: "fg",
                table: "ColorSamples",
                type: "nvarchar(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SprayGun",
                schema: "fg",
                table: "ColorSamples",
                type: "nvarchar(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "SprayPressurePsi",
                schema: "fg",
                table: "ColorSamples",
                type: "float",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ColorantsJson",
                schema: "fg",
                table: "ColorSamples");

            migrationBuilder.DropColumn(
                name: "DryingConditions",
                schema: "fg",
                table: "ColorSamples");

            migrationBuilder.DropColumn(
                name: "ExistingFinish",
                schema: "fg",
                table: "ColorSamples");

            migrationBuilder.DropColumn(
                name: "GrainDirection",
                schema: "fg",
                table: "ColorSamples");

            migrationBuilder.DropColumn(
                name: "GrowthRings",
                schema: "fg",
                table: "ColorSamples");

            migrationBuilder.DropColumn(
                name: "MoisturePercent",
                schema: "fg",
                table: "ColorSamples");

            migrationBuilder.DropColumn(
                name: "Porosity",
                schema: "fg",
                table: "ColorSamples");

            migrationBuilder.DropColumn(
                name: "SprayGun",
                schema: "fg",
                table: "ColorSamples");

            migrationBuilder.DropColumn(
                name: "SprayPressurePsi",
                schema: "fg",
                table: "ColorSamples");
        }
    }
}
