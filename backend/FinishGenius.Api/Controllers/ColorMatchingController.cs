using FinishGenius.Api.Data;
using FinishGenius.Api.Domain;
using FinishGenius.Api.Infrastructure;
using FinishGenius.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinishGenius.Api.Controllers;

/// <summary>
/// Colour matching: the library of measured samples, the search for a formula that lands on a wanted colour, what a
/// formula is expected to do on a given board, and a picture of that prediction on the customer's own wood.
/// </summary>
[ApiController]
[Authorize(Roles = Access.ColorMatching)]
[Route("api/color-matching")]
public class ColorMatchingController(
    AppDbContext db, CurrentUser me, AuditService audit, FileStorage files,
    ColorMatchService matcher, PhotoColorService photos) : ControllerBase
{
    private const string Entity = "ColorSample";

    public record SampleInput(
        int GroupId, string? Name, string? WoodSpecies, int? SandingGrit, double? WoodL, double? WoodA, double? WoodB,
        int? FormulaId, string? FormulaName, double? Concentration, ApplicationMethod? Method, int? Coats,
        double? WetFilmMils, int? FlashMinutes, string? Sealer, string? Topcoat, double? Sheen,
        double FinalL, double FinalA, double FinalB, ColorSource Source, DateTime? MeasuredAt, string? Notes, string? PhotoFile);

    public record MatchRequest(int GroupId, double L, double A, double B, string? WoodSpecies, string? Topcoat,
        double? WoodL, double? WoodA, double? WoodB, int? Take);

    public record PredictRequest(int GroupId, int? FormulaId, string? FormulaName, double? Concentration,
        string? WoodSpecies, double? WoodL, double? WoodA, double? WoodB);

    // ---------------------------------------------------------------- samples

    /// <summary>GET /api/color-matching/samples?groupId=1&amp;species=Oak — the measured samples of a group.</summary>
    [HttpGet("samples")]
    public async Task<IActionResult> Samples([FromQuery] int groupId, [FromQuery] string? species, [FromQuery] int? formulaId)
    {
        await me.EnsureGroupAsync(groupId);
        var rows = await LoadAsync(groupId, species, formulaId);
        return Ok(rows.Select(Describe));
    }

    /// <summary>GET /api/color-matching/species?groupId=1 — the woods that have been recorded, for the pickers.</summary>
    [HttpGet("species")]
    public async Task<IActionResult> Species([FromQuery] int groupId)
    {
        await me.EnsureGroupAsync(groupId);
        await LegacyTables.EnsureColorSamplesAsync(db);
        var names = await db.ColorSamples.AsNoTracking().Where(s => s.GroupId == groupId && !s.IsDeleted)
            .Select(s => s.WoodSpecies).Distinct().OrderBy(n => n).ToListAsync();
        return Ok(names.Where(n => !string.IsNullOrWhiteSpace(n)));
    }

    [HttpPost("samples")]
    public async Task<IActionResult> Create(SampleInput input)
    {
        await me.EnsureGroupAsync(input.GroupId);
        await LegacyTables.EnsureColorSamplesAsync(db);
        var sample = new ColorSample { GroupId = input.GroupId, CreatedBy = me.Id == 0 ? null : me.Id };
        await ApplyAsync(sample, input);
        db.ColorSamples.Add(sample);
        await db.SaveChangesAsync();
        audit.Log(Entity, sample.Id, "Created", $"{sample.Name} — {sample.WoodSpecies}, {Read(sample)}", sample.GroupId);
        await db.SaveChangesAsync();
        return Ok(new { message = "Colour sample saved.", id = sample.Id });
    }

    [HttpPut("samples/{id:int}")]
    public async Task<IActionResult> Update(int id, SampleInput input)
    {
        await LegacyTables.EnsureColorSamplesAsync(db);
        var sample = await db.ColorSamples.FirstOrDefaultAsync(s => s.Id == id && !s.IsDeleted) ?? throw ApiException.NotFound("Colour sample");
        await me.EnsureGroupAsync(sample.GroupId);
        var before = Read(sample);
        await ApplyAsync(sample, input);
        sample.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        audit.Log(Entity, sample.Id, "Updated", before == Read(sample) ? sample.Name : $"{before} → {Read(sample)}", sample.GroupId);
        await db.SaveChangesAsync();
        return Ok(new { message = "Colour sample saved.", id = sample.Id });
    }

    [HttpDelete("samples/{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        await LegacyTables.EnsureColorSamplesAsync(db);
        var sample = await db.ColorSamples.FirstOrDefaultAsync(s => s.Id == id && !s.IsDeleted) ?? throw ApiException.NotFound("Colour sample");
        await me.EnsureGroupAsync(sample.GroupId);
        sample.IsDeleted = true;
        await db.SaveChangesAsync();
        audit.Log(Entity, sample.Id, "Deleted", sample.Name, sample.GroupId);
        await db.SaveChangesAsync();
        return Ok(new { message = "Colour sample deleted." });
    }

    /// <summary>POST /api/color-matching/photo — stores a photograph of a sample or of bare wood.</summary>
    [HttpPost("photo")]
    public async Task<IActionResult> UploadPhoto([FromForm] int groupId, IFormFile file)
    {
        await me.EnsureGroupAsync(groupId);
        var path = await files.SaveAsync(file, $"color-samples/{groupId}", FileStorage.ImageExtensions);
        return Ok(new { message = "Photo uploaded.", storedFile = path });
    }

    // ---------------------------------------------------------------- reading colour off a photo

    public record ReadPhotoRequest(int GroupId, string StoredFile, PhotoRegion Sample, PhotoRegion? Card, string? CardType);

    /// <summary>POST /api/color-matching/read-photo — the colour inside the marked area, corrected by a grey/white card.</summary>
    [HttpPost("read-photo")]
    public async Task<IActionResult> ReadPhoto(ReadPhotoRequest r)
    {
        await me.EnsureGroupAsync(r.GroupId);
        if (!OperatingSystem.IsWindows()) throw ApiException.Bad("Reading colour from a photo needs the Windows server.");
        var full = files.ResolveExisting(r.StoredFile) ?? throw ApiException.NotFound("Photo");
        var cardL = r.CardType?.Equals("white", StringComparison.OrdinalIgnoreCase) == true ? PhotoColorService.WhiteCardL : PhotoColorService.GreyCardL;
        var reading = photos.Read(full, r.Sample, r.Card, cardL);
        return Ok(new
        {
            l = Math.Round(reading.Lab.L, 2), a = Math.Round(reading.Lab.A, 2), b = Math.Round(reading.Lab.B, 2),
            reading.Hex, reading.Calibrated, correctionDeltaE = Math.Round(reading.CorrectionDeltaE, 2), reading.Note,
        });
    }

    // ---------------------------------------------------------------- matching and prediction

    /// <summary>POST /api/color-matching/match — the recorded formulas that land closest to a wanted colour.</summary>
    [HttpPost("match")]
    public async Task<IActionResult> Match(MatchRequest r)
    {
        await me.EnsureGroupAsync(r.GroupId);
        var target = new Lab(r.L, r.A, r.B);
        var samples = await LoadAsync(r.GroupId, r.WoodSpecies, null);
        if (!string.IsNullOrWhiteSpace(r.Topcoat))
            samples = samples.Where(s => string.Equals(s.Topcoat, r.Topcoat, StringComparison.OrdinalIgnoreCase)).ToList();
        var wood = r.WoodL != null && r.WoodA != null && r.WoodB != null ? new Lab(r.WoodL.Value, r.WoodA.Value, r.WoodB.Value) : (Lab?)null;

        var results = matcher.Match(target, samples, wood, r.Take is > 0 and <= 50 ? r.Take.Value : 10);
        return Ok(new
        {
            target = new { l = r.L, a = r.A, b = r.B, hex = ColorScience.LabToHex(target) },
            sampleCount = samples.Count,
            matches = results.Select(c => new
            {
                sampleId = c.Sample.Id, c.Sample.Name, c.Sample.WoodSpecies, c.Sample.FormulaId, c.Sample.FormulaName,
                recordedConcentration = c.Sample.Concentration, suggestedConcentration = c.SuggestedConcentration,
                method = c.Sample.Method, c.Sample.Coats, c.Sample.Topcoat, c.Sample.Sheen,
                deltaE = Math.Round(c.DeltaE, 2), grade = ColorScience.MatchGrade(c.DeltaE),
                predicted = new { l = Math.Round(c.Predicted.L, 2), a = Math.Round(c.Predicted.A, 2), b = Math.Round(c.Predicted.B, 2), hex = ColorScience.LabToHex(c.Predicted) },
                recorded = new { l = c.Sample.FinalL, a = c.Sample.FinalA, b = c.Sample.FinalB, hex = ColorScience.LabToHex(new Lab(c.Sample.FinalL, c.Sample.FinalA, c.Sample.FinalB)) },
                confidence = Math.Round(c.Confidence * 100),
                c.Basis,
                source = c.Sample.Source,
            }),
        });
    }

    /// <summary>POST /api/color-matching/predict — where a formula is expected to land on this board.</summary>
    [HttpPost("predict")]
    public async Task<IActionResult> Predict(PredictRequest r)
    {
        await me.EnsureGroupAsync(r.GroupId);
        var samples = await LoadAsync(r.GroupId, null, null);
        var wood = r.WoodL != null && r.WoodA != null && r.WoodB != null ? new Lab(r.WoodL.Value, r.WoodA.Value, r.WoodB.Value) : (Lab?)null;
        var p = matcher.Predict(samples, r.FormulaId, r.FormulaName, r.Concentration, r.WoodSpecies, wood);
        if (p.SamplesUsed == 0) return Ok(new { samplesUsed = 0, confidence = 0, p.Basis });
        return Ok(new
        {
            predicted = new { l = Math.Round(p.Predicted.L, 2), a = Math.Round(p.Predicted.A, 2), b = Math.Round(p.Predicted.B, 2), hex = ColorScience.LabToHex(p.Predicted) },
            confidence = Math.Round(p.Confidence * 100), p.Basis, p.SamplesUsed,
        });
    }

    public record PreviewRequest(int GroupId, string StoredFile, PhotoRegion Wood, double L, double A, double B);

    /// <summary>POST /api/color-matching/preview — the photographed wood as it should look with this colour on it.</summary>
    [HttpPost("preview")]
    public async Task<IActionResult> Preview(PreviewRequest r)
    {
        await me.EnsureGroupAsync(r.GroupId);
        if (!OperatingSystem.IsWindows()) throw ApiException.Bad("The stain preview needs the Windows server.");
        var full = files.ResolveExisting(r.StoredFile) ?? throw ApiException.NotFound("Photo");
        var jpeg = photos.Preview(full, r.Wood, new Lab(r.L, r.A, r.B));
        return File(jpeg, "image/jpeg");
    }

    // ---------------------------------------------------------------- helpers

    private async Task<List<ColorSample>> LoadAsync(int groupId, string? species, int? formulaId)
    {
        await LegacyTables.EnsureColorSamplesAsync(db);
        var q = db.ColorSamples.AsNoTracking().Where(s => s.GroupId == groupId && !s.IsDeleted);
        if (!string.IsNullOrWhiteSpace(species)) q = q.Where(s => s.WoodSpecies == species);
        if (formulaId != null) q = q.Where(s => s.FormulaId == formulaId);
        return await q.OrderByDescending(s => s.Id).ToListAsync();
    }

    private async Task ApplyAsync(ColorSample s, SampleInput input)
    {
        var species = (input.WoodSpecies ?? "").Trim();
        if (species.Length == 0) throw ApiException.Bad("Wood species is required — the same formula lands differently on each wood.");
        if (input.FinalL is < 0 or > 100) throw ApiException.Bad("L* must be between 0 and 100.");
        if (Math.Abs(input.FinalA) > 128 || Math.Abs(input.FinalB) > 128) throw ApiException.Bad("a* and b* must be between -128 and 128.");

        s.WoodSpecies = species;
        s.SandingGrit = input.SandingGrit;
        s.WoodL = input.WoodL; s.WoodA = input.WoodA; s.WoodB = input.WoodB;
        s.FormulaId = input.FormulaId;
        s.FormulaName = (input.FormulaName ?? "").Trim();
        if (input.FormulaId is int fid)
        {
            var formula = await db.Formulas.AsNoTracking().FirstOrDefaultAsync(f => f.Id == fid && !f.IsDeleted)
                          ?? throw ApiException.NotFound("Formula");
            await me.EnsureGroupAsync(formula.GroupId);
            if (s.FormulaName.Length == 0) s.FormulaName = formula.Name;
        }
        if (s.FormulaName.Length == 0) throw ApiException.Bad("Pick the formula that was used, or type its name.");

        s.Concentration = input.Concentration;
        s.Method = input.Method;
        s.Coats = input.Coats;
        s.WetFilmMils = input.WetFilmMils;
        s.FlashMinutes = input.FlashMinutes;
        s.Sealer = input.Sealer?.Trim();
        s.Topcoat = input.Topcoat?.Trim();
        s.Sheen = input.Sheen;
        s.FinalL = input.FinalL; s.FinalA = input.FinalA; s.FinalB = input.FinalB;
        s.Source = input.Source;
        s.MeasuredAt = input.MeasuredAt;
        s.Notes = input.Notes?.Trim();
        s.PhotoFile = input.PhotoFile;
        s.Name = string.IsNullOrWhiteSpace(input.Name)
            ? $"{s.WoodSpecies} — {s.FormulaName}{(s.Concentration is > 0 ? $" {s.Concentration}%" : "")}"
            : input.Name.Trim();
    }

    private static string Read(ColorSample s) => $"L* {s.FinalL:0.##} a* {s.FinalA:0.##} b* {s.FinalB:0.##}";

    private static object Describe(ColorSample s) => new
    {
        s.Id, s.GroupId, s.Name, s.WoodSpecies, s.SandingGrit,
        wood = s.WoodL != null && s.WoodA != null && s.WoodB != null
            ? new { l = s.WoodL, a = s.WoodA, b = s.WoodB, hex = ColorScience.LabToHex(new Lab(s.WoodL.Value, s.WoodA.Value, s.WoodB.Value)) }
            : null,
        s.FormulaId, s.FormulaName, s.Concentration, s.Method, s.Coats, s.WetFilmMils, s.FlashMinutes, s.Sealer, s.Topcoat, s.Sheen,
        final = new { l = s.FinalL, a = s.FinalA, b = s.FinalB, hex = ColorScience.LabToHex(new Lab(s.FinalL, s.FinalA, s.FinalB)) },
        s.Source, s.MeasuredAt, s.PhotoFile, s.Notes, s.CreatedAt, s.UpdatedAt,
    };
}
