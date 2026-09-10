using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinishGenius.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "fg");

            migrationBuilder.CreateTable(
                name: "AppSettings",
                schema: "fg",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Key = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Value = table.Column<string>(type: "nvarchar(max)", maxLength: 2147483647, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AuditLogs",
                schema: "fg",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    GroupId = table.Column<int>(type: "int", nullable: true),
                    UserId = table.Column<int>(type: "int", nullable: true),
                    UserName = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    EntityType = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    EntityId = table.Column<int>(type: "int", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Details = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DeviceMetrics",
                schema: "fg",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DeviceId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    Timestamp = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Value = table.Column<double>(type: "float", nullable: true),
                    TextValue = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeviceMetrics", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Groups",
                schema: "fg",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Address1 = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    Address2 = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    City = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    State = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    Zip = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    Country = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    TimeZone = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    ApiKey = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LogoFile = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    ChecklistDeletionEnabled = table.Column<bool>(type: "bit", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Groups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IndustrySectors",
                schema: "fg",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IndustrySectors", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AdderTypes",
                schema: "fg",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    IsArchived = table.Column<bool>(type: "bit", nullable: false),
                    GroupId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdderTypes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AdderTypes_Groups_GroupId",
                        column: x => x.GroupId,
                        principalSchema: "fg",
                        principalTable: "Groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DefectTypes",
                schema: "fg",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    ChartColor = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    IsArchived = table.Column<bool>(type: "bit", nullable: false),
                    GroupId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DefectTypes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DefectTypes_Groups_GroupId",
                        column: x => x.GroupId,
                        principalSchema: "fg",
                        principalTable: "Groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Departments",
                schema: "fg",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    GroupId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Departments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Departments_Groups_GroupId",
                        column: x => x.GroupId,
                        principalSchema: "fg",
                        principalTable: "Groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Devices",
                schema: "fg",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    DeviceType = table.Column<int>(type: "int", nullable: false),
                    NetworkBridgeId = table.Column<int>(type: "int", nullable: true),
                    IpAddress = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    ApiKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsArchived = table.Column<bool>(type: "bit", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GroupId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Devices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Devices_Groups_GroupId",
                        column: x => x.GroupId,
                        principalSchema: "fg",
                        principalTable: "Groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Documents",
                schema: "fg",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    StoredFile = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    ContentType = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    FileSize = table.Column<long>(type: "bigint", nullable: false),
                    CreatedBy = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    GroupId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Documents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Documents_Groups_GroupId",
                        column: x => x.GroupId,
                        principalSchema: "fg",
                        principalTable: "Groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MaterialCategories",
                schema: "fg",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    MaterialType = table.Column<int>(type: "int", nullable: false),
                    Filter1 = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    Filter2 = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    GroupId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaterialCategories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MaterialCategories_Groups_GroupId",
                        column: x => x.GroupId,
                        principalSchema: "fg",
                        principalTable: "Groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MaterialLocations",
                schema: "fg",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    MaterialType = table.Column<int>(type: "int", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    GroupId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaterialLocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MaterialLocations_Groups_GroupId",
                        column: x => x.GroupId,
                        principalSchema: "fg",
                        principalTable: "Groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Photos",
                schema: "fg",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    StoredFile = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    CreatedBy = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GroupId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Photos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Photos_Groups_GroupId",
                        column: x => x.GroupId,
                        principalSchema: "fg",
                        principalTable: "Groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                schema: "fg",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    GroupId = table.Column<int>(type: "int", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Username = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    FirstName = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    LastName = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    PhoneNumber = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    Disabled = table.Column<bool>(type: "bit", nullable: false),
                    AgreementAccepted = table.Column<bool>(type: "bit", nullable: false),
                    AgreementAcceptedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DefaultGroupId = table.Column<int>(type: "int", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastLoginAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Users_Groups_GroupId",
                        column: x => x.GroupId,
                        principalSchema: "fg",
                        principalTable: "Groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Vendors",
                schema: "fg",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    VendorName = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Address = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    City = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    State = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    Zip = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    Country = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    PaymentTerms = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    AccountNumber = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    ContactName = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    OfficePhone = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    MobilePhone = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    VendorEmail = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    RequestorEmail = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    GroupId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Vendors", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Vendors_Groups_GroupId",
                        column: x => x.GroupId,
                        principalSchema: "fg",
                        principalTable: "Groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkInstructions",
                schema: "fg",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DocumentNumber = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    IssueDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    IsReleased = table.Column<bool>(type: "bit", nullable: false),
                    Controlled = table.Column<bool>(type: "bit", nullable: false),
                    Location = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Purpose = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Scope = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Terminology = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    GroupId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkInstructions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkInstructions_Groups_GroupId",
                        column: x => x.GroupId,
                        principalSchema: "fg",
                        principalTable: "Groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProcessSteps",
                schema: "fg",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    IndustrySectorId = table.Column<int>(type: "int", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    GroupId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessSteps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProcessSteps_Groups_GroupId",
                        column: x => x.GroupId,
                        principalSchema: "fg",
                        principalTable: "Groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProcessSteps_IndustrySectors_IndustrySectorId",
                        column: x => x.IndustrySectorId,
                        principalSchema: "fg",
                        principalTable: "IndustrySectors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SubSteps",
                schema: "fg",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IndustrySectorId = table.Column<int>(type: "int", nullable: false),
                    UserRole = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    ShortName = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    PassThroughs = table.Column<int>(type: "int", nullable: false),
                    WebLink = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    Instruction = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubSteps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubSteps_IndustrySectors_IndustrySectorId",
                        column: x => x.IndustrySectorId,
                        principalSchema: "fg",
                        principalTable: "IndustrySectors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkExecutionAdders",
                schema: "fg",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ExecutionId = table.Column<int>(type: "int", nullable: false),
                    AdderTypeId = table.Column<int>(type: "int", nullable: false),
                    Value = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkExecutionAdders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkExecutionAdders_AdderTypes_AdderTypeId",
                        column: x => x.AdderTypeId,
                        principalSchema: "fg",
                        principalTable: "AdderTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkExecutionDefects",
                schema: "fg",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ExecutionId = table.Column<int>(type: "int", nullable: false),
                    DefectTypeId = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkExecutionDefects", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkExecutionDefects_DefectTypes_DefectTypeId",
                        column: x => x.DefectTypeId,
                        principalSchema: "fg",
                        principalTable: "DefectTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProcessSchedules",
                schema: "fg",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Number = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    CustomerName = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    DepartmentId = table.Column<int>(type: "int", nullable: true),
                    IsArchived = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    OneSidedArea = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    TwoSidedArea = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    LaborRate = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    MarkUp = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    PremiumMarkUp = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    OneSidedComplexity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    OneSidedPriceArea = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    TwoSidedComplexity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    TwoSidedPriceArea = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    HighComplexity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    HighComplexityArea = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    TotalJobPrice = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    GroupId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessSchedules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProcessSchedules_Departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalSchema: "fg",
                        principalTable: "Departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProcessSchedules_Groups_GroupId",
                        column: x => x.GroupId,
                        principalSchema: "fg",
                        principalTable: "Groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DeviceCanisters",
                schema: "fg",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DeviceId = table.Column<int>(type: "int", nullable: false),
                    CanisterNo = table.Column<int>(type: "int", nullable: false),
                    MaterialId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeviceCanisters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeviceCanisters_Devices_DeviceId",
                        column: x => x.DeviceId,
                        principalSchema: "fg",
                        principalTable: "Devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DocumentLinks",
                schema: "fg",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DocumentId = table.Column<int>(type: "int", nullable: false),
                    EntityType = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    EntityId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentLinks_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalSchema: "fg",
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Characteristics",
                schema: "fg",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CategoryId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    InputType = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    CalcVariable = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    DefaultValue = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    Sequence = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Characteristics", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Characteristics_MaterialCategories_CategoryId",
                        column: x => x.CategoryId,
                        principalSchema: "fg",
                        principalTable: "MaterialCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Formulas",
                schema: "fg",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CategoryId = table.Column<int>(type: "int", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Number = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    CustomerName = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    IsComplete = table.Column<bool>(type: "bit", nullable: false),
                    BatchSize = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    ContainerType = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    ContainerPrice = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    MarkUp = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Substrate = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    SpinDeltaL = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    SpinDeltaA = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    SpinDeltaB = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    SpinDeltaE = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    SpexDeltaL = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    SpexDeltaA = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    SpexDeltaB = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    SpexDeltaE = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    CreatedBy = table.Column<int>(type: "int", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    GroupId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Formulas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Formulas_Groups_GroupId",
                        column: x => x.GroupId,
                        principalSchema: "fg",
                        principalTable: "Groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Formulas_MaterialCategories_CategoryId",
                        column: x => x.CategoryId,
                        principalSchema: "fg",
                        principalTable: "MaterialCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Materials",
                schema: "fg",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MaterialType = table.Column<int>(type: "int", nullable: false),
                    CategoryId = table.Column<int>(type: "int", nullable: true),
                    ProductCode = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    ProductName = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Density = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Price = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Voc = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Hap = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Tap = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    MinQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    VendorId = table.Column<int>(type: "int", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    GroupId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Materials", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Materials_Groups_GroupId",
                        column: x => x.GroupId,
                        principalSchema: "fg",
                        principalTable: "Groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Materials_MaterialCategories_CategoryId",
                        column: x => x.CategoryId,
                        principalSchema: "fg",
                        principalTable: "MaterialCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PhotoTags",
                schema: "fg",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PhotoId = table.Column<int>(type: "int", nullable: false),
                    Tag = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PhotoTags", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PhotoTags_Photos_PhotoId",
                        column: x => x.PhotoId,
                        principalSchema: "fg",
                        principalTable: "Photos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Messages",
                schema: "fg",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    GroupId = table.Column<int>(type: "int", nullable: false),
                    FromUserId = table.Column<int>(type: "int", nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Body = table.Column<string>(type: "nvarchar(max)", maxLength: 2147483647, nullable: false),
                    MessageType = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    ReplyToId = table.Column<int>(type: "int", nullable: true),
                    SentAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Messages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Messages_Users_FromUserId",
                        column: x => x.FromUserId,
                        principalSchema: "fg",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UserGroups",
                schema: "fg",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "int", nullable: false),
                    GroupId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserGroups", x => new { x.UserId, x.GroupId });
                    table.ForeignKey(
                        name: "FK_UserGroups_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "fg",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserRoles",
                schema: "fg",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Role = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserRoles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserRoles_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "fg",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseOrders",
                schema: "fg",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    VendorId = table.Column<int>(type: "int", nullable: false),
                    PoNumber = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    DeliveryDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ShipName = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    ShipAddress1 = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    ShipAddress2 = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    ShipCity = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    ShipState = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    ShipZip = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    ShipCountry = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GroupId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseOrders_Groups_GroupId",
                        column: x => x.GroupId,
                        principalSchema: "fg",
                        principalTable: "Groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseOrders_Vendors_VendorId",
                        column: x => x.VendorId,
                        principalSchema: "fg",
                        principalTable: "Vendors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkInstructionItems",
                schema: "fg",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WorkInstructionId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    MaterialId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkInstructionItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkInstructionItems_WorkInstructions_WorkInstructionId",
                        column: x => x.WorkInstructionId,
                        principalSchema: "fg",
                        principalTable: "WorkInstructions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkInstructionRelatedDocs",
                schema: "fg",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WorkInstructionId = table.Column<int>(type: "int", nullable: false),
                    DocumentNumber = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    DocumentName = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Author = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    DocumentId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkInstructionRelatedDocs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkInstructionRelatedDocs_WorkInstructions_WorkInstructionId",
                        column: x => x.WorkInstructionId,
                        principalSchema: "fg",
                        principalTable: "WorkInstructions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkInstructionSignatures",
                schema: "fg",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WorkInstructionId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Position = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkInstructionSignatures", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkInstructionSignatures_WorkInstructions_WorkInstructionId",
                        column: x => x.WorkInstructionId,
                        principalSchema: "fg",
                        principalTable: "WorkInstructions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkInstructionSteps",
                schema: "fg",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WorkInstructionId = table.Column<int>(type: "int", nullable: false),
                    Level = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    Body = table.Column<string>(type: "nvarchar(max)", maxLength: 2147483647, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkInstructionSteps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkInstructionSteps_WorkInstructions_WorkInstructionId",
                        column: x => x.WorkInstructionId,
                        principalSchema: "fg",
                        principalTable: "WorkInstructions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkInstructionTrails",
                schema: "fg",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WorkInstructionId = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Author = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Log = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkInstructionTrails", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkInstructionTrails_WorkInstructions_WorkInstructionId",
                        column: x => x.WorkInstructionId,
                        principalSchema: "fg",
                        principalTable: "WorkInstructions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProcessStepEntries",
                schema: "fg",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProcessStepId = table.Column<int>(type: "int", nullable: false),
                    SubStepId = table.Column<int>(type: "int", nullable: false),
                    Pass = table.Column<int>(type: "int", nullable: false),
                    PullDownId = table.Column<int>(type: "int", nullable: true),
                    CategoryId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessStepEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProcessStepEntries_MaterialCategories_CategoryId",
                        column: x => x.CategoryId,
                        principalSchema: "fg",
                        principalTable: "MaterialCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProcessStepEntries_ProcessSteps_ProcessStepId",
                        column: x => x.ProcessStepId,
                        principalSchema: "fg",
                        principalTable: "ProcessSteps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProcessStepEntries_SubSteps_SubStepId",
                        column: x => x.SubStepId,
                        principalSchema: "fg",
                        principalTable: "SubSteps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SubStepPullDowns",
                schema: "fg",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SubStepId = table.Column<int>(type: "int", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    Header = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    ChoiceName = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    MaterialType = table.Column<int>(type: "int", nullable: true),
                    CategoryFilter1 = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    CategoryFilter2 = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubStepPullDowns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubStepPullDowns_SubSteps_SubStepId",
                        column: x => x.SubStepId,
                        principalSchema: "fg",
                        principalTable: "SubSteps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProcessScheduleSteps",
                schema: "fg",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ScheduleId = table.Column<int>(type: "int", nullable: false),
                    ProcessStepId = table.Column<int>(type: "int", nullable: false),
                    Ordering = table.Column<int>(type: "int", nullable: false),
                    NameOverride = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessScheduleSteps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProcessScheduleSteps_ProcessSchedules_ScheduleId",
                        column: x => x.ScheduleId,
                        principalSchema: "fg",
                        principalTable: "ProcessSchedules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProcessScheduleSteps_ProcessSteps_ProcessStepId",
                        column: x => x.ProcessStepId,
                        principalSchema: "fg",
                        principalTable: "ProcessSteps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkExecutions",
                schema: "fg",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ScheduleId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Ordering = table.Column<int>(type: "int", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    GroupId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkExecutions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkExecutions_Groups_GroupId",
                        column: x => x.GroupId,
                        principalSchema: "fg",
                        principalTable: "Groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkExecutions_ProcessSchedules_ScheduleId",
                        column: x => x.ScheduleId,
                        principalSchema: "fg",
                        principalTable: "ProcessSchedules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkExecutions_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "fg",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FormulaIngredients",
                schema: "fg",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FormulaId = table.Column<int>(type: "int", nullable: false),
                    MaterialId = table.Column<int>(type: "int", nullable: false),
                    Grams = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    DispensedGrams = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    IsDispensed = table.Column<bool>(type: "bit", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormulaIngredients", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FormulaIngredients_Formulas_FormulaId",
                        column: x => x.FormulaId,
                        principalSchema: "fg",
                        principalTable: "Formulas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FormulaIngredients_Materials_MaterialId",
                        column: x => x.MaterialId,
                        principalSchema: "fg",
                        principalTable: "Materials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InventoryTransactions",
                schema: "fg",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MaterialId = table.Column<int>(type: "int", nullable: false),
                    LocationId = table.Column<int>(type: "int", nullable: true),
                    BatchNumber = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    CustomerName = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    FormulaId = table.Column<int>(type: "int", nullable: true),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GroupId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InventoryTransactions_Groups_GroupId",
                        column: x => x.GroupId,
                        principalSchema: "fg",
                        principalTable: "Groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryTransactions_MaterialLocations_LocationId",
                        column: x => x.LocationId,
                        principalSchema: "fg",
                        principalTable: "MaterialLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryTransactions_Materials_MaterialId",
                        column: x => x.MaterialId,
                        principalSchema: "fg",
                        principalTable: "Materials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MessageRecipients",
                schema: "fg",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MessageId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Viewed = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MessageRecipients", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MessageRecipients_Messages_MessageId",
                        column: x => x.MessageId,
                        principalSchema: "fg",
                        principalTable: "Messages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MessageRecipients_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "fg",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseOrderLines",
                schema: "fg",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PurchaseOrderId = table.Column<int>(type: "int", nullable: false),
                    MaterialId = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    QuantityType = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseOrderLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseOrderLines_Materials_MaterialId",
                        column: x => x.MaterialId,
                        principalSchema: "fg",
                        principalTable: "Materials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseOrderLines_PurchaseOrders_PurchaseOrderId",
                        column: x => x.PurchaseOrderId,
                        principalSchema: "fg",
                        principalTable: "PurchaseOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkInstructionMedia",
                schema: "fg",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StepId = table.Column<int>(type: "int", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    StoredFile = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkInstructionMedia", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkInstructionMedia_WorkInstructionSteps_StepId",
                        column: x => x.StepId,
                        principalSchema: "fg",
                        principalTable: "WorkInstructionSteps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProcessStepValues",
                schema: "fg",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EntryId = table.Column<int>(type: "int", nullable: false),
                    CharacteristicId = table.Column<int>(type: "int", nullable: false),
                    Value = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    MaterialId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessStepValues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProcessStepValues_Characteristics_CharacteristicId",
                        column: x => x.CharacteristicId,
                        principalSchema: "fg",
                        principalTable: "Characteristics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProcessStepValues_Materials_MaterialId",
                        column: x => x.MaterialId,
                        principalSchema: "fg",
                        principalTable: "Materials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProcessStepValues_ProcessStepEntries_EntryId",
                        column: x => x.EntryId,
                        principalSchema: "fg",
                        principalTable: "ProcessStepEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ScheduleStepOverrides",
                schema: "fg",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ScheduleStepId = table.Column<int>(type: "int", nullable: false),
                    ProcessStepValueId = table.Column<int>(type: "int", nullable: false),
                    Value = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    MinValue = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    MaxValue = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduleStepOverrides", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScheduleStepOverrides_ProcessScheduleSteps_ScheduleStepId",
                        column: x => x.ScheduleStepId,
                        principalSchema: "fg",
                        principalTable: "ProcessScheduleSteps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkExecutionLines",
                schema: "fg",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ExecutionId = table.Column<int>(type: "int", nullable: false),
                    StepNumber = table.Column<int>(type: "int", nullable: false),
                    StepName = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    Value = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Unit = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    MinValue = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    MaxValue = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkExecutionLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkExecutionLines_WorkExecutions_ExecutionId",
                        column: x => x.ExecutionId,
                        principalSchema: "fg",
                        principalTable: "WorkExecutions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkLineChecks",
                schema: "fg",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LineId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    UserName = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    RecordedValue = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    CheckedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkLineChecks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkLineChecks_WorkExecutionLines_LineId",
                        column: x => x.LineId,
                        principalSchema: "fg",
                        principalTable: "WorkExecutionLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AdderTypes_GroupId",
                schema: "fg",
                table: "AdderTypes",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_AppSettings_Key",
                schema: "fg",
                table: "AppSettings",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_EntityType_EntityId",
                schema: "fg",
                table: "AuditLogs",
                columns: new[] { "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_Characteristics_CategoryId",
                schema: "fg",
                table: "Characteristics",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_DefectTypes_GroupId",
                schema: "fg",
                table: "DefectTypes",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_Departments_GroupId",
                schema: "fg",
                table: "Departments",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceCanisters_DeviceId",
                schema: "fg",
                table: "DeviceCanisters",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceMetrics_DeviceId_Timestamp",
                schema: "fg",
                table: "DeviceMetrics",
                columns: new[] { "DeviceId", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_Devices_ApiKey",
                schema: "fg",
                table: "Devices",
                column: "ApiKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Devices_GroupId",
                schema: "fg",
                table: "Devices",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentLinks_DocumentId",
                schema: "fg",
                table: "DocumentLinks",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentLinks_EntityType_EntityId",
                schema: "fg",
                table: "DocumentLinks",
                columns: new[] { "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_Documents_GroupId",
                schema: "fg",
                table: "Documents",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_FormulaIngredients_FormulaId",
                schema: "fg",
                table: "FormulaIngredients",
                column: "FormulaId");

            migrationBuilder.CreateIndex(
                name: "IX_FormulaIngredients_MaterialId",
                schema: "fg",
                table: "FormulaIngredients",
                column: "MaterialId");

            migrationBuilder.CreateIndex(
                name: "IX_Formulas_CategoryId",
                schema: "fg",
                table: "Formulas",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Formulas_GroupId",
                schema: "fg",
                table: "Formulas",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_Groups_Name",
                schema: "fg",
                table: "Groups",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransactions_GroupId",
                schema: "fg",
                table: "InventoryTransactions",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransactions_LocationId",
                schema: "fg",
                table: "InventoryTransactions",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransactions_MaterialId",
                schema: "fg",
                table: "InventoryTransactions",
                column: "MaterialId");

            migrationBuilder.CreateIndex(
                name: "IX_MaterialCategories_GroupId",
                schema: "fg",
                table: "MaterialCategories",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_MaterialLocations_GroupId",
                schema: "fg",
                table: "MaterialLocations",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_Materials_CategoryId",
                schema: "fg",
                table: "Materials",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Materials_GroupId_MaterialType_IsDeleted",
                schema: "fg",
                table: "Materials",
                columns: new[] { "GroupId", "MaterialType", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_MessageRecipients_MessageId",
                schema: "fg",
                table: "MessageRecipients",
                column: "MessageId");

            migrationBuilder.CreateIndex(
                name: "IX_MessageRecipients_UserId_Viewed",
                schema: "fg",
                table: "MessageRecipients",
                columns: new[] { "UserId", "Viewed" });

            migrationBuilder.CreateIndex(
                name: "IX_Messages_FromUserId",
                schema: "fg",
                table: "Messages",
                column: "FromUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Photos_GroupId",
                schema: "fg",
                table: "Photos",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_PhotoTags_PhotoId",
                schema: "fg",
                table: "PhotoTags",
                column: "PhotoId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessSchedules_DepartmentId",
                schema: "fg",
                table: "ProcessSchedules",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessSchedules_GroupId_IsArchived",
                schema: "fg",
                table: "ProcessSchedules",
                columns: new[] { "GroupId", "IsArchived" });

            migrationBuilder.CreateIndex(
                name: "IX_ProcessScheduleSteps_ProcessStepId",
                schema: "fg",
                table: "ProcessScheduleSteps",
                column: "ProcessStepId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessScheduleSteps_ScheduleId",
                schema: "fg",
                table: "ProcessScheduleSteps",
                column: "ScheduleId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessStepEntries_CategoryId",
                schema: "fg",
                table: "ProcessStepEntries",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessStepEntries_ProcessStepId",
                schema: "fg",
                table: "ProcessStepEntries",
                column: "ProcessStepId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessStepEntries_SubStepId",
                schema: "fg",
                table: "ProcessStepEntries",
                column: "SubStepId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessSteps_GroupId_IsDeleted",
                schema: "fg",
                table: "ProcessSteps",
                columns: new[] { "GroupId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_ProcessSteps_IndustrySectorId",
                schema: "fg",
                table: "ProcessSteps",
                column: "IndustrySectorId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessStepValues_CharacteristicId",
                schema: "fg",
                table: "ProcessStepValues",
                column: "CharacteristicId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessStepValues_EntryId",
                schema: "fg",
                table: "ProcessStepValues",
                column: "EntryId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessStepValues_MaterialId",
                schema: "fg",
                table: "ProcessStepValues",
                column: "MaterialId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderLines_MaterialId",
                schema: "fg",
                table: "PurchaseOrderLines",
                column: "MaterialId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderLines_PurchaseOrderId",
                schema: "fg",
                table: "PurchaseOrderLines",
                column: "PurchaseOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrders_GroupId",
                schema: "fg",
                table: "PurchaseOrders",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrders_VendorId",
                schema: "fg",
                table: "PurchaseOrders",
                column: "VendorId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleStepOverrides_ScheduleStepId",
                schema: "fg",
                table: "ScheduleStepOverrides",
                column: "ScheduleStepId");

            migrationBuilder.CreateIndex(
                name: "IX_SubStepPullDowns_SubStepId",
                schema: "fg",
                table: "SubStepPullDowns",
                column: "SubStepId");

            migrationBuilder.CreateIndex(
                name: "IX_SubSteps_IndustrySectorId_Sequence",
                schema: "fg",
                table: "SubSteps",
                columns: new[] { "IndustrySectorId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserRoles_UserId",
                schema: "fg",
                table: "UserRoles",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                schema: "fg",
                table: "Users",
                column: "Email");

            migrationBuilder.CreateIndex(
                name: "IX_Users_GroupId",
                schema: "fg",
                table: "Users",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Username",
                schema: "fg",
                table: "Users",
                column: "Username");

            migrationBuilder.CreateIndex(
                name: "IX_Vendors_GroupId",
                schema: "fg",
                table: "Vendors",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkExecutionAdders_AdderTypeId",
                schema: "fg",
                table: "WorkExecutionAdders",
                column: "AdderTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkExecutionDefects_DefectTypeId",
                schema: "fg",
                table: "WorkExecutionDefects",
                column: "DefectTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkExecutionLines_ExecutionId",
                schema: "fg",
                table: "WorkExecutionLines",
                column: "ExecutionId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkExecutions_GroupId_Status",
                schema: "fg",
                table: "WorkExecutions",
                columns: new[] { "GroupId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkExecutions_ScheduleId",
                schema: "fg",
                table: "WorkExecutions",
                column: "ScheduleId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkExecutions_UserId",
                schema: "fg",
                table: "WorkExecutions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkInstructionItems_WorkInstructionId",
                schema: "fg",
                table: "WorkInstructionItems",
                column: "WorkInstructionId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkInstructionMedia_StepId",
                schema: "fg",
                table: "WorkInstructionMedia",
                column: "StepId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkInstructionRelatedDocs_WorkInstructionId",
                schema: "fg",
                table: "WorkInstructionRelatedDocs",
                column: "WorkInstructionId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkInstructions_GroupId",
                schema: "fg",
                table: "WorkInstructions",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkInstructionSignatures_WorkInstructionId",
                schema: "fg",
                table: "WorkInstructionSignatures",
                column: "WorkInstructionId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkInstructionSteps_WorkInstructionId",
                schema: "fg",
                table: "WorkInstructionSteps",
                column: "WorkInstructionId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkInstructionTrails_WorkInstructionId",
                schema: "fg",
                table: "WorkInstructionTrails",
                column: "WorkInstructionId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkLineChecks_LineId",
                schema: "fg",
                table: "WorkLineChecks",
                column: "LineId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppSettings",
                schema: "fg");

            migrationBuilder.DropTable(
                name: "AuditLogs",
                schema: "fg");

            migrationBuilder.DropTable(
                name: "DeviceCanisters",
                schema: "fg");

            migrationBuilder.DropTable(
                name: "DeviceMetrics",
                schema: "fg");

            migrationBuilder.DropTable(
                name: "DocumentLinks",
                schema: "fg");

            migrationBuilder.DropTable(
                name: "FormulaIngredients",
                schema: "fg");

            migrationBuilder.DropTable(
                name: "InventoryTransactions",
                schema: "fg");

            migrationBuilder.DropTable(
                name: "MessageRecipients",
                schema: "fg");

            migrationBuilder.DropTable(
                name: "PhotoTags",
                schema: "fg");

            migrationBuilder.DropTable(
                name: "ProcessStepValues",
                schema: "fg");

            migrationBuilder.DropTable(
                name: "PurchaseOrderLines",
                schema: "fg");

            migrationBuilder.DropTable(
                name: "ScheduleStepOverrides",
                schema: "fg");

            migrationBuilder.DropTable(
                name: "SubStepPullDowns",
                schema: "fg");

            migrationBuilder.DropTable(
                name: "UserGroups",
                schema: "fg");

            migrationBuilder.DropTable(
                name: "UserRoles",
                schema: "fg");

            migrationBuilder.DropTable(
                name: "WorkExecutionAdders",
                schema: "fg");

            migrationBuilder.DropTable(
                name: "WorkExecutionDefects",
                schema: "fg");

            migrationBuilder.DropTable(
                name: "WorkInstructionItems",
                schema: "fg");

            migrationBuilder.DropTable(
                name: "WorkInstructionMedia",
                schema: "fg");

            migrationBuilder.DropTable(
                name: "WorkInstructionRelatedDocs",
                schema: "fg");

            migrationBuilder.DropTable(
                name: "WorkInstructionSignatures",
                schema: "fg");

            migrationBuilder.DropTable(
                name: "WorkInstructionTrails",
                schema: "fg");

            migrationBuilder.DropTable(
                name: "WorkLineChecks",
                schema: "fg");

            migrationBuilder.DropTable(
                name: "Devices",
                schema: "fg");

            migrationBuilder.DropTable(
                name: "Documents",
                schema: "fg");

            migrationBuilder.DropTable(
                name: "Formulas",
                schema: "fg");

            migrationBuilder.DropTable(
                name: "MaterialLocations",
                schema: "fg");

            migrationBuilder.DropTable(
                name: "Messages",
                schema: "fg");

            migrationBuilder.DropTable(
                name: "Photos",
                schema: "fg");

            migrationBuilder.DropTable(
                name: "Characteristics",
                schema: "fg");

            migrationBuilder.DropTable(
                name: "ProcessStepEntries",
                schema: "fg");

            migrationBuilder.DropTable(
                name: "Materials",
                schema: "fg");

            migrationBuilder.DropTable(
                name: "PurchaseOrders",
                schema: "fg");

            migrationBuilder.DropTable(
                name: "ProcessScheduleSteps",
                schema: "fg");

            migrationBuilder.DropTable(
                name: "AdderTypes",
                schema: "fg");

            migrationBuilder.DropTable(
                name: "DefectTypes",
                schema: "fg");

            migrationBuilder.DropTable(
                name: "WorkInstructionSteps",
                schema: "fg");

            migrationBuilder.DropTable(
                name: "WorkExecutionLines",
                schema: "fg");

            migrationBuilder.DropTable(
                name: "SubSteps",
                schema: "fg");

            migrationBuilder.DropTable(
                name: "MaterialCategories",
                schema: "fg");

            migrationBuilder.DropTable(
                name: "Vendors",
                schema: "fg");

            migrationBuilder.DropTable(
                name: "ProcessSteps",
                schema: "fg");

            migrationBuilder.DropTable(
                name: "WorkInstructions",
                schema: "fg");

            migrationBuilder.DropTable(
                name: "WorkExecutions",
                schema: "fg");

            migrationBuilder.DropTable(
                name: "IndustrySectors",
                schema: "fg");

            migrationBuilder.DropTable(
                name: "ProcessSchedules",
                schema: "fg");

            migrationBuilder.DropTable(
                name: "Users",
                schema: "fg");

            migrationBuilder.DropTable(
                name: "Departments",
                schema: "fg");

            migrationBuilder.DropTable(
                name: "Groups",
                schema: "fg");
        }
    }
}
