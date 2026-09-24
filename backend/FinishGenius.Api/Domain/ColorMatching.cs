namespace FinishGenius.Api.Domain;

/// <summary>Where a colour reading came from — device readings are the ones a prediction can be trusted on.</summary>
public enum ColorSource
{
    /// <summary>Read on a spectrophotometer and typed in.</summary>
    Device = 1,
    /// <summary>Taken from a photograph (calibrated against a grey/white card in the shot).</summary>
    Photo = 2,
    /// <summary>Entered by hand from somewhere else (a supplier sheet, an old record).</summary>
    Manual = 3,
}

/// <summary>How the stain was put on: the same formula gives a different colour sprayed than wiped.</summary>
public enum ApplicationMethod
{
    Spray = 1,
    Wipe = 2,
    Brush = 3,
    Dip = 4,
    Roll = 5,
    Other = 9,
}

/// <summary>
/// One finished physical sample: this wood, prepared this way, with this formula at this strength, applied and topcoated
/// like this, measured to this colour. It is the record the whole colour-matching feature stands on — a match, a
/// prediction and a preview are only ever as good as the samples behind them, so every condition that changes the
/// result (species, grit, coats, method, topcoat) is part of the record, as the requirements describe.
/// </summary>
public class ColorSample : GroupOwned
{
    public string Name { get; set; } = "";

    // ---- the wood
    public string WoodSpecies { get; set; } = "";
    /// <summary>Sanding grit the piece was prepared to (180, 220 …).</summary>
    public int? SandingGrit { get; set; }
    /// <summary>The unfinished wood's own colour, when it was measured before staining.</summary>
    public double? WoodL { get; set; }
    public double? WoodA { get; set; }
    public double? WoodB { get; set; }
    /// <summary>Flat sawn, rift, quartered — the face decides how much stain the grain takes.</summary>
    public string? GrainDirection { get; set; }
    /// <summary>Open, medium or closed pore.</summary>
    public string? Porosity { get; set; }
    /// <summary>Tight, medium or wide growth rings.</summary>
    public string? GrowthRings { get; set; }
    /// <summary>What was already on the wood, if anything (an old finish changes everything).</summary>
    public string? ExistingFinish { get; set; }
    /// <summary>Moisture content in percent, where a meter was used.</summary>
    public double? MoisturePercent { get; set; }

    // ---- the stain
    /// <summary>The formula used, when it is one of this group's formulas.</summary>
    public int? FormulaId { get; set; }
    /// <summary>Kept as text as well, so a sample still says what was on it if the formula is renamed or removed.</summary>
    public string FormulaName { get; set; } = "";
    /// <summary>Strength the stain was used at, in percent.</summary>
    public double? Concentration { get; set; }

    // ---- how it was applied
    public ApplicationMethod? Method { get; set; }
    public int? Coats { get; set; }
    /// <summary>Wet film thickness in mils.</summary>
    public double? WetFilmMils { get; set; }
    public int? FlashMinutes { get; set; }
    /// <summary>The gun used, when sprayed (e.g. "Kremlin").</summary>
    public string? SprayGun { get; set; }
    /// <summary>Gun pressure in psi.</summary>
    public double? SprayPressurePsi { get; set; }
    /// <summary>How it dried: temperature, humidity, time — as the shop recorded it.</summary>
    public string? DryingConditions { get; set; }
    public string? Sealer { get; set; }
    public string? Topcoat { get; set; }
    /// <summary>Sheen of the topcoat, in percent.</summary>
    public double? Sheen { get; set; }

    // ---- the result
    public double FinalL { get; set; }
    public double FinalA { get; set; }
    public double FinalB { get; set; }
    public ColorSource Source { get; set; } = ColorSource.Device;
    public DateTime? MeasuredAt { get; set; }
    /// <summary>Photograph of the finished sample (<c>color-samples/{groupId}/…</c>).</summary>
    public string? PhotoFile { get; set; }
    public string? Notes { get; set; }
    /// <summary>
    /// What was actually in the formula when this sample was made, as JSON: each colorant with its grams and its
    /// share of the batch. The requirements' training table is per colorant (Dye A %, Dye B %, Pigment %), and a
    /// formula gets edited over the years — so the recipe is copied onto the sample instead of being looked up later.
    /// </summary>
    public string? ColorantsJson { get; set; }

    public int? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public bool IsDeleted { get; set; }
}
