using FinishGenius.Api.Data;
using FinishGenius.Api.Domain;
using FinishGenius.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinishGenius.Api.Controllers;

/// <summary>
/// Process Sub Step Setup (System Admin). Sub steps are global reference data per industry sector
/// (not group owned); reading them is allowed to everyone who builds process steps.
/// </summary>
[ApiController]
[Authorize(Roles = Access.ProcessRead)]
[Route("api/sub-steps")]
public class SubStepsController(AppDbContext db, AuditService audit) : ControllerBase
{
    private const string Entity = "SubStep";
    private static readonly string[] UserRoles = ["User", "Admin"];

    public class SubStepInput
    {
        public int IndustrySectorId { get; set; }
        public string? UserRole { get; set; }
        public string? Name { get; set; }
        public string? ShortName { get; set; }
        public int Sequence { get; set; }
        public int PassThroughs { get; set; } = 1;
        public string? WebLink { get; set; }
        public string? Instruction { get; set; }
    }

    public class PullDownInput
    {
        public int Sequence { get; set; }
        public string? Header { get; set; }
        public string? ChoiceName { get; set; }
        public MaterialType? MaterialType { get; set; }
        public string? CategoryFilter1 { get; set; }
        public string? CategoryFilter2 { get; set; }
    }

    /// <summary>Human-readable description of the structured pull-down source (replaces the legacy SQL "Query").</summary>
    public static string QueryText(SubStepPullDown p)
    {
        var parts = new List<string>();
        if (p.MaterialType != null) parts.Add($"Type = {MaterialTypes.Label(p.MaterialType.Value)}");
        if (!string.IsNullOrWhiteSpace(p.CategoryFilter1)) parts.Add($"Filter1 = '{p.CategoryFilter1}'");
        if (!string.IsNullOrWhiteSpace(p.CategoryFilter2)) parts.Add($"Filter2 = '{p.CategoryFilter2}'");
        return parts.Count == 0 ? "All categories" : "Categories where " + string.Join(" and ", parts);
    }

    private static object PullDownDto(SubStepPullDown p) => new
    {
        p.Id, p.SubStepId, p.Sequence, p.Header, p.ChoiceName,
        MaterialType = p.MaterialType == null ? (int?)null : (int)p.MaterialType.Value,
        MaterialTypeLabel = p.MaterialType == null ? null : MaterialTypes.Label(p.MaterialType.Value),
        p.CategoryFilter1, p.CategoryFilter2, Query = QueryText(p),
    };

    private static object SubStepDto(SubStep s) => new
    {
        s.Id, s.IndustrySectorId, s.UserRole, s.Name, s.ShortName, s.Sequence, s.PassThroughs, s.WebLink, s.Instruction,
        PullDownCount = s.PullDowns.Count,
        PullDowns = s.PullDowns.OrderBy(p => p.Sequence).ThenBy(p => p.Id).Select(PullDownDto),
    };

    /// <summary>GET /api/sub-steps?industrySectorId=7&amp;userRole=User</summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int industrySectorId, [FromQuery] string? userRole)
    {
        var q = db.SubSteps.AsNoTracking().Include(s => s.PullDowns).Where(s => s.IndustrySectorId == industrySectorId);
        if (!string.IsNullOrWhiteSpace(userRole)) q = q.Where(s => s.UserRole == userRole);
        var rows = await q.OrderBy(s => s.Sequence).ToListAsync();
        var used = await UsageCountsAsync(rows.Select(r => r.Id).ToList());
        return Ok(rows.Select(s => new
        {
            s.Id, s.IndustrySectorId, s.UserRole, s.Name, s.ShortName, s.Sequence, s.PassThroughs, s.WebLink, s.Instruction,
            PullDownCount = s.PullDowns.Count,
            UsedBySteps = used.GetValueOrDefault(s.Id),
            PullDowns = s.PullDowns.OrderBy(p => p.Sequence).ThenBy(p => p.Id).Select(PullDownDto),
        }));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        var s = await db.SubSteps.AsNoTracking().Include(x => x.PullDowns).FirstOrDefaultAsync(x => x.Id == id)
                ?? throw ApiException.NotFound("Sub Step");
        return Ok(SubStepDto(s));
    }

    private async Task<Dictionary<int, int>> UsageCountsAsync(List<int> subStepIds) =>
        await db.ProcessStepEntries.AsNoTracking()
            .Where(e => subStepIds.Contains(e.SubStepId) && !db.ProcessSteps.Any(p => p.Id == e.ProcessStepId && p.IsDeleted))
            .GroupBy(e => e.SubStepId)
            .Select(g => new { g.Key, Count = g.Select(e => e.ProcessStepId).Distinct().Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count);

    private async Task ValidateAsync(SubStepInput input, int? id)
    {
        if (!await db.IndustrySectors.AnyAsync(s => s.Id == input.IndustrySectorId))
            throw ApiException.Bad("Industry Sector is required.");
        Text.Req(input.Name, "Name");
        Text.Req(input.ShortName, "Short Name");
        if (input.UserRole is not null && !UserRoles.Contains(input.UserRole))
            throw ApiException.Bad("User Role must be User or Admin.");
        if (input.Sequence < 1) throw ApiException.Bad("Sequence must be 1 or greater.");
        if (input.PassThroughs < 1) throw ApiException.Bad("Number of Pass Throughs must be 1 or greater.");
        if (input.PassThroughs > 26) throw ApiException.Bad("Number of Pass Throughs cannot be greater than 26.");
        if (await db.SubSteps.AnyAsync(s => s.IndustrySectorId == input.IndustrySectorId && s.Sequence == input.Sequence && s.Id != (id ?? 0)))
            throw ApiException.Bad("Sequence already exists.");
    }

    [HttpPost]
    [Authorize(Roles = Access.SubSteps)]
    public async Task<IActionResult> Create([FromBody] SubStepInput input)
    {
        await ValidateAsync(input, null);
        var s = new SubStep
        {
            IndustrySectorId = input.IndustrySectorId, UserRole = input.UserRole ?? "User", Name = input.Name!.Trim(),
            ShortName = input.ShortName!.Trim(), Sequence = input.Sequence, PassThroughs = input.PassThroughs,
            WebLink = Text.Clean(input.WebLink), Instruction = Text.Clean(input.Instruction),
        };
        db.SubSteps.Add(s);
        await db.SaveChangesAsync();
        audit.Log(Entity, s.Id, "Created", $"{s.Sequence} {s.Name} ({s.UserRole}, {s.PassThroughs} pass throughs)");
        await db.SaveChangesAsync();
        return Ok(new { message = "Sub Step created.", id = s.Id });
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = Access.SubSteps)]
    public async Task<IActionResult> Update(int id, [FromBody] SubStepInput input)
    {
        var s = await db.SubSteps.FirstOrDefaultAsync(x => x.Id == id) ?? throw ApiException.NotFound("Sub Step");
        await ValidateAsync(input, id);
        if (input.IndustrySectorId != s.IndustrySectorId && await db.ProcessStepEntries.AnyAsync(e => e.SubStepId == id))
            throw ApiException.Bad("The Industry Sector cannot be changed because process steps already use this sub step.");
        if (input.PassThroughs < s.PassThroughs)
        {
            var maxPass = await db.ProcessStepEntries.Where(e => e.SubStepId == id).MaxAsync(e => (int?)e.Pass) ?? 0;
            if (maxPass > input.PassThroughs)
                throw ApiException.Bad($"Number of Pass Throughs cannot be lower than {maxPass} because process steps have data in pass {maxPass}.");
        }

        var changes = new List<string>();
        void Track(string field, object? before, object? after)
        {
            if (!Equals(before, after)) changes.Add($"{field}: {before ?? "—"} → {after ?? "—"}");
        }
        Track("Name", s.Name, input.Name!.Trim());
        Track("Short Name", s.ShortName, input.ShortName!.Trim());
        Track("Sequence", s.Sequence, input.Sequence);
        Track("Pass Throughs", s.PassThroughs, input.PassThroughs);
        Track("User Role", s.UserRole, input.UserRole ?? s.UserRole);
        Track("WebLink", s.WebLink, Text.Clean(input.WebLink));
        if (s.Instruction != Text.Clean(input.Instruction)) changes.Add("Instruction updated");

        s.IndustrySectorId = input.IndustrySectorId;
        s.UserRole = input.UserRole ?? s.UserRole;
        s.Name = input.Name!.Trim();
        s.ShortName = input.ShortName!.Trim();
        s.Sequence = input.Sequence;
        s.PassThroughs = input.PassThroughs;
        s.WebLink = Text.Clean(input.WebLink);
        s.Instruction = Text.Clean(input.Instruction);
        audit.Log(Entity, s.Id, "Updated", changes.Count == 0 ? "No changes" : string.Join("\n", changes));
        await db.SaveChangesAsync();
        return Ok(new { message = "Sub Step updated.", id = s.Id });
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = Access.SubSteps)]
    public async Task<IActionResult> Delete(int id)
    {
        var s = await db.SubSteps.Include(x => x.PullDowns).FirstOrDefaultAsync(x => x.Id == id) ?? throw ApiException.NotFound("Sub Step");
        var stepCount = await db.ProcessStepEntries.Where(e => e.SubStepId == id).Select(e => e.ProcessStepId).Distinct().CountAsync();
        if (stepCount > 0)
            throw ApiException.Bad($"This sub step cannot be deleted because {stepCount} process step{(stepCount == 1 ? " uses" : "s use")} it. Clear it from those process steps first.");
        db.SubSteps.Remove(s);
        audit.Log(Entity, s.Id, "Deleted", $"{s.Sequence} {s.Name}");
        await db.SaveChangesAsync();
        return Ok(new { message = "Sub Step deleted." });
    }

    // ---------- Pull downs ----------

    [HttpGet("{id:int}/pull-downs")]
    public async Task<IActionResult> PullDowns(int id)
    {
        if (!await db.SubSteps.AnyAsync(s => s.Id == id)) throw ApiException.NotFound("Sub Step");
        var rows = await db.SubStepPullDowns.AsNoTracking().Where(p => p.SubStepId == id)
            .OrderBy(p => p.Sequence).ThenBy(p => p.Id).ToListAsync();
        return Ok(rows.Select(PullDownDto));
    }

    private async Task ValidatePullDownAsync(int subStepId, PullDownInput input, int? id)
    {
        Text.Req(input.Header, "Header");
        if (input.Sequence < 1) throw ApiException.Bad("Sequence must be 1 or greater.");
        if (input.MaterialType != null && !Enum.IsDefined(input.MaterialType.Value)) throw ApiException.Bad("Invalid Material Type.");
        if (await db.SubStepPullDowns.AnyAsync(p => p.SubStepId == subStepId && p.Sequence == input.Sequence && p.Id != (id ?? 0)))
            throw ApiException.Bad("Sequence already exists.");
    }

    [HttpPost("{id:int}/pull-downs")]
    [Authorize(Roles = Access.SubSteps)]
    public async Task<IActionResult> CreatePullDown(int id, [FromBody] PullDownInput input)
    {
        var s = await db.SubSteps.FirstOrDefaultAsync(x => x.Id == id) ?? throw ApiException.NotFound("Sub Step");
        await ValidatePullDownAsync(id, input, null);
        var p = new SubStepPullDown
        {
            SubStepId = id, Sequence = input.Sequence, Header = input.Header!.Trim(), ChoiceName = Text.Clean(input.ChoiceName),
            MaterialType = input.MaterialType, CategoryFilter1 = Text.Clean(input.CategoryFilter1), CategoryFilter2 = Text.Clean(input.CategoryFilter2),
        };
        db.SubStepPullDowns.Add(p);
        audit.Log(Entity, s.Id, "Pull down created", $"{p.Header}: {QueryText(p)}");
        await db.SaveChangesAsync();
        return Ok(new { message = "Pull Down created.", id = p.Id });
    }

    [HttpPut("pull-downs/{pid:int}")]
    [Authorize(Roles = Access.SubSteps)]
    public async Task<IActionResult> UpdatePullDown(int pid, [FromBody] PullDownInput input)
    {
        var p = await db.SubStepPullDowns.FirstOrDefaultAsync(x => x.Id == pid) ?? throw ApiException.NotFound("Pull Down");
        await ValidatePullDownAsync(p.SubStepId, input, pid);
        var before = QueryText(p);
        p.Sequence = input.Sequence;
        p.Header = input.Header!.Trim();
        p.ChoiceName = Text.Clean(input.ChoiceName);
        p.MaterialType = input.MaterialType;
        p.CategoryFilter1 = Text.Clean(input.CategoryFilter1);
        p.CategoryFilter2 = Text.Clean(input.CategoryFilter2);
        var after = QueryText(p);
        audit.Log(Entity, p.SubStepId, "Pull down updated", before == after ? p.Header : $"{p.Header}: {before} → {after}");
        await db.SaveChangesAsync();
        return Ok(new { message = "Pull Down updated.", id = p.Id });
    }

    [HttpDelete("pull-downs/{pid:int}")]
    [Authorize(Roles = Access.SubSteps)]
    public async Task<IActionResult> DeletePullDown(int pid)
    {
        var p = await db.SubStepPullDowns.FirstOrDefaultAsync(x => x.Id == pid) ?? throw ApiException.NotFound("Pull Down");
        var stepCount = await db.ProcessStepEntries.Where(e => e.PullDownId == pid).Select(e => e.ProcessStepId).Distinct().CountAsync();
        if (stepCount > 0)
            throw ApiException.Bad($"This pull down cannot be deleted because {stepCount} process step{(stepCount == 1 ? " uses" : "s use")} it.");
        db.SubStepPullDowns.Remove(p);
        audit.Log(Entity, p.SubStepId, "Pull down deleted", p.Header);
        await db.SaveChangesAsync();
        return Ok(new { message = "Pull Down deleted." });
    }
}
