using FinishGenius.Api.Data;
using FinishGenius.Api.Domain;
using FinishGenius.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinishGenius.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/lookups")]
public class LookupsController(AppDbContext db, CurrentUser me) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var groupIds = await me.GroupIdsAsync();
        return Ok(new
        {
            Roles = Roles.All.Select(r => new { Value = r, Label = Roles.Labels[r] }),
            MaterialTypes = Enum.GetValues<MaterialType>().Select(t => new { Value = (int)t, Label = MaterialTypes.Label(t) }),
            DeviceTypes = new[]
            {
                new { Value = 1, Label = "Camera" }, new { Value = 2, Label = "Scale(g)" },
                new { Value = 3, Label = "Temperature & Humidity Sensor" }, new { Value = 4, Label = "Scale(kg)" },
                new { Value = 5, Label = "Label Printer" }, new { Value = 6, Label = "Network Bridge" },
                new { Value = 7, Label = "Dispense Machine" },
            },
            IndustrySectors = await db.IndustrySectors.AsNoTracking().OrderBy(s => s.Name).ToListAsync(),
            InputTypes = CharacteristicInputTypes.All,
            CalcVariables = new[]
            {
                new { Value = CalcVariables.None, Label = "None" },
                new { Value = CalcVariables.MaterialId, Label = "Material used (quantities)" },
                new { Value = CalcVariables.Coverage, Label = "Coverage (sq ft / gal)" },
                new { Value = CalcVariables.MixPercent, Label = "Mix % of previous material" },
                new { Value = CalcVariables.ProductionRate, Label = "Production rate (sq ft / hr)" },
                new { Value = CalcVariables.CostPerSqFt, Label = "Cost ($ / sq ft)" },
            },
            TimeZones = TimeZoneInfo.GetSystemTimeZones().Select(z => new { Value = z.Id, Label = z.DisplayName }),
            Groups = await db.Groups.AsNoTracking().Where(g => groupIds.Contains(g.Id)).OrderBy(g => g.Name)
                .Select(g => new { g.Id, g.Name }).ToListAsync(),
        });
    }
}
