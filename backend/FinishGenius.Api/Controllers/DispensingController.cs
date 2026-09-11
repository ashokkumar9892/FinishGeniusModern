using System.Globalization;
using FinishGenius.Api.Data;
using FinishGenius.Api.Domain;
using FinishGenius.Api.Infrastructure;
using FinishGenius.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinishGenius.Api.Controllers;

/// <summary>
/// Group-level dispensing tools of the Formulas list: "Clean Nozzle", "Purge" / "Purge History", reading and taring scales
/// and the status of device commands (see <see cref="DeviceCommandService"/>).
/// </summary>
[ApiController]
[Authorize(Roles = Access.Formulas)]
[Route("api/dispensing")]
public class DispensingController(AppDbContext db, CurrentUser me, AuditService audit, DeviceCommandService commands) : ControllerBase
{
    public record NozzleRequest(int GroupId, int? CleanNozzleHours);
    public record GroupRequest(int GroupId);
    public record PurgeSettingRequest(string? FromDate, string? ToDate, string? Time);

    // ------------------------------------------------------------------ clean nozzle

    public static async Task<DateTime?> LastDispensedAtAsync(AppDbContext db, int groupId, DispenseSetting? s)
    {
        if (s?.LastDispensedAt != null) return s.LastDispensedAt;
        // Legacy used the latest MaterialQuantityChanges.DateDispensed (recorded dispenses).
        return await db.InventoryTransactions.Where(t => t.GroupId == groupId && t.FormulaId != null && t.Quantity < 0)
            .MaxAsync(t => (DateTime?)t.CreatedAt);
    }

    /// <summary>The nozzle must be cleaned when the interval since the last dispense has passed and nobody confirmed a cleaning.</summary>
    public static bool CleaningRequired(DispenseSetting? s, DateTime? lastDispensed) =>
        s is { CleanNozzleHours: > 0 } && !s.IsNozzleCleaned && lastDispensed != null
        && DateTime.UtcNow - lastDispensed.Value > TimeSpan.FromHours(s.CleanNozzleHours!.Value);

    /// <summary>GET /api/dispensing/settings?groupId= — clean-nozzle interval and which dispensing buttons apply.</summary>
    [HttpGet("settings")]
    public async Task<IActionResult> Settings([FromQuery] int groupId)
    {
        await me.EnsureGroupAsync(groupId);
        var s = await db.DispenseSettings.AsNoTracking().FirstOrDefaultAsync(x => x.GroupId == groupId);
        var last = await LastDispensedAtAsync(db, groupId, s);
        var types = await db.Devices.AsNoTracking().Where(d => d.GroupId == groupId && !d.IsArchived).Select(d => d.DeviceType).Distinct().ToListAsync();
        var cutoff = DateTime.UtcNow - DevicesController.OnlineWindow;
        var onlineBridge = await db.Devices.AnyAsync(d => d.GroupId == groupId && !d.IsArchived && d.DeviceType == DeviceType.NetworkBridge && d.LastSeenAt >= cutoff);
        return Ok(new
        {
            CleanNozzleHours = s?.CleanNozzleHours ?? 0,
            IsNozzleCleaned = s?.IsNozzleCleaned ?? false,
            LastDispensedAt = last,
            CleaningRequired = CleaningRequired(s, last),
            HasDispensers = types.Contains(DeviceType.DispenseMachine),
            HasBridges = types.Contains(DeviceType.NetworkBridge),
            // Purge / machine dispense need a network bridge that is connected right now.
            HasOnlineBridge = onlineBridge,
        });
    }

    /// <summary>PUT /api/dispensing/settings { groupId, cleanNozzleHours } — "Clean Nozzle" (None, 1 … 24 hours).</summary>
    [HttpPut("settings")]
    public async Task<IActionResult> SaveSettings([FromBody] NozzleRequest req)
    {
        await me.EnsureGroupAsync(req.GroupId);
        var hours = req.CleanNozzleHours ?? 0;
        if (hours is < 0 or > 24) throw ApiException.Bad("Please select a time interval between 1 and 24 hours (or None).");
        var s = await db.DispenseSettings.FirstOrDefaultAsync(x => x.GroupId == req.GroupId);
        if (s == null)
        {
            s = new DispenseSetting { GroupId = req.GroupId };
            db.DispenseSettings.Add(s);
        }
        var before = s.CleanNozzleHours ?? 0;
        s.CleanNozzleHours = hours == 0 ? null : hours;
        audit.Log("DispenseSetting", req.GroupId, "Clean Nozzle updated", $"Interval: {Hours(before)} → {Hours(hours)}", req.GroupId);
        await db.SaveChangesAsync();
        return Ok(new { message = "Clean Nozzle setting saved." });
    }

    /// <summary>POST /api/dispensing/settings/nozzle-cleaned { groupId } — "Confirmation: Nozzle Cleaning Completed".</summary>
    [HttpPost("settings/nozzle-cleaned")]
    public async Task<IActionResult> NozzleCleaned([FromBody] GroupRequest req)
    {
        await me.EnsureGroupAsync(req.GroupId);
        var s = await db.DispenseSettings.FirstOrDefaultAsync(x => x.GroupId == req.GroupId);
        if (s == null)
        {
            s = new DispenseSetting { GroupId = req.GroupId };
            db.DispenseSettings.Add(s);
        }
        s.IsNozzleCleaned = true;
        audit.Log("DispenseSetting", req.GroupId, "Nozzle cleaning confirmed", null, req.GroupId);
        await db.SaveChangesAsync();
        return Ok(new { message = "Nozzle cleaning confirmed. You can dispense now." });
    }

    private static string Hours(int h) => h <= 0 ? "None" : h == 1 ? "1 hour" : $"{h} hours";

    // ------------------------------------------------------------------ purge

    /// <summary>GET /api/dispensing/purge?groupId= — the group's network bridges with their purge window ("Purge" modal).</summary>
    [HttpGet("purge")]
    public async Task<IActionResult> PurgeDevices([FromQuery] int groupId)
    {
        await me.EnsureGroupAsync(groupId);
        var bridges = await db.Devices.AsNoTracking().Where(d => d.GroupId == groupId && !d.IsArchived && d.DeviceType == DeviceType.NetworkBridge)
            .OrderBy(d => d.Name).ToListAsync();
        var ids = bridges.Select(b => b.Id).ToList();
        var settings = await db.PurgeSettings.AsNoTracking().Where(p => ids.Contains(p.BridgeDeviceId)).ToDictionaryAsync(p => p.BridgeDeviceId);
        var dispensers = await db.Devices.AsNoTracking().Include(d => d.Canisters)
            .Where(d => d.NetworkBridgeId != null && ids.Contains(d.NetworkBridgeId.Value) && d.DeviceType == DeviceType.DispenseMachine && !d.IsArchived)
            .ToListAsync();
        var failures = await db.PurgeFailures.AsNoTracking().Where(p => ids.Contains(p.BridgeDeviceId) && p.IsActive)
            .GroupBy(p => p.BridgeDeviceId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count);
        var cutoff = DateTime.UtcNow - DevicesController.OnlineWindow;
        return Ok(bridges.Select(b =>
        {
            settings.TryGetValue(b.Id, out var s);
            return new
            {
                b.Id, b.Name, b.LastSeenAt, Online = b.LastSeenAt >= cutoff,
                FromDate = s?.FromDate.ToString("yyyy-MM-dd"), ToDate = s?.ToDate.ToString("yyyy-MM-dd"), Time = s?.Time.ToString("HH:mm"),
                Dispensers = dispensers.Where(d => d.NetworkBridgeId == b.Id).OrderBy(d => d.Id)
                    .Select(d => new { d.Id, d.Name, Canisters = d.Canisters.Count(c => c.MaterialId != null) }),
                ActiveFailures = failures.GetValueOrDefault(b.Id),
            };
        }));
    }

    /// <summary>PUT /api/dispensing/purge/{bridgeId}/settings { fromDate, toDate, time } — legacy sp_SavePurgeSetting.</summary>
    [HttpPut("purge/{bridgeId:int}/settings")]
    public async Task<IActionResult> SavePurgeSetting(int bridgeId, [FromBody] PurgeSettingRequest req)
    {
        var bridge = await LoadDeviceAsync(bridgeId, DeviceType.NetworkBridge, "Network Bridge");
        var from = ParseDate(req.FromDate, "From Date is required.");
        var to = ParseDate(req.ToDate, "To Date is required.");
        if (from > to) throw ApiException.Bad("From Date must be less than or equal to To Date.");
        var time = ParseTime(req.Time) ?? throw ApiException.Bad("Time is required.");

        var s = await db.PurgeSettings.FirstOrDefaultAsync(p => p.BridgeDeviceId == bridge.Id);
        if (s == null)
        {
            s = new PurgeSetting { BridgeDeviceId = bridge.Id, CreatedBy = me.Id == 0 ? null : me.Id };
            db.PurgeSettings.Add(s);
        }
        else
        {
            s.UpdatedBy = me.Id == 0 ? null : me.Id;
            s.UpdatedAt = DateTime.UtcNow;
        }
        s.FromDate = from;
        s.ToDate = to;
        s.Time = time;
        audit.Log("Device", bridge.Id, "Purge setting saved", $"{from:yyyy-MM-dd} – {to:yyyy-MM-dd} at {time:HH\\:mm}", bridge.GroupId);
        await db.SaveChangesAsync();
        return Ok(new { message = "Purge setting is saved. Please restart network bridge to apply impact of latest setting." });
    }

    /// <summary>POST /api/dispensing/purge/{bridgeId}/run — "Purge": purges every filled canister of the bridge's dispense machine.</summary>
    [HttpPost("purge/{bridgeId:int}/run")]
    public async Task<IActionResult> RunPurge(int bridgeId)
    {
        var bridge = await LoadDeviceAsync(bridgeId, DeviceType.NetworkBridge, "Network Bridge");
        // Legacy sp_GetPurgeSetting: the (first) dispense machine behind the bridge, canisters with a material.
        var dispenser = await db.Devices.AsNoTracking().Include(d => d.Canisters)
            .Where(d => d.NetworkBridgeId == bridge.Id && d.DeviceType == DeviceType.DispenseMachine && !d.IsArchived)
            .OrderBy(d => d.Id).FirstOrDefaultAsync();
        var canisters = dispenser?.Canisters.Where(c => c.MaterialId != null).OrderBy(c => c.CanisterNo).Select(c => c.CanisterNo).ToList() ?? [];
        if (dispenser == null) throw ApiException.Bad($"No Dispense Machine is connected to \"{bridge.Name}\".");
        if (canisters.Count == 0) throw ApiException.Bad($"No canisters are assigned on \"{dispenser.Name}\" (Dashboard › Devices › Canister Tint Assignment).");

        var cmd = await commands.QueueAsync(bridge, DeviceCommandTypes.Purge, new
        {
            BridgeId = bridge.Id, BridgeName = bridge.Name, DispenserId = dispenser.Id, DispenserName = dispenser.Name,
            dispenser.IpAddress, PurgeType = "Manual", Canisters = canisters,
        }, null, me.Id, TimeSpan.FromMinutes(10));
        audit.Log("Device", bridge.Id, "Purge requested", $"{dispenser.Name}: canisters {string.Join(", ", canisters)}", bridge.GroupId);
        await db.SaveChangesAsync();
        return Ok(new { message = $"Purge request sent to {bridge.Name} for {canisters.Count} canister(s).", commandId = cmd.Id });
    }

    /// <summary>GET /api/dispensing/purge/history?groupId=&amp;from=2026-09-01&amp;to=2026-09-10 — "Purge History" (administrators).</summary>
    [HttpGet("purge/history")]
    public async Task<IActionResult> PurgeHistory([FromQuery] int groupId, [FromQuery] string? from, [FromQuery] string? to)
    {
        me.EnsureAdmin();
        await me.EnsureGroupAsync(groupId);
        var fromDate = ParseDate(from, "From Date is required.");
        var toDate = ParseDate(to, "To Date is required.");
        if (fromDate > toDate) throw ApiException.Bad("From Date must be less than or equal to To Date.");
        var start = fromDate.ToDateTime(TimeOnly.MinValue);
        var end = toDate.AddDays(1).ToDateTime(TimeOnly.MinValue);

        var bridges = await db.Devices.AsNoTracking().Where(d => d.GroupId == groupId && d.DeviceType == DeviceType.NetworkBridge)
            .Select(d => new { d.Id, d.Name, GroupName = d.Group!.Name }).ToDictionaryAsync(d => d.Id);
        var ids = bridges.Keys.ToList();
        var ok = await db.PurgeSuccesses.AsNoTracking()
            .Where(p => ids.Contains(p.BridgeDeviceId) && p.ExecutedAt >= start && p.ExecutedAt < end)
            .OrderByDescending(p => p.ExecutedAt).Take(5000).ToListAsync();
        var failed = await db.PurgeFailures.AsNoTracking()
            .Where(p => ids.Contains(p.BridgeDeviceId) && p.CreatedAt >= start && p.CreatedAt < end)
            .OrderByDescending(p => p.CreatedAt).Take(5000).ToListAsync();

        var rows = ok.Select(p => new
        {
            Key = $"s{p.Id}", GroupName = bridges[p.BridgeDeviceId].GroupName, BridgeName = bridges[p.BridgeDeviceId].Name,
            PurgeType = p.PurgeType ?? "", p.CanisterNumber, Date = p.ExecutedAt, p.Message, Result = "Success",
        }).Concat(failed.Select(p => new
        {
            Key = $"f{p.Id}", GroupName = bridges[p.BridgeDeviceId].GroupName, BridgeName = bridges[p.BridgeDeviceId].Name,
            PurgeType = "", p.CanisterNumber, Date = p.CreatedAt, p.Message, Result = p.IsActive ? "Failed (active)" : "Failed",
        })).OrderByDescending(r => r.Date).ToList();
        return Ok(rows);
    }

    // ------------------------------------------------------------------ scales

    /// <summary>POST /api/dispensing/scales/{deviceId}/weight — asks the bridge for the scale weight; poll the returned command.</summary>
    [HttpPost("scales/{deviceId:int}/weight")]
    public async Task<IActionResult> ReadScale(int deviceId)
    {
        var scale = await LoadScaleAsync(deviceId);
        var cmd = await commands.QueueAsync(scale, DeviceCommandTypes.GetWeight,
            new { DeviceId = scale.Id, DeviceName = scale.Name, scale.IpAddress, Unit = scale.DeviceType == DeviceType.ScaleKilograms ? "kg" : "g" },
            null, me.Id, DeviceCommandService.ScaleTimeout);
        await db.SaveChangesAsync();
        return Ok(new { message = "Reading the scale…", commandId = cmd.Id });
    }

    /// <summary>POST /api/dispensing/scales/{deviceId}/tare.</summary>
    [HttpPost("scales/{deviceId:int}/tare")]
    public async Task<IActionResult> TareScale(int deviceId)
    {
        var scale = await LoadScaleAsync(deviceId);
        var cmd = await commands.QueueAsync(scale, DeviceCommandTypes.Tare, new { DeviceId = scale.Id, DeviceName = scale.Name, scale.IpAddress },
            null, me.Id, DeviceCommandService.ScaleTimeout);
        await db.SaveChangesAsync();
        return Ok(new { message = "Taring the scale…", commandId = cmd.Id });
    }

    // ------------------------------------------------------------------ commands

    /// <summary>GET /api/dispensing/commands/{id} — status of a device command (scale weight in grams when available).</summary>
    [HttpGet("commands/{id:int}")]
    public async Task<IActionResult> Command(int id)
    {
        var c = await db.DeviceCommands.FirstOrDefaultAsync(x => x.Id == id) ?? throw ApiException.NotFound("Command");
        await me.EnsureGroupAsync(c.GroupId);
        if (DeviceCommandService.Expire(c)) await db.SaveChangesAsync();

        decimal? weight = null;
        var message = c.ResultMessage;
        var status = c.Status;
        if (c.CommandType == DeviceCommandTypes.GetWeight && c.Status == DeviceCommandStatuses.Succeeded)
        {
            var type = await db.Devices.Where(d => d.Id == c.DeviceId).Select(d => d.DeviceType).FirstOrDefaultAsync();
            var (grams, error) = DeviceCommandService.ParseWeight(c.ResultData, type);
            if (error != null) { status = DeviceCommandStatuses.Failed; message = error; }
            else if (grams <= 0) { status = DeviceCommandStatuses.Failed; message = "No weight on scale"; }
            else weight = grams;
        }
        return Ok(new
        {
            c.Id, c.CommandType, Status = status, Done = !DeviceCommandStatuses.IsOpen(status), Message = message,
            WeightGrams = weight, c.CreatedAt, c.SentAt, c.CompletedAt,
        });
    }

    /// <summary>POST /api/dispensing/commands/{id}/cancel — "Cancel Dispense" / stop waiting.</summary>
    [HttpPost("commands/{id:int}/cancel")]
    public async Task<IActionResult> Cancel(int id)
    {
        var c = await db.DeviceCommands.FirstOrDefaultAsync(x => x.Id == id) ?? throw ApiException.NotFound("Command");
        await me.EnsureGroupAsync(c.GroupId);
        DeviceCommandService.Expire(c);
        if (!DeviceCommandStatuses.IsOpen(c.Status))
        {
            await db.SaveChangesAsync();
            throw ApiException.Bad("This request has already finished.");
        }
        c.Status = DeviceCommandStatuses.Cancelled;
        c.ResultMessage = "Canceled by the user.";
        c.CompletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(new { message = c.CommandType == DeviceCommandTypes.Dispense ? "Dispense canceled." : "Request canceled." });
    }

    // ------------------------------------------------------------------ helpers

    private async Task<Device> LoadDeviceAsync(int id, DeviceType type, string label)
    {
        var d = await db.Devices.FirstOrDefaultAsync(x => x.Id == id && !x.IsArchived && x.DeviceType == type) ?? throw ApiException.NotFound(label);
        await me.EnsureGroupAsync(d.GroupId);
        return d;
    }

    private async Task<Device> LoadScaleAsync(int id)
    {
        var d = await db.Devices.FirstOrDefaultAsync(x => x.Id == id && !x.IsArchived && (x.DeviceType == DeviceType.ScaleGrams || x.DeviceType == DeviceType.ScaleKilograms))
            ?? throw ApiException.Bad("Please select a scale or manually enter weight.");
        await me.EnsureGroupAsync(d.GroupId);
        return d;
    }

    private static DateOnly ParseDate(string? value, string requiredMessage)
    {
        if (string.IsNullOrWhiteSpace(value)) throw ApiException.Bad(requiredMessage);
        var formats = new[] { "yyyy-MM-dd", "MM/dd/yyyy", "M/d/yyyy" };
        if (DateOnly.TryParseExact(value.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) return d;
        throw ApiException.Bad($"\"{value}\" is not a valid date.");
    }

    private static TimeOnly? ParseTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var formats = new[] { "HH:mm", "H:mm", "HH:mm:ss", "h:mm tt", "h:mmtt", "hh:mm tt" };
        if (TimeOnly.TryParseExact(value.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)) return t;
        throw ApiException.Bad($"\"{value}\" is not a valid time.");
    }
}
