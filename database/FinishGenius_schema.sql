IF OBJECT_ID(N'[fg].[__EFMigrationsHistory]') IS NULL
BEGIN
    IF SCHEMA_ID(N'fg') IS NULL EXEC(N'CREATE SCHEMA [fg];');
    CREATE TABLE [fg].[__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    IF SCHEMA_ID(N'fg') IS NULL EXEC(N'CREATE SCHEMA [fg];');
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE TABLE [fg].[AppSettings] (
        [Id] int NOT NULL IDENTITY,
        [Key] nvarchar(400) NOT NULL,
        [Value] nvarchar(max) NULL,
        CONSTRAINT [PK_AppSettings] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE TABLE [fg].[AuditLogs] (
        [Id] bigint NOT NULL IDENTITY,
        [GroupId] int NULL,
        [UserId] int NULL,
        [UserName] nvarchar(400) NULL,
        [EntityType] nvarchar(400) NOT NULL,
        [EntityId] int NOT NULL,
        [Action] nvarchar(400) NOT NULL,
        [Details] nvarchar(4000) NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_AuditLogs] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE TABLE [fg].[DeviceMetrics] (
        [Id] bigint NOT NULL IDENTITY,
        [DeviceId] int NOT NULL,
        [Name] nvarchar(400) NOT NULL,
        [Unit] nvarchar(400) NULL,
        [Timestamp] datetime2 NOT NULL,
        [Value] float NULL,
        [TextValue] nvarchar(400) NULL,
        CONSTRAINT [PK_DeviceMetrics] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE TABLE [fg].[Groups] (
        [Id] int NOT NULL IDENTITY,
        [Name] nvarchar(400) NOT NULL,
        [Address1] nvarchar(400) NULL,
        [Address2] nvarchar(400) NULL,
        [City] nvarchar(400) NULL,
        [State] nvarchar(400) NULL,
        [Zip] nvarchar(400) NULL,
        [Country] nvarchar(400) NULL,
        [TimeZone] nvarchar(400) NOT NULL,
        [ApiKey] uniqueidentifier NULL,
        [LogoFile] nvarchar(400) NULL,
        [ChecklistDeletionEnabled] bit NOT NULL,
        [IsDeleted] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        CONSTRAINT [PK_Groups] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE TABLE [fg].[IndustrySectors] (
        [Id] int NOT NULL IDENTITY,
        [Name] nvarchar(400) NOT NULL,
        CONSTRAINT [PK_IndustrySectors] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE TABLE [fg].[AdderTypes] (
        [Id] int NOT NULL IDENTITY,
        [Name] nvarchar(400) NOT NULL,
        [IsArchived] bit NOT NULL,
        [GroupId] int NOT NULL,
        CONSTRAINT [PK_AdderTypes] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AdderTypes_Groups_GroupId] FOREIGN KEY ([GroupId]) REFERENCES [fg].[Groups] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE TABLE [fg].[DefectTypes] (
        [Id] int NOT NULL IDENTITY,
        [Name] nvarchar(400) NOT NULL,
        [ChartColor] nvarchar(400) NULL,
        [IsArchived] bit NOT NULL,
        [GroupId] int NOT NULL,
        CONSTRAINT [PK_DefectTypes] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_DefectTypes_Groups_GroupId] FOREIGN KEY ([GroupId]) REFERENCES [fg].[Groups] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE TABLE [fg].[Departments] (
        [Id] int NOT NULL IDENTITY,
        [Name] nvarchar(400) NOT NULL,
        [GroupId] int NOT NULL,
        CONSTRAINT [PK_Departments] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Departments_Groups_GroupId] FOREIGN KEY ([GroupId]) REFERENCES [fg].[Groups] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE TABLE [fg].[Devices] (
        [Id] int NOT NULL IDENTITY,
        [Name] nvarchar(400) NOT NULL,
        [Description] nvarchar(400) NULL,
        [DeviceType] int NOT NULL,
        [NetworkBridgeId] int NULL,
        [IpAddress] nvarchar(400) NULL,
        [ApiKey] uniqueidentifier NOT NULL,
        [IsArchived] bit NOT NULL,
        [LastSeenAt] datetime2 NULL,
        [CreatedAt] datetime2 NOT NULL,
        [GroupId] int NOT NULL,
        CONSTRAINT [PK_Devices] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Devices_Groups_GroupId] FOREIGN KEY ([GroupId]) REFERENCES [fg].[Groups] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE TABLE [fg].[Documents] (
        [Id] int NOT NULL IDENTITY,
        [Name] nvarchar(400) NOT NULL,
        [FileName] nvarchar(400) NULL,
        [StoredFile] nvarchar(400) NULL,
        [ContentType] nvarchar(400) NULL,
        [FileSize] bigint NOT NULL,
        [CreatedBy] int NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        [GroupId] int NOT NULL,
        CONSTRAINT [PK_Documents] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Documents_Groups_GroupId] FOREIGN KEY ([GroupId]) REFERENCES [fg].[Groups] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE TABLE [fg].[MaterialCategories] (
        [Id] int NOT NULL IDENTITY,
        [Name] nvarchar(400) NOT NULL,
        [MaterialType] int NOT NULL,
        [Filter1] nvarchar(400) NULL,
        [Filter2] nvarchar(400) NULL,
        [GroupId] int NOT NULL,
        CONSTRAINT [PK_MaterialCategories] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_MaterialCategories_Groups_GroupId] FOREIGN KEY ([GroupId]) REFERENCES [fg].[Groups] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE TABLE [fg].[MaterialLocations] (
        [Id] int NOT NULL IDENTITY,
        [Name] nvarchar(400) NOT NULL,
        [MaterialType] int NOT NULL,
        [IsDeleted] bit NOT NULL,
        [GroupId] int NOT NULL,
        CONSTRAINT [PK_MaterialLocations] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_MaterialLocations_Groups_GroupId] FOREIGN KEY ([GroupId]) REFERENCES [fg].[Groups] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE TABLE [fg].[Photos] (
        [Id] int NOT NULL IDENTITY,
        [Name] nvarchar(400) NOT NULL,
        [StoredFile] nvarchar(400) NOT NULL,
        [ContentType] nvarchar(400) NULL,
        [CreatedBy] int NULL,
        [CreatedAt] datetime2 NOT NULL,
        [GroupId] int NOT NULL,
        CONSTRAINT [PK_Photos] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Photos_Groups_GroupId] FOREIGN KEY ([GroupId]) REFERENCES [fg].[Groups] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE TABLE [fg].[Users] (
        [Id] int NOT NULL IDENTITY,
        [GroupId] int NOT NULL,
        [Email] nvarchar(400) NOT NULL,
        [Username] nvarchar(400) NOT NULL,
        [PasswordHash] nvarchar(400) NOT NULL,
        [FirstName] nvarchar(400) NULL,
        [LastName] nvarchar(400) NULL,
        [PhoneNumber] nvarchar(400) NULL,
        [Disabled] bit NOT NULL,
        [AgreementAccepted] bit NOT NULL,
        [AgreementAcceptedAt] datetime2 NULL,
        [DefaultGroupId] int NULL,
        [IsDeleted] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [LastLoginAt] datetime2 NULL,
        CONSTRAINT [PK_Users] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Users_Groups_GroupId] FOREIGN KEY ([GroupId]) REFERENCES [fg].[Groups] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE TABLE [fg].[Vendors] (
        [Id] int NOT NULL IDENTITY,
        [VendorName] nvarchar(400) NOT NULL,
        [Address] nvarchar(400) NULL,
        [City] nvarchar(400) NULL,
        [State] nvarchar(400) NULL,
        [Zip] nvarchar(400) NULL,
        [Country] nvarchar(400) NULL,
        [PaymentTerms] nvarchar(400) NULL,
        [AccountNumber] nvarchar(400) NULL,
        [ContactName] nvarchar(400) NULL,
        [OfficePhone] nvarchar(400) NULL,
        [MobilePhone] nvarchar(400) NULL,
        [VendorEmail] nvarchar(400) NULL,
        [RequestorEmail] nvarchar(400) NULL,
        [IsDeleted] bit NOT NULL,
        [GroupId] int NOT NULL,
        CONSTRAINT [PK_Vendors] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Vendors_Groups_GroupId] FOREIGN KEY ([GroupId]) REFERENCES [fg].[Groups] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE TABLE [fg].[WorkInstructions] (
        [Id] int NOT NULL IDENTITY,
        [DocumentNumber] nvarchar(400) NOT NULL,
        [Name] nvarchar(400) NOT NULL,
        [IssueDate] datetime2 NOT NULL,
        [Version] int NOT NULL,
        [IsReleased] bit NOT NULL,
        [Controlled] bit NOT NULL,
        [Location] nvarchar(4000) NULL,
        [Purpose] nvarchar(4000) NULL,
        [Scope] nvarchar(4000) NULL,
        [Terminology] nvarchar(4000) NULL,
        [IsDeleted] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        [GroupId] int NOT NULL,
        CONSTRAINT [PK_WorkInstructions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_WorkInstructions_Groups_GroupId] FOREIGN KEY ([GroupId]) REFERENCES [fg].[Groups] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE TABLE [fg].[ProcessSteps] (
        [Id] int NOT NULL IDENTITY,
        [Name] nvarchar(400) NOT NULL,
        [IndustrySectorId] int NOT NULL,
        [IsDeleted] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        [GroupId] int NOT NULL,
        CONSTRAINT [PK_ProcessSteps] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ProcessSteps_Groups_GroupId] FOREIGN KEY ([GroupId]) REFERENCES [fg].[Groups] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_ProcessSteps_IndustrySectors_IndustrySectorId] FOREIGN KEY ([IndustrySectorId]) REFERENCES [fg].[IndustrySectors] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE TABLE [fg].[SubSteps] (
        [Id] int NOT NULL IDENTITY,
        [IndustrySectorId] int NOT NULL,
        [UserRole] nvarchar(400) NOT NULL,
        [Name] nvarchar(400) NOT NULL,
        [ShortName] nvarchar(400) NOT NULL,
        [Sequence] int NOT NULL,
        [PassThroughs] int NOT NULL,
        [WebLink] nvarchar(400) NULL,
        [Instruction] nvarchar(4000) NULL,
        CONSTRAINT [PK_SubSteps] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_SubSteps_IndustrySectors_IndustrySectorId] FOREIGN KEY ([IndustrySectorId]) REFERENCES [fg].[IndustrySectors] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE TABLE [fg].[WorkExecutionAdders] (
        [Id] int NOT NULL IDENTITY,
        [ExecutionId] int NOT NULL,
        [AdderTypeId] int NOT NULL,
        [Value] nvarchar(400) NOT NULL,
        [UserId] int NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_WorkExecutionAdders] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_WorkExecutionAdders_AdderTypes_AdderTypeId] FOREIGN KEY ([AdderTypeId]) REFERENCES [fg].[AdderTypes] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE TABLE [fg].[WorkExecutionDefects] (
        [Id] int NOT NULL IDENTITY,
        [ExecutionId] int NOT NULL,
        [DefectTypeId] int NOT NULL,
        [Quantity] int NOT NULL,
        [Notes] nvarchar(400) NULL,
        [UserId] int NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_WorkExecutionDefects] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_WorkExecutionDefects_DefectTypes_DefectTypeId] FOREIGN KEY ([DefectTypeId]) REFERENCES [fg].[DefectTypes] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE TABLE [fg].[ProcessSchedules] (
        [Id] int NOT NULL IDENTITY,
        [Name] nvarchar(400) NOT NULL,
        [Number] nvarchar(400) NOT NULL,
        [CustomerName] nvarchar(400) NULL,
        [DepartmentId] int NULL,
        [IsArchived] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        [OneSidedArea] decimal(18,4) NOT NULL,
        [TwoSidedArea] decimal(18,4) NOT NULL,
        [LaborRate] decimal(18,4) NOT NULL,
        [MarkUp] decimal(18,4) NOT NULL,
        [PremiumMarkUp] decimal(18,4) NOT NULL,
        [OneSidedComplexity] decimal(18,4) NOT NULL,
        [OneSidedPriceArea] decimal(18,4) NOT NULL,
        [TwoSidedComplexity] decimal(18,4) NOT NULL,
        [TwoSidedPriceArea] decimal(18,4) NOT NULL,
        [HighComplexity] decimal(18,4) NOT NULL,
        [HighComplexityArea] decimal(18,4) NOT NULL,
        [TotalJobPrice] decimal(18,4) NOT NULL,
        [GroupId] int NOT NULL,
        CONSTRAINT [PK_ProcessSchedules] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ProcessSchedules_Departments_DepartmentId] FOREIGN KEY ([DepartmentId]) REFERENCES [fg].[Departments] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_ProcessSchedules_Groups_GroupId] FOREIGN KEY ([GroupId]) REFERENCES [fg].[Groups] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE TABLE [fg].[DeviceCanisters] (
        [Id] int NOT NULL IDENTITY,
        [DeviceId] int NOT NULL,
        [CanisterNo] int NOT NULL,
        [MaterialId] int NULL,
        CONSTRAINT [PK_DeviceCanisters] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_DeviceCanisters_Devices_DeviceId] FOREIGN KEY ([DeviceId]) REFERENCES [fg].[Devices] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE TABLE [fg].[DocumentLinks] (
        [Id] int NOT NULL IDENTITY,
        [DocumentId] int NOT NULL,
        [EntityType] nvarchar(400) NOT NULL,
        [EntityId] int NOT NULL,
        CONSTRAINT [PK_DocumentLinks] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_DocumentLinks_Documents_DocumentId] FOREIGN KEY ([DocumentId]) REFERENCES [fg].[Documents] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE TABLE [fg].[Characteristics] (
        [Id] int NOT NULL IDENTITY,
        [CategoryId] int NOT NULL,
        [Name] nvarchar(400) NOT NULL,
        [Unit] nvarchar(400) NULL,
        [InputType] nvarchar(400) NOT NULL,
        [CalcVariable] nvarchar(400) NULL,
        [DefaultValue] nvarchar(400) NULL,
        [Sequence] int NOT NULL,
        CONSTRAINT [PK_Characteristics] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Characteristics_MaterialCategories_CategoryId] FOREIGN KEY ([CategoryId]) REFERENCES [fg].[MaterialCategories] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE TABLE [fg].[Formulas] (
        [Id] int NOT NULL IDENTITY,
        [CategoryId] int NULL,
        [Name] nvarchar(400) NOT NULL,
        [Number] nvarchar(400) NULL,
        [CustomerName] nvarchar(400) NULL,
        [IsComplete] bit NOT NULL,
        [BatchSize] decimal(18,4) NOT NULL,
        [ContainerType] nvarchar(400) NULL,
        [ContainerPrice] decimal(18,4) NOT NULL,
        [MarkUp] decimal(18,4) NOT NULL,
        [Substrate] nvarchar(400) NULL,
        [Notes] nvarchar(4000) NULL,
        [SpinDeltaL] decimal(18,4) NULL,
        [SpinDeltaA] decimal(18,4) NULL,
        [SpinDeltaB] decimal(18,4) NULL,
        [SpinDeltaE] decimal(18,4) NULL,
        [SpexDeltaL] decimal(18,4) NULL,
        [SpexDeltaA] decimal(18,4) NULL,
        [SpexDeltaB] decimal(18,4) NULL,
        [SpexDeltaE] decimal(18,4) NULL,
        [CreatedBy] int NULL,
        [IsDeleted] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        [GroupId] int NOT NULL,
        CONSTRAINT [PK_Formulas] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Formulas_Groups_GroupId] FOREIGN KEY ([GroupId]) REFERENCES [fg].[Groups] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Formulas_MaterialCategories_CategoryId] FOREIGN KEY ([CategoryId]) REFERENCES [fg].[MaterialCategories] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE TABLE [fg].[Materials] (
        [Id] int NOT NULL IDENTITY,
        [MaterialType] int NOT NULL,
        [CategoryId] int NULL,
        [ProductCode] nvarchar(400) NULL,
        [ProductName] nvarchar(400) NOT NULL,
        [Density] decimal(18,4) NOT NULL,
        [Price] decimal(18,4) NOT NULL,
        [Voc] decimal(18,4) NOT NULL,
        [Hap] decimal(18,4) NOT NULL,
        [Tap] decimal(18,4) NOT NULL,
        [MinQuantity] decimal(18,4) NOT NULL,
        [VendorId] int NULL,
        [Notes] nvarchar(4000) NULL,
        [IsDeleted] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        [GroupId] int NOT NULL,
        CONSTRAINT [PK_Materials] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Materials_Groups_GroupId] FOREIGN KEY ([GroupId]) REFERENCES [fg].[Groups] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Materials_MaterialCategories_CategoryId] FOREIGN KEY ([CategoryId]) REFERENCES [fg].[MaterialCategories] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE TABLE [fg].[PhotoTags] (
        [Id] int NOT NULL IDENTITY,
        [PhotoId] int NOT NULL,
        [Tag] nvarchar(400) NOT NULL,
        CONSTRAINT [PK_PhotoTags] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_PhotoTags_Photos_PhotoId] FOREIGN KEY ([PhotoId]) REFERENCES [fg].[Photos] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE TABLE [fg].[Messages] (
        [Id] int NOT NULL IDENTITY,
        [GroupId] int NOT NULL,
        [FromUserId] int NOT NULL,
        [Subject] nvarchar(400) NOT NULL,
        [Body] nvarchar(max) NOT NULL,
        [MessageType] nvarchar(400) NOT NULL,
        [ReplyToId] int NULL,
        [SentAt] datetime2 NOT NULL,
        CONSTRAINT [PK_Messages] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Messages_Users_FromUserId] FOREIGN KEY ([FromUserId]) REFERENCES [fg].[Users] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE TABLE [fg].[UserGroups] (
        [UserId] int NOT NULL,
        [GroupId] int NOT NULL,
        CONSTRAINT [PK_UserGroups] PRIMARY KEY ([UserId], [GroupId]),
        CONSTRAINT [FK_UserGroups_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [fg].[Users] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE TABLE [fg].[UserRoles] (
        [Id] int NOT NULL IDENTITY,
        [UserId] int NOT NULL,
        [Role] nvarchar(400) NOT NULL,
        CONSTRAINT [PK_UserRoles] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_UserRoles_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [fg].[Users] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE TABLE [fg].[PurchaseOrders] (
        [Id] int NOT NULL IDENTITY,
        [VendorId] int NOT NULL,
        [PoNumber] nvarchar(400) NOT NULL,
        [DeliveryDate] datetime2 NULL,
        [ShipName] nvarchar(400) NULL,
        [ShipAddress1] nvarchar(400) NULL,
        [ShipAddress2] nvarchar(400) NULL,
        [ShipCity] nvarchar(400) NULL,
        [ShipState] nvarchar(400) NULL,
        [ShipZip] nvarchar(400) NULL,
        [ShipCountry] nvarchar(400) NULL,
        [CreatedBy] int NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [GroupId] int NOT NULL,
        CONSTRAINT [PK_PurchaseOrders] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_PurchaseOrders_Groups_GroupId] FOREIGN KEY ([GroupId]) REFERENCES [fg].[Groups] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_PurchaseOrders_Vendors_VendorId] FOREIGN KEY ([VendorId]) REFERENCES [fg].[Vendors] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE TABLE [fg].[WorkInstructionItems] (
        [Id] int NOT NULL IDENTITY,
        [WorkInstructionId] int NOT NULL,
        [Kind] nvarchar(400) NOT NULL,
        [Description] nvarchar(400) NOT NULL,
        [MaterialId] int NULL,
        CONSTRAINT [PK_WorkInstructionItems] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_WorkInstructionItems_WorkInstructions_WorkInstructionId] FOREIGN KEY ([WorkInstructionId]) REFERENCES [fg].[WorkInstructions] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE TABLE [fg].[WorkInstructionRelatedDocs] (
        [Id] int NOT NULL IDENTITY,
        [WorkInstructionId] int NOT NULL,
        [DocumentNumber] nvarchar(400) NOT NULL,
        [DocumentName] nvarchar(400) NOT NULL,
        [Author] nvarchar(400) NULL,
        [DocumentId] int NULL,
        CONSTRAINT [PK_WorkInstructionRelatedDocs] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_WorkInstructionRelatedDocs_WorkInstructions_WorkInstructionId] FOREIGN KEY ([WorkInstructionId]) REFERENCES [fg].[WorkInstructions] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE TABLE [fg].[WorkInstructionSignatures] (
        [Id] int NOT NULL IDENTITY,
        [WorkInstructionId] int NOT NULL,
        [Name] nvarchar(400) NOT NULL,
        [Position] nvarchar(400) NULL,
        [Date] datetime2 NOT NULL,
        CONSTRAINT [PK_WorkInstructionSignatures] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_WorkInstructionSignatures_WorkInstructions_WorkInstructionId] FOREIGN KEY ([WorkInstructionId]) REFERENCES [fg].[WorkInstructions] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE TABLE [fg].[WorkInstructionSteps] (
        [Id] int NOT NULL IDENTITY,
        [WorkInstructionId] int NOT NULL,
        [Level] int NOT NULL,
        [Title] nvarchar(4000) NOT NULL,
        [Body] nvarchar(max) NULL,
        CONSTRAINT [PK_WorkInstructionSteps] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_WorkInstructionSteps_WorkInstructions_WorkInstructionId] FOREIGN KEY ([WorkInstructionId]) REFERENCES [fg].[WorkInstructions] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE TABLE [fg].[WorkInstructionTrails] (
        [Id] int NOT NULL IDENTITY,
        [WorkInstructionId] int NOT NULL,
        [Version] nvarchar(400) NOT NULL,
        [Author] nvarchar(400) NOT NULL,
        [Log] nvarchar(4000) NULL,
        [Date] datetime2 NOT NULL,
        CONSTRAINT [PK_WorkInstructionTrails] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_WorkInstructionTrails_WorkInstructions_WorkInstructionId] FOREIGN KEY ([WorkInstructionId]) REFERENCES [fg].[WorkInstructions] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE TABLE [fg].[ProcessStepEntries] (
        [Id] int NOT NULL IDENTITY,
        [ProcessStepId] int NOT NULL,
        [SubStepId] int NOT NULL,
        [Pass] int NOT NULL,
        [PullDownId] int NULL,
        [CategoryId] int NULL,
        CONSTRAINT [PK_ProcessStepEntries] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ProcessStepEntries_MaterialCategories_CategoryId] FOREIGN KEY ([CategoryId]) REFERENCES [fg].[MaterialCategories] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_ProcessStepEntries_ProcessSteps_ProcessStepId] FOREIGN KEY ([ProcessStepId]) REFERENCES [fg].[ProcessSteps] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_ProcessStepEntries_SubSteps_SubStepId] FOREIGN KEY ([SubStepId]) REFERENCES [fg].[SubSteps] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE TABLE [fg].[SubStepPullDowns] (
        [Id] int NOT NULL IDENTITY,
        [SubStepId] int NOT NULL,
        [Sequence] int NOT NULL,
        [Header] nvarchar(400) NOT NULL,
        [ChoiceName] nvarchar(400) NULL,
        [MaterialType] int NULL,
        [CategoryFilter1] nvarchar(400) NULL,
        [CategoryFilter2] nvarchar(400) NULL,
        CONSTRAINT [PK_SubStepPullDowns] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_SubStepPullDowns_SubSteps_SubStepId] FOREIGN KEY ([SubStepId]) REFERENCES [fg].[SubSteps] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE TABLE [fg].[ProcessScheduleSteps] (
        [Id] int NOT NULL IDENTITY,
        [ScheduleId] int NOT NULL,
        [ProcessStepId] int NOT NULL,
        [Ordering] int NOT NULL,
        [NameOverride] nvarchar(400) NULL,
        CONSTRAINT [PK_ProcessScheduleSteps] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ProcessScheduleSteps_ProcessSchedules_ScheduleId] FOREIGN KEY ([ScheduleId]) REFERENCES [fg].[ProcessSchedules] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_ProcessScheduleSteps_ProcessSteps_ProcessStepId] FOREIGN KEY ([ProcessStepId]) REFERENCES [fg].[ProcessSteps] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE TABLE [fg].[WorkExecutions] (
        [Id] int NOT NULL IDENTITY,
        [ScheduleId] int NOT NULL,
        [UserId] int NOT NULL,
        [Status] int NOT NULL,
        [Ordering] int NOT NULL,
        [Notes] nvarchar(4000) NULL,
        [StartedAt] datetime2 NOT NULL,
        [CompletedAt] datetime2 NULL,
        [GroupId] int NOT NULL,
        CONSTRAINT [PK_WorkExecutions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_WorkExecutions_Groups_GroupId] FOREIGN KEY ([GroupId]) REFERENCES [fg].[Groups] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_WorkExecutions_ProcessSchedules_ScheduleId] FOREIGN KEY ([ScheduleId]) REFERENCES [fg].[ProcessSchedules] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_WorkExecutions_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [fg].[Users] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE TABLE [fg].[FormulaIngredients] (
        [Id] int NOT NULL IDENTITY,
        [FormulaId] int NOT NULL,
        [MaterialId] int NOT NULL,
        [Grams] decimal(18,4) NOT NULL,
        [DispensedGrams] decimal(18,4) NOT NULL,
        [IsDispensed] bit NOT NULL,
        [Sequence] int NOT NULL,
        CONSTRAINT [PK_FormulaIngredients] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_FormulaIngredients_Formulas_FormulaId] FOREIGN KEY ([FormulaId]) REFERENCES [fg].[Formulas] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_FormulaIngredients_Materials_MaterialId] FOREIGN KEY ([MaterialId]) REFERENCES [fg].[Materials] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE TABLE [fg].[InventoryTransactions] (
        [Id] int NOT NULL IDENTITY,
        [MaterialId] int NOT NULL,
        [LocationId] int NULL,
        [BatchNumber] nvarchar(400) NULL,
        [Quantity] decimal(18,4) NOT NULL,
        [Reason] nvarchar(400) NULL,
        [CustomerName] nvarchar(400) NULL,
        [FormulaId] int NULL,
        [CreatedBy] int NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [GroupId] int NOT NULL,
        CONSTRAINT [PK_InventoryTransactions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_InventoryTransactions_Groups_GroupId] FOREIGN KEY ([GroupId]) REFERENCES [fg].[Groups] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_InventoryTransactions_MaterialLocations_LocationId] FOREIGN KEY ([LocationId]) REFERENCES [fg].[MaterialLocations] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_InventoryTransactions_Materials_MaterialId] FOREIGN KEY ([MaterialId]) REFERENCES [fg].[Materials] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE TABLE [fg].[MessageRecipients] (
        [Id] int NOT NULL IDENTITY,
        [MessageId] int NOT NULL,
        [UserId] int NOT NULL,
        [Viewed] bit NOT NULL,
        CONSTRAINT [PK_MessageRecipients] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_MessageRecipients_Messages_MessageId] FOREIGN KEY ([MessageId]) REFERENCES [fg].[Messages] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_MessageRecipients_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [fg].[Users] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE TABLE [fg].[PurchaseOrderLines] (
        [Id] int NOT NULL IDENTITY,
        [PurchaseOrderId] int NOT NULL,
        [MaterialId] int NOT NULL,
        [Quantity] decimal(18,4) NOT NULL,
        [QuantityType] nvarchar(400) NULL,
        [UnitPrice] decimal(18,4) NOT NULL,
        CONSTRAINT [PK_PurchaseOrderLines] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_PurchaseOrderLines_Materials_MaterialId] FOREIGN KEY ([MaterialId]) REFERENCES [fg].[Materials] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_PurchaseOrderLines_PurchaseOrders_PurchaseOrderId] FOREIGN KEY ([PurchaseOrderId]) REFERENCES [fg].[PurchaseOrders] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE TABLE [fg].[WorkInstructionMedia] (
        [Id] int NOT NULL IDENTITY,
        [StepId] int NOT NULL,
        [FileName] nvarchar(400) NOT NULL,
        [StoredFile] nvarchar(400) NOT NULL,
        [ContentType] nvarchar(400) NOT NULL,
        CONSTRAINT [PK_WorkInstructionMedia] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_WorkInstructionMedia_WorkInstructionSteps_StepId] FOREIGN KEY ([StepId]) REFERENCES [fg].[WorkInstructionSteps] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE TABLE [fg].[ProcessStepValues] (
        [Id] int NOT NULL IDENTITY,
        [EntryId] int NOT NULL,
        [CharacteristicId] int NOT NULL,
        [Value] nvarchar(4000) NULL,
        [MaterialId] int NULL,
        CONSTRAINT [PK_ProcessStepValues] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ProcessStepValues_Characteristics_CharacteristicId] FOREIGN KEY ([CharacteristicId]) REFERENCES [fg].[Characteristics] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_ProcessStepValues_Materials_MaterialId] FOREIGN KEY ([MaterialId]) REFERENCES [fg].[Materials] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_ProcessStepValues_ProcessStepEntries_EntryId] FOREIGN KEY ([EntryId]) REFERENCES [fg].[ProcessStepEntries] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE TABLE [fg].[ScheduleStepOverrides] (
        [Id] int NOT NULL IDENTITY,
        [ScheduleStepId] int NOT NULL,
        [ProcessStepValueId] int NOT NULL,
        [Value] nvarchar(4000) NULL,
        [MinValue] decimal(18,4) NULL,
        [MaxValue] decimal(18,4) NULL,
        CONSTRAINT [PK_ScheduleStepOverrides] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ScheduleStepOverrides_ProcessScheduleSteps_ScheduleStepId] FOREIGN KEY ([ScheduleStepId]) REFERENCES [fg].[ProcessScheduleSteps] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE TABLE [fg].[WorkExecutionLines] (
        [Id] int NOT NULL IDENTITY,
        [ExecutionId] int NOT NULL,
        [StepNumber] int NOT NULL,
        [StepName] nvarchar(400) NOT NULL,
        [Sequence] int NOT NULL,
        [Description] nvarchar(4000) NOT NULL,
        [Value] nvarchar(4000) NULL,
        [Unit] nvarchar(400) NULL,
        [MinValue] decimal(18,4) NULL,
        [MaxValue] decimal(18,4) NULL,
        CONSTRAINT [PK_WorkExecutionLines] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_WorkExecutionLines_WorkExecutions_ExecutionId] FOREIGN KEY ([ExecutionId]) REFERENCES [fg].[WorkExecutions] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE TABLE [fg].[WorkLineChecks] (
        [Id] int NOT NULL IDENTITY,
        [LineId] int NOT NULL,
        [UserId] int NOT NULL,
        [UserName] nvarchar(400) NULL,
        [RecordedValue] nvarchar(400) NULL,
        [CheckedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_WorkLineChecks] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_WorkLineChecks_WorkExecutionLines_LineId] FOREIGN KEY ([LineId]) REFERENCES [fg].[WorkExecutionLines] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AdderTypes_GroupId] ON [fg].[AdderTypes] ([GroupId]);
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_AppSettings_Key] ON [fg].[AppSettings] ([Key]);
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AuditLogs_EntityType_EntityId] ON [fg].[AuditLogs] ([EntityType], [EntityId]);
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Characteristics_CategoryId] ON [fg].[Characteristics] ([CategoryId]);
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_DefectTypes_GroupId] ON [fg].[DefectTypes] ([GroupId]);
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Departments_GroupId] ON [fg].[Departments] ([GroupId]);
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_DeviceCanisters_DeviceId] ON [fg].[DeviceCanisters] ([DeviceId]);
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_DeviceMetrics_DeviceId_Timestamp] ON [fg].[DeviceMetrics] ([DeviceId], [Timestamp]);
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Devices_ApiKey] ON [fg].[Devices] ([ApiKey]);
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Devices_GroupId] ON [fg].[Devices] ([GroupId]);
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_DocumentLinks_DocumentId] ON [fg].[DocumentLinks] ([DocumentId]);
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_DocumentLinks_EntityType_EntityId] ON [fg].[DocumentLinks] ([EntityType], [EntityId]);
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Documents_GroupId] ON [fg].[Documents] ([GroupId]);
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_FormulaIngredients_FormulaId] ON [fg].[FormulaIngredients] ([FormulaId]);
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_FormulaIngredients_MaterialId] ON [fg].[FormulaIngredients] ([MaterialId]);
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Formulas_CategoryId] ON [fg].[Formulas] ([CategoryId]);
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Formulas_GroupId] ON [fg].[Formulas] ([GroupId]);
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Groups_Name] ON [fg].[Groups] ([Name]);
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_InventoryTransactions_GroupId] ON [fg].[InventoryTransactions] ([GroupId]);
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_InventoryTransactions_LocationId] ON [fg].[InventoryTransactions] ([LocationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_InventoryTransactions_MaterialId] ON [fg].[InventoryTransactions] ([MaterialId]);
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_MaterialCategories_GroupId] ON [fg].[MaterialCategories] ([GroupId]);
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_MaterialLocations_GroupId] ON [fg].[MaterialLocations] ([GroupId]);
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Materials_CategoryId] ON [fg].[Materials] ([CategoryId]);
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Materials_GroupId_MaterialType_IsDeleted] ON [fg].[Materials] ([GroupId], [MaterialType], [IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_MessageRecipients_MessageId] ON [fg].[MessageRecipients] ([MessageId]);
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_MessageRecipients_UserId_Viewed] ON [fg].[MessageRecipients] ([UserId], [Viewed]);
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Messages_FromUserId] ON [fg].[Messages] ([FromUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Photos_GroupId] ON [fg].[Photos] ([GroupId]);
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_PhotoTags_PhotoId] ON [fg].[PhotoTags] ([PhotoId]);
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ProcessSchedules_DepartmentId] ON [fg].[ProcessSchedules] ([DepartmentId]);
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ProcessSchedules_GroupId_IsArchived] ON [fg].[ProcessSchedules] ([GroupId], [IsArchived]);
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ProcessScheduleSteps_ProcessStepId] ON [fg].[ProcessScheduleSteps] ([ProcessStepId]);
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ProcessScheduleSteps_ScheduleId] ON [fg].[ProcessScheduleSteps] ([ScheduleId]);
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ProcessStepEntries_CategoryId] ON [fg].[ProcessStepEntries] ([CategoryId]);
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ProcessStepEntries_ProcessStepId] ON [fg].[ProcessStepEntries] ([ProcessStepId]);
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ProcessStepEntries_SubStepId] ON [fg].[ProcessStepEntries] ([SubStepId]);
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ProcessSteps_GroupId_IsDeleted] ON [fg].[ProcessSteps] ([GroupId], [IsDeleted]);
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ProcessSteps_IndustrySectorId] ON [fg].[ProcessSteps] ([IndustrySectorId]);
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ProcessStepValues_CharacteristicId] ON [fg].[ProcessStepValues] ([CharacteristicId]);
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ProcessStepValues_EntryId] ON [fg].[ProcessStepValues] ([EntryId]);
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ProcessStepValues_MaterialId] ON [fg].[ProcessStepValues] ([MaterialId]);
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_PurchaseOrderLines_MaterialId] ON [fg].[PurchaseOrderLines] ([MaterialId]);
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_PurchaseOrderLines_PurchaseOrderId] ON [fg].[PurchaseOrderLines] ([PurchaseOrderId]);
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_PurchaseOrders_GroupId] ON [fg].[PurchaseOrders] ([GroupId]);
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_PurchaseOrders_VendorId] ON [fg].[PurchaseOrders] ([VendorId]);
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ScheduleStepOverrides_ScheduleStepId] ON [fg].[ScheduleStepOverrides] ([ScheduleStepId]);
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_SubStepPullDowns_SubStepId] ON [fg].[SubStepPullDowns] ([SubStepId]);
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_SubSteps_IndustrySectorId_Sequence] ON [fg].[SubSteps] ([IndustrySectorId], [Sequence]);
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_UserRoles_UserId] ON [fg].[UserRoles] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Users_Email] ON [fg].[Users] ([Email]);
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Users_GroupId] ON [fg].[Users] ([GroupId]);
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Users_Username] ON [fg].[Users] ([Username]);
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Vendors_GroupId] ON [fg].[Vendors] ([GroupId]);
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_WorkExecutionAdders_AdderTypeId] ON [fg].[WorkExecutionAdders] ([AdderTypeId]);
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_WorkExecutionDefects_DefectTypeId] ON [fg].[WorkExecutionDefects] ([DefectTypeId]);
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_WorkExecutionLines_ExecutionId] ON [fg].[WorkExecutionLines] ([ExecutionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_WorkExecutions_GroupId_Status] ON [fg].[WorkExecutions] ([GroupId], [Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_WorkExecutions_ScheduleId] ON [fg].[WorkExecutions] ([ScheduleId]);
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_WorkExecutions_UserId] ON [fg].[WorkExecutions] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_WorkInstructionItems_WorkInstructionId] ON [fg].[WorkInstructionItems] ([WorkInstructionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_WorkInstructionMedia_StepId] ON [fg].[WorkInstructionMedia] ([StepId]);
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_WorkInstructionRelatedDocs_WorkInstructionId] ON [fg].[WorkInstructionRelatedDocs] ([WorkInstructionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_WorkInstructions_GroupId] ON [fg].[WorkInstructions] ([GroupId]);
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_WorkInstructionSignatures_WorkInstructionId] ON [fg].[WorkInstructionSignatures] ([WorkInstructionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_WorkInstructionSteps_WorkInstructionId] ON [fg].[WorkInstructionSteps] ([WorkInstructionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_WorkInstructionTrails_WorkInstructionId] ON [fg].[WorkInstructionTrails] ([WorkInstructionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_WorkLineChecks_LineId] ON [fg].[WorkLineChecks] ([LineId]);
END;

IF NOT EXISTS (
    SELECT * FROM [fg].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260910193128_InitialCreate'
)
BEGIN
    INSERT INTO [fg].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260910193128_InitialCreate', N'10.0.12');
END;

COMMIT;
GO

