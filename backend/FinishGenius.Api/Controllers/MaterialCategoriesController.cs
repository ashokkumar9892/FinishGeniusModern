using FinishGenius.Api.Data;
using FinishGenius.Api.Domain;
using FinishGenius.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinishGenius.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/material-categories")]
public class MaterialCategoriesController(AppDbContext db, CurrentUser me) : ControllerBase
{
    /// <summary>GET /api/material-categories?groupId=1&amp;type=4 — categories with their characteristics (ordered by sequence).</summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int groupId, [FromQuery] MaterialType? type)
    {
        await me.EnsureGroupAsync(groupId);
        var q = db.MaterialCategories.AsNoTracking().Where(c => c.GroupId == groupId);
        if (type != null) q = q.Where(c => c.MaterialType == type);
        var rows = await q.Include(c => c.Characteristics).OrderBy(c => c.Name).ToListAsync();
        return Ok(rows.Select(c => new
        {
            c.Id, c.GroupId, c.Name, MaterialType = (int)c.MaterialType, MaterialTypeLabel = MaterialTypes.Label(c.MaterialType),
            c.Filter1, c.Filter2,
            MaterialCount = 0,
            Characteristics = c.Characteristics.OrderBy(x => x.Sequence).Select(x => new
            {
                x.Id, x.CategoryId, x.Name, x.Unit, x.InputType, x.CalcVariable, x.DefaultValue, x.Sequence,
            }),
        }));
    }
}
