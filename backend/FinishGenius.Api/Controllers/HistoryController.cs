using FinishGenius.Api.Data;
using FinishGenius.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinishGenius.Api.Controllers;

/// <summary>Audit trail shown by every "History" (⟲) action.</summary>
[ApiController]
[Authorize]
[Route("api/history")]
public class HistoryController(AppDbContext db, CurrentUser me) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] string entityType, [FromQuery] int entityId)
    {
        var groupIds = await me.GroupIdsAsync();
        var rows = await db.AuditLogs.AsNoTracking()
            .Where(a => a.EntityType == entityType && a.EntityId == entityId && (a.GroupId == null || groupIds.Contains(a.GroupId.Value)))
            .OrderByDescending(a => a.Id).Take(500)
            .Select(a => new { a.Id, a.Action, a.Details, a.UserName, a.CreatedAt })
            .ToListAsync();
        return Ok(rows);
    }
}
