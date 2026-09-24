using FinishGenius.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace FinishGenius.Api.Data;

/// <summary>
/// All Finish Genius tables live in the "fg" schema so they never collide with other tables that
/// already exist in the shared database (e.g. dbo.Users).
/// </summary>
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public const string Schema = "fg";

    public DbSet<Group> Groups => Set<Group>();
    public DbSet<User> Users => Set<User>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<UserGroup> UserGroups => Set<UserGroup>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<MessageRecipient> MessageRecipients => Set<MessageRecipient>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();

    public DbSet<MaterialCategory> MaterialCategories => Set<MaterialCategory>();
    public DbSet<Characteristic> Characteristics => Set<Characteristic>();
    public DbSet<Material> Materials => Set<Material>();
    public DbSet<MaterialLocation> MaterialLocations => Set<MaterialLocation>();
    public DbSet<InventoryTransaction> InventoryTransactions => Set<InventoryTransaction>();
    public DbSet<Vendor> Vendors => Set<Vendor>();
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
    public DbSet<PurchaseOrderLine> PurchaseOrderLines => Set<PurchaseOrderLine>();
    public DbSet<Formula> Formulas => Set<Formula>();
    public DbSet<FormulaIngredient> FormulaIngredients => Set<FormulaIngredient>();
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<DocumentLink> DocumentLinks => Set<DocumentLink>();
    public DbSet<Photo> Photos => Set<Photo>();
    public DbSet<PhotoTag> PhotoTags => Set<PhotoTag>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<DeviceCanister> DeviceCanisters => Set<DeviceCanister>();
    public DbSet<DeviceMetric> DeviceMetrics => Set<DeviceMetric>();
    public DbSet<DeviceCommand> DeviceCommands => Set<DeviceCommand>();
    public DbSet<FormulaDevicePreference> FormulaDevicePreferences => Set<FormulaDevicePreference>();
    public DbSet<FormulaDispenseSnapshot> FormulaDispenseSnapshots => Set<FormulaDispenseSnapshot>();
    public DbSet<DispenseSetting> DispenseSettings => Set<DispenseSetting>();
    public DbSet<PurgeSetting> PurgeSettings => Set<PurgeSetting>();
    public DbSet<PurgeFailure> PurgeFailures => Set<PurgeFailure>();
    public DbSet<PurgeSuccess> PurgeSuccesses => Set<PurgeSuccess>();

    public DbSet<ColorSample> ColorSamples => Set<ColorSample>();

    public DbSet<IndustrySector> IndustrySectors => Set<IndustrySector>();
    public DbSet<SubStep> SubSteps => Set<SubStep>();
    public DbSet<SubStepPullDown> SubStepPullDowns => Set<SubStepPullDown>();
    public DbSet<ProcessStep> ProcessSteps => Set<ProcessStep>();
    public DbSet<ProcessStepEntry> ProcessStepEntries => Set<ProcessStepEntry>();
    public DbSet<ProcessStepValue> ProcessStepValues => Set<ProcessStepValue>();
    public DbSet<ProcessSchedule> ProcessSchedules => Set<ProcessSchedule>();
    public DbSet<ProcessScheduleStep> ProcessScheduleSteps => Set<ProcessScheduleStep>();
    public DbSet<ScheduleStepOverride> ScheduleStepOverrides => Set<ScheduleStepOverride>();

    public DbSet<WorkExecution> WorkExecutions => Set<WorkExecution>();
    public DbSet<WorkExecutionLine> WorkExecutionLines => Set<WorkExecutionLine>();
    public DbSet<WorkLineCheck> WorkLineChecks => Set<WorkLineCheck>();
    public DbSet<DefectType> DefectTypes => Set<DefectType>();
    public DbSet<AdderType> AdderTypes => Set<AdderType>();
    public DbSet<WorkExecutionDefect> WorkExecutionDefects => Set<WorkExecutionDefect>();
    public DbSet<WorkExecutionAdder> WorkExecutionAdders => Set<WorkExecutionAdder>();

    public DbSet<WorkInstruction> WorkInstructions => Set<WorkInstruction>();
    public DbSet<WorkInstructionStep> WorkInstructionSteps => Set<WorkInstructionStep>();
    public DbSet<WorkInstructionMedia> WorkInstructionMedia => Set<WorkInstructionMedia>();
    public DbSet<WorkInstructionTrail> WorkInstructionTrails => Set<WorkInstructionTrail>();
    public DbSet<WorkInstructionRelatedDoc> WorkInstructionRelatedDocs => Set<WorkInstructionRelatedDoc>();
    public DbSet<WorkInstructionSignature> WorkInstructionSignatures => Set<WorkInstructionSignature>();
    public DbSet<WorkInstructionItem> WorkInstructionItems => Set<WorkInstructionItem>();

    protected override void ConfigureConventions(ModelConfigurationBuilder b)
    {
        b.Properties<decimal>().HavePrecision(18, 4);
        b.Properties<decimal?>().HavePrecision(18, 4);
        b.Properties<string>().HaveMaxLength(400);
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema(Schema);

        // SQL Server rejects multiple cascade paths, so default to NO ACTION and opt children in below.
        foreach (var fk in b.Model.GetEntityTypes().SelectMany(e => e.GetForeignKeys()))
            fk.DeleteBehavior = DeleteBehavior.Restrict;

        // Long text columns
        b.Entity<SubStep>().Property(x => x.Instruction).HasMaxLength(4000);
        b.Entity<Material>().Property(x => x.Notes).HasMaxLength(4000);
        b.Entity<Formula>().Property(x => x.Notes).HasMaxLength(4000);
        b.Entity<AuditLog>().Property(x => x.Details).HasMaxLength(4000);
        b.Entity<Message>().Property(x => x.Body).HasMaxLength(int.MaxValue);
        b.Entity<AppSetting>().Property(x => x.Value).HasMaxLength(int.MaxValue);
        b.Entity<ProcessStepValue>().Property(x => x.Value).HasMaxLength(4000);
        b.Entity<ScheduleStepOverride>().Property(x => x.Value).HasMaxLength(4000);
        b.Entity<WorkExecutionLine>().Property(x => x.Description).HasMaxLength(4000);
        b.Entity<WorkExecutionLine>().Property(x => x.Value).HasMaxLength(4000);
        b.Entity<WorkExecution>().Property(x => x.Notes).HasMaxLength(4000);
        b.Entity<WorkInstructionStep>().Property(x => x.Title).HasMaxLength(4000);
        b.Entity<WorkInstructionStep>().Property(x => x.Body).HasMaxLength(int.MaxValue);
        b.Entity<WorkInstructionTrail>().Property(x => x.Log).HasMaxLength(4000);
        foreach (var p in new[] { "Purpose", "Scope", "Terminology", "Location" })
            b.Entity<WorkInstruction>().Property(p).HasMaxLength(4000);

        b.Entity<ColorSample>().Property(x => x.Notes).HasMaxLength(4000);
        b.Entity<ColorSample>().Property(x => x.ColorantsJson).HasMaxLength(int.MaxValue);
        b.Entity<ColorSample>().HasIndex(x => new { x.GroupId, x.WoodSpecies });
        b.Entity<ColorSample>().HasIndex(x => x.FormulaId);

        b.Entity<Group>().HasIndex(x => x.Name);
        b.Entity<User>().HasIndex(x => x.Username);
        b.Entity<User>().HasIndex(x => x.Email);
        b.Entity<User>().HasMany(x => x.Roles).WithOne().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<User>().HasMany(x => x.ExtraGroups).WithOne().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<UserGroup>().HasKey(x => new { x.UserId, x.GroupId });
        b.Entity<AuditLog>().HasIndex(x => new { x.EntityType, x.EntityId });
        b.Entity<AppSetting>().HasIndex(x => x.Key).IsUnique();

        b.Entity<Message>().HasMany(x => x.Recipients).WithOne().HasForeignKey(x => x.MessageId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<MessageRecipient>().HasIndex(x => new { x.UserId, x.Viewed });

        b.Entity<MaterialCategory>().HasMany(x => x.Characteristics).WithOne().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Material>().HasIndex(x => new { x.GroupId, x.MaterialType, x.IsDeleted });
        b.Entity<InventoryTransaction>().HasIndex(x => new { x.MaterialId });
        b.Entity<InventoryTransaction>().HasIndex(x => new { x.GroupId, x.BatchNumber });
        b.Entity<PurchaseOrder>().HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.PurchaseOrderId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Formula>().HasMany(x => x.Ingredients).WithOne().HasForeignKey(x => x.FormulaId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Formula>().HasOne(x => x.Mirror).WithMany().HasForeignKey(x => x.MaterialId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Document>().HasMany(x => x.Links).WithOne().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<DocumentLink>().HasIndex(x => new { x.EntityType, x.EntityId });
        b.Entity<Photo>().HasMany(x => x.Tags).WithOne().HasForeignKey(x => x.PhotoId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Device>().HasMany(x => x.Canisters).WithOne().HasForeignKey(x => x.DeviceId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Device>().HasIndex(x => x.ApiKey).IsUnique();
        b.Entity<DeviceMetric>().HasIndex(x => new { x.DeviceId, x.Timestamp });

        // Formula workspace: dispensing, batches, device preferences, purge history, bridge commands.
        b.Entity<Formula>().Property(x => x.BatchType).HasDefaultValue(FormulaBatchType.Grams).HasSentinel(FormulaBatchType.Grams);
        b.Entity<DeviceCommand>().Property(x => x.Payload).HasMaxLength(int.MaxValue);
        b.Entity<DeviceCommand>().Property(x => x.ResultData).HasMaxLength(4000);
        b.Entity<DeviceCommand>().Property(x => x.ResultMessage).HasMaxLength(4000);
        b.Entity<DeviceCommand>().HasIndex(x => new { x.BridgeDeviceId, x.Status });
        b.Entity<DeviceCommand>().HasIndex(x => new { x.FormulaId, x.CommandType });
        b.Entity<FormulaDevicePreference>().HasIndex(x => new { x.UserId, x.FormulaId }).IsUnique();
        b.Entity<FormulaDispenseSnapshot>().HasIndex(x => x.FormulaId);
        b.Entity<DispenseSetting>().HasIndex(x => x.GroupId).IsUnique();
        b.Entity<PurgeSetting>().HasIndex(x => x.BridgeDeviceId).IsUnique();
        b.Entity<PurgeFailure>().HasIndex(x => new { x.BridgeDeviceId, x.IsActive });
        b.Entity<PurgeSuccess>().HasIndex(x => new { x.BridgeDeviceId, x.ExecutedAt });

        b.Entity<SubStep>().HasMany(x => x.PullDowns).WithOne().HasForeignKey(x => x.SubStepId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<SubStep>().HasIndex(x => new { x.IndustrySectorId, x.Sequence }).IsUnique();
        b.Entity<ProcessStep>().HasMany(x => x.Entries).WithOne().HasForeignKey(x => x.ProcessStepId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<ProcessStep>().HasIndex(x => new { x.GroupId, x.IsDeleted });
        b.Entity<ProcessStepEntry>().HasMany(x => x.Values).WithOne().HasForeignKey(x => x.EntryId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<ProcessSchedule>().HasMany(x => x.Steps).WithOne().HasForeignKey(x => x.ScheduleId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<ProcessSchedule>().HasIndex(x => new { x.GroupId, x.IsArchived });
        b.Entity<ProcessScheduleStep>().HasMany(x => x.Overrides).WithOne().HasForeignKey(x => x.ScheduleStepId).OnDelete(DeleteBehavior.Cascade);
        // Schedule-level edits die with the step value they override; entries lose a deleted pull down.
        b.Entity<ScheduleStepOverride>().HasOne<ProcessStepValue>().WithMany().HasForeignKey(x => x.ProcessStepValueId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<ProcessStepEntry>().HasOne<SubStepPullDown>().WithMany().HasForeignKey(x => x.PullDownId).OnDelete(DeleteBehavior.SetNull);
        // Not unique: imported legacy schedules reuse numbers; the API enforces uniqueness for new/changed numbers.
        b.Entity<ProcessSchedule>().HasIndex(x => new { x.GroupId, x.Number });

        b.Entity<WorkExecution>().HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.ExecutionId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<WorkExecution>().HasIndex(x => new { x.GroupId, x.Status });
        b.Entity<WorkExecutionLine>().HasMany(x => x.Checks).WithOne().HasForeignKey(x => x.LineId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<WorkInstruction>().HasMany(x => x.Steps).WithOne().HasForeignKey(x => x.WorkInstructionId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<WorkInstruction>().HasMany(x => x.Trail).WithOne().HasForeignKey(x => x.WorkInstructionId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<WorkInstruction>().HasMany(x => x.RelatedDocuments).WithOne().HasForeignKey(x => x.WorkInstructionId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<WorkInstruction>().HasMany(x => x.Signatures).WithOne().HasForeignKey(x => x.WorkInstructionId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<WorkInstruction>().HasMany(x => x.Items).WithOne().HasForeignKey(x => x.WorkInstructionId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<WorkInstructionStep>().HasMany(x => x.Media).WithOne().HasForeignKey(x => x.StepId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<WorkInstruction>().Ignore(x => x.StatusText);
        b.Entity<User>().Ignore(x => x.FullName);
    }
}
