namespace FinishGenius.Api.Domain;

/// <summary>Numeric values match the legacy FGAPP MaterialType column so data can be migrated 1:1.</summary>
public enum MaterialType
{
    Base = 1,
    Pigment = 2,
    Dye = 3,
    Equipment = 4,
    Sundry = 5,
    Formula = 6,
    Product = 7,
}

public class MaterialCategory
{
    public int Id { get; set; }
    /// <summary>null = shared library category available to every group (all legacy categories are shared).</summary>
    public int? GroupId { get; set; }
    public Group? Group { get; set; }
    public string Name { get; set; } = "";
    public MaterialType MaterialType { get; set; }
    /// <summary>Legacy filter columns: pull-downs select categories by MaterialType + Filter2 (e.g. "GUNS").</summary>
    public string? Filter1 { get; set; }
    public string? Filter2 { get; set; }
    public List<Characteristic> Characteristics { get; set; } = [];
}

public static class CharacteristicInputTypes
{
    public const string Text = "Text";
    public const string Number = "Number";
    public const string Material = "Material";   // pick a material from this category
    public const string YesNo = "YesNo";
    public const string Notes = "Notes";
    public static readonly string[] All = [Text, Number, Material, YesNo, Notes];
}

/// <summary>Calculation roles a characteristic value can play in Material Quantities and Pricing.</summary>
public static class CalcVariables
{
    public const string None = "";
    public const string MaterialId = "Material_ID";
    public const string Coverage = "Coverage";            // sq ft per gallon
    public const string MixPercent = "MixPercent";        // % of the mix this material represents
    public const string ProductionRate = "ProductionRate"; // sq ft per hour
    public const string CostPerSqFt = "CostPerSqFt";      // $ per sq ft (process costing sub step)

    // Legacy Finish Genius tags (imported data): Material_ID + Material_Coverage (+ Material_Qty) is the base coating,
    // MiscN_ID + MiscN_Qty pairs are additives (fl oz / gal of base) or consumables (sq ft per piece),
    // Gun_TE is the spray gun transfer efficiency (%), Step_Labor / Step_Setup are minutes per sq ft.
    public const string MaterialCoverage = "Material_Coverage";
    public const string MaterialQty = "Material_Qty";
    public const string GunTransferEfficiency = "Gun_TE";
    public const string StepLabor = "Step_Labor";
    public const string StepSetup = "Step_Setup";
    public static readonly string[] Legacy =
        [MaterialCoverage, MaterialQty, GunTransferEfficiency, StepLabor, StepSetup, .. Enumerable.Range(1, 7).SelectMany(n => new[] { $"Misc{n}_ID", $"Misc{n}_Qty" })];

    public static readonly string[] All = [None, MaterialId, Coverage, MixPercent, ProductionRate, CostPerSqFt, .. Legacy];
}

/// <summary>An input shown when a category is chosen inside a process sub step (legacy "Characteristics").</summary>
public class Characteristic
{
    public int Id { get; set; }
    public int CategoryId { get; set; }
    public string Name { get; set; } = "";
    public string? Unit { get; set; }
    public string InputType { get; set; } = CharacteristicInputTypes.Text;
    public string? CalcVariable { get; set; }
    public string? DefaultValue { get; set; }
    public int Sequence { get; set; }
}

public class Material : GroupOwned
{
    public MaterialType MaterialType { get; set; }
    public int? CategoryId { get; set; }
    public MaterialCategory? Category { get; set; }
    public string? ProductCode { get; set; }
    public string ProductName { get; set; } = "";
    /// <summary>lb/gal</summary>
    public decimal Density { get; set; }
    /// <summary>$ per gallon (or per piece for equipment/sundries)</summary>
    public decimal Price { get; set; }
    public decimal Voc { get; set; }
    public decimal Hap { get; set; }
    public decimal Tap { get; set; }
    public decimal MinQuantity { get; set; }
    public int? VendorId { get; set; }
    public string? Notes { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}

public class MaterialLocation : GroupOwned
{
    public string Name { get; set; } = "";
    public MaterialType MaterialType { get; set; }
    public bool IsDeleted { get; set; }
}

/// <summary>Every inventory movement. On-hand = SUM(Quantity). Negative quantities are consumption.</summary>
public class InventoryTransaction : GroupOwned
{
    public int MaterialId { get; set; }
    public Material? Material { get; set; }
    public int? LocationId { get; set; }
    public MaterialLocation? Location { get; set; }
    public string? BatchNumber { get; set; }
    public decimal Quantity { get; set; }
    public string? Reason { get; set; }
    public string? CustomerName { get; set; }
    public int? FormulaId { get; set; }
    public int CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Vendor : GroupOwned
{
    public string VendorName { get; set; } = "";
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Zip { get; set; }
    public string? Country { get; set; }
    public string? PaymentTerms { get; set; }
    public string? AccountNumber { get; set; }
    public string? ContactName { get; set; }
    public string? OfficePhone { get; set; }
    public string? MobilePhone { get; set; }
    public string? VendorEmail { get; set; }
    public string? RequestorEmail { get; set; }
    public bool IsDeleted { get; set; }
}

public class PurchaseOrder : GroupOwned
{
    public int VendorId { get; set; }
    public Vendor? Vendor { get; set; }
    public string PoNumber { get; set; } = "";
    public DateTime? DeliveryDate { get; set; }
    public string? ShipName { get; set; }
    public string? ShipAddress1 { get; set; }
    public string? ShipAddress2 { get; set; }
    public string? ShipCity { get; set; }
    public string? ShipState { get; set; }
    public string? ShipZip { get; set; }
    public string? ShipCountry { get; set; }
    public int CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<PurchaseOrderLine> Lines { get; set; } = [];
}

public class PurchaseOrderLine
{
    public int Id { get; set; }
    public int PurchaseOrderId { get; set; }
    public int MaterialId { get; set; }
    public Material? Material { get; set; }
    public decimal Quantity { get; set; }
    public string? QuantityType { get; set; }
    public decimal UnitPrice { get; set; }
}

public class Formula : GroupOwned
{
    public int? CategoryId { get; set; }
    public MaterialCategory? Category { get; set; }
    public string Name { get; set; } = "";
    public string? Number { get; set; }
    public string? CustomerName { get; set; }
    public bool IsComplete { get; set; }
    /// <summary>Batch size in grams used by the ingredient list.</summary>
    public decimal BatchSize { get; set; }
    public string? ContainerType { get; set; }
    public decimal ContainerPrice { get; set; }
    public decimal MarkUp { get; set; }
    public string? Substrate { get; set; }
    public string? Notes { get; set; }
    public decimal? SpinDeltaL { get; set; }
    public decimal? SpinDeltaA { get; set; }
    public decimal? SpinDeltaB { get; set; }
    public decimal? SpinDeltaE { get; set; }
    public decimal? SpexDeltaL { get; set; }
    public decimal? SpexDeltaA { get; set; }
    public decimal? SpexDeltaB { get; set; }
    public decimal? SpexDeltaE { get; set; }
    public int? CreatedBy { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public List<FormulaIngredient> Ingredients { get; set; } = [];
}

public class FormulaIngredient
{
    public int Id { get; set; }
    public int FormulaId { get; set; }
    public int MaterialId { get; set; }
    public Material? Material { get; set; }
    public decimal Grams { get; set; }
    public decimal DispensedGrams { get; set; }
    public bool IsDispensed { get; set; }
    public int Sequence { get; set; }
}

public static class LinkEntityTypes
{
    public const string Material = "Material";
    public const string Formula = "Formula";
    public const string ProcessStep = "ProcessStep";
    public const string ProcessSchedule = "ProcessSchedule";
    public const string Pricing = "Pricing";
    public const string MaterialQuantity = "MaterialQuantity";
    public static readonly string[] All = [Material, Formula, ProcessStep, ProcessSchedule, Pricing, MaterialQuantity];
}

/// <summary>Central document library. A document is uploaded once and linked to many objects.</summary>
public class Document : GroupOwned
{
    public string Name { get; set; } = "";
    public string? FileName { get; set; }
    public string? StoredFile { get; set; }
    public string? ContentType { get; set; }
    public long FileSize { get; set; }
    public int? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public List<DocumentLink> Links { get; set; } = [];
}

public class DocumentLink
{
    public int Id { get; set; }
    public int DocumentId { get; set; }
    public string EntityType { get; set; } = "";
    public int EntityId { get; set; }
}

public class Photo : GroupOwned
{
    public string Name { get; set; } = "";
    public string StoredFile { get; set; } = "";
    public string? ContentType { get; set; }
    public int? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<PhotoTag> Tags { get; set; } = [];
}

public class PhotoTag
{
    public int Id { get; set; }
    public int PhotoId { get; set; }
    public string Tag { get; set; } = "";
}

public enum DeviceType
{
    Camera = 1,
    ScaleGrams = 2,
    TempHumiditySensor = 3,
    ScaleKilograms = 4,
    LabelPrinter = 5,
    NetworkBridge = 6,
    DispenseMachine = 7,
}

public class Device : GroupOwned
{
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public DeviceType DeviceType { get; set; }
    public int? NetworkBridgeId { get; set; }
    public string? IpAddress { get; set; }
    public Guid ApiKey { get; set; } = Guid.NewGuid();
    public bool IsArchived { get; set; }
    public DateTime? LastSeenAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<DeviceCanister> Canisters { get; set; } = [];
}

public class DeviceCanister
{
    public int Id { get; set; }
    public int DeviceId { get; set; }
    public int CanisterNo { get; set; }
    public int? MaterialId { get; set; }
}

public class DeviceMetric
{
    public long Id { get; set; }
    public int DeviceId { get; set; }
    public string Name { get; set; } = "";
    public string? Unit { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public double? Value { get; set; }
    public string? TextValue { get; set; }
}
