using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinishGenius.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ColorMatching : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ColorSamples",
                schema: "fg",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    WoodSpecies = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    SandingGrit = table.Column<int>(type: "int", nullable: true),
                    WoodL = table.Column<double>(type: "float", nullable: true),
                    WoodA = table.Column<double>(type: "float", nullable: true),
                    WoodB = table.Column<double>(type: "float", nullable: true),
                    FormulaId = table.Column<int>(type: "int", nullable: true),
                    FormulaName = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Concentration = table.Column<double>(type: "float", nullable: true),
                    Method = table.Column<int>(type: "int", nullable: true),
                    Coats = table.Column<int>(type: "int", nullable: true),
                    WetFilmMils = table.Column<double>(type: "float", nullable: true),
                    FlashMinutes = table.Column<int>(type: "int", nullable: true),
                    Sealer = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    Topcoat = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    Sheen = table.Column<double>(type: "float", nullable: true),
                    FinalL = table.Column<double>(type: "float", nullable: false),
                    FinalA = table.Column<double>(type: "float", nullable: false),
                    FinalB = table.Column<double>(type: "float", nullable: false),
                    Source = table.Column<int>(type: "int", nullable: false),
                    MeasuredAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PhotoFile = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    CreatedBy = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    GroupId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ColorSamples", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ColorSamples_Groups_GroupId",
                        column: x => x.GroupId,
                        principalSchema: "fg",
                        principalTable: "Groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ColorSamples_FormulaId",
                schema: "fg",
                table: "ColorSamples",
                column: "FormulaId");

            migrationBuilder.CreateIndex(
                name: "IX_ColorSamples_GroupId_WoodSpecies",
                schema: "fg",
                table: "ColorSamples",
                columns: new[] { "GroupId", "WoodSpecies" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ColorSamples",
                schema: "fg");
        }
    }
}
