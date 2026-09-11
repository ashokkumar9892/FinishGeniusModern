using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinishGenius.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class FormulaWorkspace : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InventoryTransactions_GroupId",
                schema: "fg",
                table: "InventoryTransactions");

            migrationBuilder.AddColumn<string>(
                name: "ColorCode",
                schema: "fg",
                table: "Materials",
                type: "nvarchar(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BatchType",
                schema: "fg",
                table: "Formulas",
                type: "int",
                nullable: false,
                defaultValue: 2);

            migrationBuilder.AddColumn<decimal>(
                name: "BatchValue",
                schema: "fg",
                table: "Formulas",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "DispenserId",
                schema: "fg",
                table: "Formulas",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmployeeName",
                schema: "fg",
                table: "Formulas",
                type: "nvarchar(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "MixedOn",
                schema: "fg",
                table: "Formulas",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PurchaseOrderNumber",
                schema: "fg",
                table: "Formulas",
                type: "nvarchar(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BatchNumber",
                schema: "fg",
                table: "FormulaIngredients",
                type: "nvarchar(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DispenseAmount",
                schema: "fg",
                table: "FormulaIngredients",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "NewValue",
                schema: "fg",
                table: "AuditLogs",
                type: "nvarchar(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OldValue",
                schema: "fg",
                table: "AuditLogs",
                type: "nvarchar(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DeviceCommands",
                schema: "fg",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DeviceId = table.Column<int>(type: "int", nullable: false),
                    BridgeDeviceId = table.Column<int>(type: "int", nullable: false),
                    CommandType = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Payload = table.Column<string>(type: "nvarchar(max)", maxLength: 2147483647, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    ResultMessage = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    ResultData = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    FormulaId = table.Column<int>(type: "int", nullable: true),
                    CreatedBy = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SentAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GroupId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeviceCommands", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeviceCommands_Groups_GroupId",
                        column: x => x.GroupId,
                        principalSchema: "fg",
                        principalTable: "Groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DispenseSettings",
                schema: "fg",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    GroupId = table.Column<int>(type: "int", nullable: false),
                    CleanNozzleHours = table.Column<int>(type: "int", nullable: true),
                    IsNozzleCleaned = table.Column<bool>(type: "bit", nullable: false),
                    LastDispensedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DispenseSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FormulaDevicePreferences",
                schema: "fg",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    FormulaId = table.Column<int>(type: "int", nullable: false),
                    ScaleDeviceId = table.Column<int>(type: "int", nullable: true),
                    PrinterDeviceId = table.Column<int>(type: "int", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormulaDevicePreferences", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FormulaDispenseSnapshots",
                schema: "fg",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FormulaId = table.Column<int>(type: "int", nullable: false),
                    IngredientId = table.Column<int>(type: "int", nullable: false),
                    Grams = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    DispenseAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    DispensedGrams = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    IsDispensed = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormulaDispenseSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PurgeFailures",
                schema: "fg",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BridgeDeviceId = table.Column<int>(type: "int", nullable: false),
                    CanisterNumber = table.Column<int>(type: "int", nullable: false),
                    Message = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurgeFailures", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PurgeSettings",
                schema: "fg",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BridgeDeviceId = table.Column<int>(type: "int", nullable: false),
                    FromDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ToDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Time = table.Column<TimeOnly>(type: "time", nullable: false),
                    CreatedBy = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedBy = table.Column<int>(type: "int", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurgeSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PurgeSuccesses",
                schema: "fg",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BridgeDeviceId = table.Column<int>(type: "int", nullable: false),
                    CanisterNumber = table.Column<int>(type: "int", nullable: false),
                    Message = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    PurgeType = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    ExecutedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurgeSuccesses", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransactions_GroupId_BatchNumber",
                schema: "fg",
                table: "InventoryTransactions",
                columns: new[] { "GroupId", "BatchNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_DeviceCommands_BridgeDeviceId_Status",
                schema: "fg",
                table: "DeviceCommands",
                columns: new[] { "BridgeDeviceId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_DeviceCommands_FormulaId_CommandType",
                schema: "fg",
                table: "DeviceCommands",
                columns: new[] { "FormulaId", "CommandType" });

            migrationBuilder.CreateIndex(
                name: "IX_DeviceCommands_GroupId",
                schema: "fg",
                table: "DeviceCommands",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_DispenseSettings_GroupId",
                schema: "fg",
                table: "DispenseSettings",
                column: "GroupId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FormulaDevicePreferences_UserId_FormulaId",
                schema: "fg",
                table: "FormulaDevicePreferences",
                columns: new[] { "UserId", "FormulaId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FormulaDispenseSnapshots_FormulaId",
                schema: "fg",
                table: "FormulaDispenseSnapshots",
                column: "FormulaId");

            migrationBuilder.CreateIndex(
                name: "IX_PurgeFailures_BridgeDeviceId_IsActive",
                schema: "fg",
                table: "PurgeFailures",
                columns: new[] { "BridgeDeviceId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_PurgeSettings_BridgeDeviceId",
                schema: "fg",
                table: "PurgeSettings",
                column: "BridgeDeviceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurgeSuccesses_BridgeDeviceId_ExecutedAt",
                schema: "fg",
                table: "PurgeSuccesses",
                columns: new[] { "BridgeDeviceId", "ExecutedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeviceCommands",
                schema: "fg");

            migrationBuilder.DropTable(
                name: "DispenseSettings",
                schema: "fg");

            migrationBuilder.DropTable(
                name: "FormulaDevicePreferences",
                schema: "fg");

            migrationBuilder.DropTable(
                name: "FormulaDispenseSnapshots",
                schema: "fg");

            migrationBuilder.DropTable(
                name: "PurgeFailures",
                schema: "fg");

            migrationBuilder.DropTable(
                name: "PurgeSettings",
                schema: "fg");

            migrationBuilder.DropTable(
                name: "PurgeSuccesses",
                schema: "fg");

            migrationBuilder.DropIndex(
                name: "IX_InventoryTransactions_GroupId_BatchNumber",
                schema: "fg",
                table: "InventoryTransactions");

            migrationBuilder.DropColumn(
                name: "ColorCode",
                schema: "fg",
                table: "Materials");

            migrationBuilder.DropColumn(
                name: "BatchType",
                schema: "fg",
                table: "Formulas");

            migrationBuilder.DropColumn(
                name: "BatchValue",
                schema: "fg",
                table: "Formulas");

            migrationBuilder.DropColumn(
                name: "DispenserId",
                schema: "fg",
                table: "Formulas");

            migrationBuilder.DropColumn(
                name: "EmployeeName",
                schema: "fg",
                table: "Formulas");

            migrationBuilder.DropColumn(
                name: "MixedOn",
                schema: "fg",
                table: "Formulas");

            migrationBuilder.DropColumn(
                name: "PurchaseOrderNumber",
                schema: "fg",
                table: "Formulas");

            migrationBuilder.DropColumn(
                name: "BatchNumber",
                schema: "fg",
                table: "FormulaIngredients");

            migrationBuilder.DropColumn(
                name: "DispenseAmount",
                schema: "fg",
                table: "FormulaIngredients");

            migrationBuilder.DropColumn(
                name: "NewValue",
                schema: "fg",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "OldValue",
                schema: "fg",
                table: "AuditLogs");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransactions_GroupId",
                schema: "fg",
                table: "InventoryTransactions",
                column: "GroupId");
        }
    }
}
