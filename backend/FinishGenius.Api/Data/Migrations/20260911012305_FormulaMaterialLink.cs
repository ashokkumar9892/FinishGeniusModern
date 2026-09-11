using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinishGenius.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class FormulaMaterialLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MaterialId",
                schema: "fg",
                table: "Formulas",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Formulas_MaterialId",
                schema: "fg",
                table: "Formulas",
                column: "MaterialId");

            migrationBuilder.AddForeignKey(
                name: "FK_Formulas_Materials_MaterialId",
                schema: "fg",
                table: "Formulas",
                column: "MaterialId",
                principalSchema: "fg",
                principalTable: "Materials",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // Additive backfill 1: imported formulations are their own mirror material (legacy: same row, so same id, type Formula = 6).
            migrationBuilder.Sql("""
                UPDATE f SET f.MaterialId = m.Id
                FROM fg.Formulas f JOIN fg.Materials m ON m.Id = f.Id AND m.MaterialType = 6
                WHERE f.MaterialId IS NULL;
                """);

            // Additive backfill 2: formulas created in the new app before this migration get a mirror material now.
            migrationBuilder.Sql("""
                DECLARE @map TABLE (FormulaId int NOT NULL, MaterialId int NOT NULL);
                MERGE fg.Materials AS t
                USING (SELECT Id, GroupId, CategoryId, Name, Number FROM fg.Formulas WHERE MaterialId IS NULL AND IsDeleted = 0) AS s
                ON 1 = 0
                WHEN NOT MATCHED THEN
                    INSERT (GroupId, MaterialType, CategoryId, ProductCode, ProductName, Density, Price, Voc, Hap, Tap, MinQuantity, IsDeleted, CreatedAt)
                    VALUES (s.GroupId, 6, s.CategoryId, s.Number, s.Name, 0, 0, 0, 0, 0, 0, 0, SYSUTCDATETIME())
                OUTPUT s.Id, inserted.Id INTO @map (FormulaId, MaterialId);
                UPDATE f SET f.MaterialId = m.MaterialId FROM fg.Formulas f JOIN @map m ON m.FormulaId = f.Id;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Formulas_Materials_MaterialId",
                schema: "fg",
                table: "Formulas");

            migrationBuilder.DropIndex(
                name: "IX_Formulas_MaterialId",
                schema: "fg",
                table: "Formulas");

            migrationBuilder.DropColumn(
                name: "MaterialId",
                schema: "fg",
                table: "Formulas");
        }
    }
}
