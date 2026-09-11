using System.Text.Json;
using FinishGenius.Api.Data;
using FinishGenius.Api.Domain;
using FinishGenius.Api.Infrastructure;
using FinishGenius.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinishGenius.Api.Controllers;

/// <summary>
/// Endpoints for the shop's Network Bridge (hardware integration point). Every call sends the bridge's own API key in the
/// <c>X-Api-Key</c> header (Dashboard › Devices shows it). Mirrors the legacy bridge stored procedures:
/// <list type="bullet">
/// <item><c>GET  /api/bridge/commands</c> — pending dispense / purge / scale / label jobs (replaces the SignalR hub push).</item>
/// <item><c>POST /api/bridge/commands/{id}/result</c> — <c>{ success, message, data }</c>; for scales <c>data</c> is the weight.</item>
/// <item><c>GET  /api/bridge/purge-setting</c> — sp_GetPurgeSettingByApiKey + sp_GetPurgeSetting (window and canisters).</item>
/// <item><c>POST /api/bridge/purge-results</c> — sp_SavePurgeSuccess / sp_SavePurgeFailed.</item>
/// <item><c>POST /api/bridge/purge-failures/clear</c> — sp_DeletePurgeFailed.</item>
/// </list>
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/bridge")]
public class DeviceBridgeController(AppDbContext db, DeviceCommandService commands) : ControllerBase
{
    public record CommandResult(bool Success, string? Message, JsonElement? Data);
    public record PurgeResultsRequest(List<DeviceCommandService.PurgeResultInput>? Results);
    public record ClearFailuresRequest(int CanisterNumber);

    /// <summary>GET /api/bridge/commands — open jobs for this bridge; they are marked "Sent" when handed out.</summary>
    [HttpGet("commands")]
    public async Task<IActionResult> Commands()
    {
        var bridge = await BridgeAsync();
        var open = await db.DeviceCommands
            .Where(c => c.BridgeDeviceId == bridge.Id && (c.Status == DeviceCommandStatuses.Pending || c.Status == DeviceCommandStatuses.Sent))
            .OrderBy(c => c.Id).Take(50).ToListAsync();
        foreach (var c in open) DeviceCommandService.Expire(c);
        var live = open.Where(c => DeviceCommandStatuses.IsOpen(c.Status)).ToList();
        var now = DateTime.UtcNow;
        foreach (var c in live.Where(c => c.Status == DeviceCommandStatuses.Pending))
        {
            c.Status = DeviceCommandStatuses.Sent;
            c.SentAt = now;
        }
        await db.SaveChangesAsync();

        var deviceIds = live.Select(c => c.DeviceId).Distinct().ToList();
        var devices = await db.Devices.AsNoTracking().Where(d => deviceIds.Contains(d.Id))
            .Select(d => new { d.Id, d.Name, d.DeviceType, d.IpAddress }).ToDictionaryAsync(d => d.Id);
        return Ok(live.Select(c => new
        {
            c.Id, c.CommandType, c.DeviceId,
            DeviceName = devices.GetValueOrDefault(c.DeviceId)?.Name,
            DeviceType = devices.TryGetValue(c.DeviceId, out var d) ? DevicesController.TypeLabel(d.DeviceType) : null,
            IpAddress = devices.GetValueOrDefault(c.DeviceId)?.IpAddress,
            Payload = c.Payload == null ? (JsonElement?)null : JsonDocument.Parse(c.Payload).RootElement.Clone(),
            c.CreatedAt, c.ExpiresAt,
        }));
    }

    /// <summary>POST /api/bridge/commands/{id}/result — <c>{ success: true, message: "OK", data: … }</c>.</summary>
    [HttpPost("commands/{id:int}/result")]
    public async Task<IActionResult> Result(int id, [FromBody] CommandResult req)
    {
        var bridge = await BridgeAsync();
        var c = await db.DeviceCommands.FirstOrDefaultAsync(x => x.Id == id && x.BridgeDeviceId == bridge.Id) ?? throw ApiException.NotFound("Command");
        DeviceCommandService.Expire(c);
        if (!DeviceCommandStatuses.IsOpen(c.Status))
        {
            await db.SaveChangesAsync();
            throw new ApiException(StatusCodes.Status409Conflict, $"This command is no longer open ({c.Status}).");
        }
        string? data = req.Data is { } el ? el.ValueKind == JsonValueKind.String ? el.GetString() : el.GetRawText() : null;
        await commands.ApplyResultAsync(c, req.Success, Text.Clean(req.Message), data);
        await db.SaveChangesAsync();
        return Ok(new { message = "Result stored.", status = c.Status });
    }

    /// <summary>GET /api/bridge/purge-setting — automatic purge window plus the dispense machine(s) and filled canisters.</summary>
    [HttpGet("purge-setting")]
    public async Task<IActionResult> PurgeSetting()
    {
        var bridge = await BridgeAsync();
        await db.SaveChangesAsync();
        var s = await db.PurgeSettings.AsNoTracking().FirstOrDefaultAsync(p => p.BridgeDeviceId == bridge.Id);
        var dispensers = await db.Devices.AsNoTracking().Include(d => d.Canisters)
            .Where(d => d.NetworkBridgeId == bridge.Id && d.DeviceType == DeviceType.DispenseMachine && !d.IsArchived)
            .OrderBy(d => d.Id).ToListAsync();
        return Ok(new
        {
            FromDate = s?.FromDate.ToString("yyyy-MM-dd"),
            ToDate = s?.ToDate.ToString("yyyy-MM-dd"),
            Time = s?.Time.ToString("HH:mm"),
            Dispensers = dispensers.Select(d => new
            {
                d.Id, d.Name, d.IpAddress,
                Canisters = d.Canisters.Where(c => c.MaterialId != null).OrderBy(c => c.CanisterNo).Select(c => c.CanisterNo),
            }),
        });
    }

    /// <summary>POST /api/bridge/purge-results — <c>{ results: [{ canisterNumber, success, message, executedAt, purgeType: "Auto" }] }</c>.</summary>
    [HttpPost("purge-results")]
    public async Task<IActionResult> PurgeResults([FromBody] PurgeResultsRequest req)
    {
        var bridge = await BridgeAsync();
        var results = req.Results ?? [];
        if (results.Count > 500) throw ApiException.Bad("Send at most 500 purge results per request.");
        if (results.Any(r => r.CanisterNumber < 0 || r.CanisterNumber > DevicesController.CanisterCount))
            throw ApiException.Bad($"Canister numbers must be between 0 and {DevicesController.CanisterCount}.");
        var n = commands.StorePurgeResults(bridge.Id, results);
        await db.SaveChangesAsync();
        return Ok(new { message = "Purge results stored.", count = n });
    }

    /// <summary>POST /api/bridge/purge-failures/clear <c>{ canisterNumber }</c> — 0 clears every active failure of the bridge.</summary>
    [HttpPost("purge-failures/clear")]
    public async Task<IActionResult> ClearFailures([FromBody] ClearFailuresRequest req)
    {
        var bridge = await BridgeAsync();
        var q = db.PurgeFailures.Where(p => p.BridgeDeviceId == bridge.Id && p.IsActive);
        if (req.CanisterNumber != 0) q = q.Where(p => p.CanisterNumber == req.CanisterNumber || p.CanisterNumber == 0);
        var rows = await q.ToListAsync();
        foreach (var r in rows) r.IsActive = false;
        await db.SaveChangesAsync();
        return Ok(new { message = "Purge failures cleared.", count = rows.Count });
    }

    private async Task<Device> BridgeAsync()
    {
        if (!Guid.TryParse(Request.Headers["X-Api-Key"].ToString(), out var key))
            throw new ApiException(StatusCodes.Status401Unauthorized, "A valid X-Api-Key header is required.");
        var device = await db.Devices.FirstOrDefaultAsync(d => d.ApiKey == key && !d.IsArchived)
            ?? throw new ApiException(StatusCodes.Status401Unauthorized, "Unknown or revoked device API key.");
        if (device.DeviceType != DeviceType.NetworkBridge)
            throw new ApiException(StatusCodes.Status403Forbidden, "Only Network Bridge devices can use this endpoint.");
        device.LastSeenAt = DateTime.UtcNow;
        return device;
    }
}
