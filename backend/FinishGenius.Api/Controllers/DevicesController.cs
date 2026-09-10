using FinishGenius.Api.Data;
using FinishGenius.Api.Domain;
using FinishGenius.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinishGenius.Api.Controllers;

public record DeviceRequest(int GroupId, string? Name, string? Description, int? DeviceType, int? NetworkBridgeId, string? IpAddress);
public record CanisterAssignment(int CanisterNo, int? MaterialId);
public record IngestReading(string? Name, string? Unit, double? Value, string? TextValue, DateTime? Timestamp);
public record IngestRequest(List<IngestReading>? Readings);

/// <summary>
/// Shop-floor devices (Dashboard › Devices): scales, sensors, label printers, network bridges, dispense machines.
/// Devices push telemetry to <c>POST /api/devices/ingest</c> with their own API key.
/// </summary>
[ApiController]
[Authorize]
[Route("api/devices")]
public class DevicesController(AppDbContext db, CurrentUser me, AuditService audit) : ControllerBase
{
    private const string Entity = "Device";
    public const int CanisterCount = 16;
    public const int MaxReadingsPerRequest = 5000;
    /// <summary>A device is "online" when it has reported within this window.</summary>
    public static readonly TimeSpan OnlineWindow = TimeSpan.FromMinutes(10);

    public static string TypeLabel(DeviceType t) => t switch
    {
        DeviceType.Camera => "Camera",
        DeviceType.ScaleGrams => "Scale(g)",
        DeviceType.TempHumiditySensor => "Temperature & Humidity Sensor",
        DeviceType.ScaleKilograms => "Scale(kg)",
        DeviceType.LabelPrinter => "Label Printer",
        DeviceType.NetworkBridge => "Network Bridge",
        DeviceType.DispenseMachine => "Dispense Machine",
        _ => t.ToString(),
    };

    /// <summary>Device types that connect through a Network Bridge.</summary>
    public static bool Bridgeable(DeviceType t) => t is DeviceType.ScaleGrams or DeviceType.ScaleKilograms or DeviceType.LabelPrinter;

    private static readonly MaterialType[] TintTypes = [MaterialType.Pigment, MaterialType.Dye, MaterialType.Base];

    /// <summary>GET /api/devices?groupId=2&amp;type=5 — shared with other modules (e.g. label printer pickers).</summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int groupId, [FromQuery] int? type)
    {
        await me.EnsureGroupAsync(groupId);
        var q = db.Devices.AsNoTracking().Where(d => d.GroupId == groupId && !d.IsArchived);
        if (type is > 0) q = q.Where(d => d.DeviceType == (DeviceType)type.Value);
        var rows = await q.OrderByDescending(d => d.Id)
            .Select(d => new
            {
                d.Id, d.GroupId, GroupName = d.Group!.Name, d.Name, d.Description, d.DeviceType, d.NetworkBridgeId,
                NetworkBridgeName = db.Devices.Where(b => b.Id == d.NetworkBridgeId).Select(b => b.Name).FirstOrDefault(),
                d.IpAddress, d.LastSeenAt, d.CreatedAt,
            }).ToListAsync();
        var cutoff = DateTime.UtcNow - OnlineWindow;
        return Ok(rows.Select(d => new
        {
            d.Id, d.GroupId, d.GroupName, d.Name, d.Description, DeviceType = (int)d.DeviceType,
            DeviceTypeLabel = TypeLabel(d.DeviceType), d.NetworkBridgeId, d.NetworkBridgeName, d.IpAddress, d.LastSeenAt,
            Online = d.LastSeenAt != null && d.LastSeenAt >= cutoff, d.CreatedAt,
        }));
    }

    /// <summary>GET /api/devices/{id} — details including canisters and the API key.</summary>
    [HttpGet("{id:int}")]
    [Authorize(Roles = Access.Dashboard)]
    public async Task<IActionResult> Get(int id)
    {
        var d = await LoadAsync(id, includeCanisters: true);
        var materialIds = d.Canisters.Where(c => c.MaterialId != null).Select(c => c.MaterialId!.Value).Distinct().ToList();
        var names = await db.Materials.AsNoTracking().Where(m => materialIds.Contains(m.Id))
            .ToDictionaryAsync(m => m.Id, m => m.ProductName);
        var bridgeName = d.NetworkBridgeId == null ? null
            : await db.Devices.Where(b => b.Id == d.NetworkBridgeId).Select(b => b.Name).FirstOrDefaultAsync();
        var groupName = await db.Groups.Where(g => g.Id == d.GroupId).Select(g => g.Name).FirstAsync();
        return Ok(new
        {
            d.Id, d.GroupId, GroupName = groupName, d.Name, d.Description, DeviceType = (int)d.DeviceType,
            DeviceTypeLabel = TypeLabel(d.DeviceType), d.NetworkBridgeId, NetworkBridgeName = bridgeName, d.IpAddress,
            d.ApiKey, d.LastSeenAt, Online = d.LastSeenAt >= DateTime.UtcNow - OnlineWindow, d.CreatedAt,
            Canisters = d.Canisters.OrderBy(c => c.CanisterNo).Select(c => new
            {
                c.CanisterNo, c.MaterialId,
                MaterialName = c.MaterialId != null && names.TryGetValue(c.MaterialId.Value, out var n) ? n : null,
            }),
        });
    }

    [HttpPost]
    [Authorize(Roles = Access.Dashboard)]
    public async Task<IActionResult> Create([FromBody] DeviceRequest req)
    {
        await me.EnsureGroupAsync(req.GroupId);
        var d = new Device { GroupId = req.GroupId };
        await ApplyAsync(d, req);
        db.Devices.Add(d);
        await db.SaveChangesAsync();
        audit.Log(Entity, d.Id, "Created", $"{d.Name} ({TypeLabel(d.DeviceType)})", d.GroupId);
        await db.SaveChangesAsync();
        return Ok(new { message = "Device created.", id = d.Id, deviceType = (int)d.DeviceType });
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = Access.Dashboard)]
    public async Task<IActionResult> Update(int id, [FromBody] DeviceRequest req)
    {
        var d = await LoadAsync(id, includeCanisters: true);
        var oldGroup = d.GroupId;
        var groupId = req.GroupId > 0 ? req.GroupId : d.GroupId;
        if (groupId != d.GroupId) await me.EnsureGroupAsync(groupId);

        var before = $"{d.Name} | {TypeLabel(d.DeviceType)} | {d.Description} | {d.IpAddress} | bridge {d.NetworkBridgeId}";
        d.GroupId = groupId;
        await ApplyAsync(d, req);

        if (groupId != oldGroup)
        {
            // Canister materials and bridged devices belong to the old group.
            d.Canisters.Clear();
            await UnlinkBridgedDevicesAsync(d.Id);
        }
        if (d.DeviceType != DeviceType.DispenseMachine) d.Canisters.Clear();
        if (d.DeviceType != DeviceType.NetworkBridge) await UnlinkBridgedDevicesAsync(d.Id);

        var after = $"{d.Name} | {TypeLabel(d.DeviceType)} | {d.Description} | {d.IpAddress} | bridge {d.NetworkBridgeId}";
        audit.Log(Entity, d.Id, "Updated", before == after && groupId == oldGroup ? "No changes" : $"{before} → {after}", d.GroupId);
        await db.SaveChangesAsync();
        return Ok(new { message = "Device updated.", id = d.Id, deviceType = (int)d.DeviceType });
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = Access.Dashboard)]
    public async Task<IActionResult> Delete(int id)
    {
        var d = await LoadAsync(id);
        d.IsArchived = true;
        await UnlinkBridgedDevicesAsync(d.Id);
        audit.Log(Entity, d.Id, "Deleted", d.Name, d.GroupId);
        await db.SaveChangesAsync();
        return Ok(new { message = "Device deleted." });
    }

    /// <summary>PUT /api/devices/{id}/canisters [{ canisterNo, materialId|null }] — Canister Tint Assignment.</summary>
    [HttpPut("{id:int}/canisters")]
    [Authorize(Roles = Access.Dashboard)]
    public async Task<IActionResult> SaveCanisters(int id, [FromBody] List<CanisterAssignment> req)
    {
        var d = await LoadAsync(id, includeCanisters: true);
        if (d.DeviceType != DeviceType.DispenseMachine)
            throw ApiException.Bad("Canister assignments are only available for Dispense Machines.");
        req ??= [];
        if (req.Any(c => c.CanisterNo < 1 || c.CanisterNo > CanisterCount))
            throw ApiException.Bad($"Canister numbers must be between 1 and {CanisterCount}.");
        if (req.GroupBy(c => c.CanisterNo).Any(g => g.Count() > 1))
            throw ApiException.Bad("Each canister can only be assigned once.");

        var materialIds = req.Where(c => c.MaterialId != null).Select(c => c.MaterialId!.Value).Distinct().ToList();
        var valid = await db.Materials.AsNoTracking()
            .Where(m => materialIds.Contains(m.Id) && m.GroupId == d.GroupId && !m.IsDeleted && TintTypes.Contains(m.MaterialType))
            .Select(m => m.Id).ToListAsync();
        if (valid.Count != materialIds.Count)
            throw ApiException.Bad("Canisters can only hold Pigment, Dye or Base materials of the device's group.");

        d.Canisters.Clear();
        foreach (var c in req.Where(c => c.MaterialId != null).OrderBy(c => c.CanisterNo))
            d.Canisters.Add(new DeviceCanister { CanisterNo = c.CanisterNo, MaterialId = c.MaterialId });
        audit.Log(Entity, d.Id, "Canisters updated",
            string.Join(", ", req.Where(c => c.MaterialId != null).OrderBy(c => c.CanisterNo).Select(c => $"#{c.CanisterNo}: {c.MaterialId}")) is { Length: > 0 } s ? s : "All canisters cleared",
            d.GroupId);
        await db.SaveChangesAsync();
        return Ok(new { message = "Canister assignments saved." });
    }

    [HttpPost("{id:int}/regenerate-key")]
    [Authorize(Roles = Access.Dashboard)]
    public async Task<IActionResult> RegenerateKey(int id)
    {
        var d = await LoadAsync(id);
        d.ApiKey = Guid.NewGuid();
        audit.Log(Entity, d.Id, "API key regenerated", null, d.GroupId);
        await db.SaveChangesAsync();
        return Ok(new { message = "API key regenerated. Update the device with the new key.", apiKey = d.ApiKey });
    }

    /// <summary>
    /// GET /api/devices/{id}/metrics?date=2026-09-10&amp;tzOffset=240 — that day's readings grouped by metric name.
    /// <c>tzOffset</c> is the browser's <c>Date.getTimezoneOffset()</c>; defaults to the group's time zone.
    /// </summary>
    [HttpGet("{id:int}/metrics")]
    [Authorize(Roles = Access.Dashboard)]
    public async Task<IActionResult> Metrics(int id, [FromQuery] DateOnly? date, [FromQuery] int? tzOffset)
    {
        var d = await LoadAsync(id);
        var offset = tzOffset ?? await LocalDay.GroupOffsetMinutesAsync(db, d.GroupId);
        var day = date ?? DateOnly.FromDateTime(DateTime.UtcNow.AddMinutes(-offset));
        var from = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).AddMinutes(offset);
        var to = from.AddDays(1);

        var rows = await db.DeviceMetrics.AsNoTracking()
            .Where(m => m.DeviceId == id && m.Timestamp >= from && m.Timestamp < to)
            .OrderBy(m => m.Timestamp).ThenBy(m => m.Id)
            .Take(50_000)
            .Select(m => new { m.Name, m.Unit, m.Timestamp, m.Value, m.TextValue })
            .ToListAsync();

        var series = rows.GroupBy(r => r.Name).OrderBy(g => g.Key).Select(g => new
        {
            Name = g.Key,
            Unit = g.Select(r => r.Unit).LastOrDefault(u => !string.IsNullOrWhiteSpace(u)),
            Count = g.Count(),
            Min = g.Min(r => r.Value),
            Max = g.Max(r => r.Value),
            Latest = g.Last().Value,
            LatestText = g.Last().TextValue,
            Points = g.Select(r => new { r.Timestamp, r.Value, r.TextValue }),
        });
        return Ok(new { date = day.ToString("yyyy-MM-dd"), from, to, series });
    }

    /// <summary>
    /// POST /api/devices/ingest — telemetry from a device or network bridge.
    /// Header <c>X-Api-Key: &lt;device API key&gt;</c>; body <c>{ readings: [{ name, unit?, value?, textValue?, timestamp? }] }</c>.
    /// An empty <c>readings</c> array is a heartbeat (only updates "last seen").
    /// </summary>
    [HttpPost("ingest")]
    [AllowAnonymous]
    public async Task<IActionResult> Ingest([FromBody] IngestRequest? req)
    {
        var header = Request.Headers["X-Api-Key"].ToString();
        if (!Guid.TryParse(header, out var key))
            throw new ApiException(StatusCodes.Status401Unauthorized, "A valid X-Api-Key header is required.");
        var device = await db.Devices.FirstOrDefaultAsync(d => d.ApiKey == key && !d.IsArchived)
            ?? throw new ApiException(StatusCodes.Status401Unauthorized, "Unknown or revoked device API key.");

        var readings = req?.Readings ?? [];
        if (readings.Count > MaxReadingsPerRequest)
            throw ApiException.Bad($"Send at most {MaxReadingsPerRequest} readings per request.");

        var now = DateTime.UtcNow;
        var metrics = new List<DeviceMetric>(readings.Count);
        for (var i = 0; i < readings.Count; i++)
        {
            var r = readings[i];
            var name = Text.Clean(r.Name) ?? throw ApiException.Bad($"Reading {i + 1}: name is required.");
            if (name.Length > 100) throw ApiException.Bad($"Reading {i + 1}: name must be 100 characters or fewer.");
            if (r.Value is { } v && (double.IsNaN(v) || double.IsInfinity(v)))
                throw ApiException.Bad($"Reading {i + 1}: value must be a finite number.");
            if (r.Value == null && string.IsNullOrWhiteSpace(r.TextValue))
                throw ApiException.Bad($"Reading {i + 1}: value or textValue is required.");
            var ts = r.Timestamp switch
            {
                null => now,
                { Kind: DateTimeKind.Unspecified } t => DateTime.SpecifyKind(t, DateTimeKind.Utc),
                { } t => t.ToUniversalTime(),
            };
            if (ts > now.AddMinutes(5)) ts = now; // clock drift on the device: never store future readings
            var text = Text.Clean(r.TextValue);
            metrics.Add(new DeviceMetric
            {
                DeviceId = device.Id, Name = name, Unit = Text.Clean(r.Unit) is { } u ? (u.Length > 50 ? u[..50] : u) : null,
                Value = r.Value, TextValue = text is { Length: > 400 } ? text[..400] : text, Timestamp = ts,
            });
        }

        db.DeviceMetrics.AddRange(metrics);
        device.LastSeenAt = now;
        await db.SaveChangesAsync();
        return Ok(new { message = "Readings stored.", count = metrics.Count, deviceId = device.Id, receivedAt = now });
    }

    // -----------------------------------------------------------------------------------------

    private async Task<Device> LoadAsync(int id, bool includeCanisters = false)
    {
        var q = db.Devices.AsQueryable();
        if (includeCanisters) q = q.Include(d => d.Canisters);
        var d = await q.FirstOrDefaultAsync(x => x.Id == id && !x.IsArchived) ?? throw ApiException.NotFound("Device");
        await me.EnsureGroupAsync(d.GroupId);
        return d;
    }

    private async Task ApplyAsync(Device d, DeviceRequest req)
    {
        var name = Text.Req(req.Name, "Device Name");
        if (req.DeviceType is not { } t || !Enum.IsDefined(typeof(DeviceType), t))
            throw ApiException.Bad("Device Type is required.");
        var type = (DeviceType)t;

        var ip = Text.Clean(req.IpAddress);
        if (ip != null)
        {
            if (!(ip.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || ip.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                || !Uri.TryCreate(ip, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host))
                throw ApiException.Bad("Device IP Address must start with http:// or https://");
        }

        int? bridgeId = null;
        if (Bridgeable(type) && req.NetworkBridgeId is > 0)
        {
            if (req.NetworkBridgeId == d.Id && d.Id > 0) throw ApiException.Bad("A device cannot be its own Network Bridge.");
            var ok = await db.Devices.AnyAsync(b => b.Id == req.NetworkBridgeId && b.GroupId == d.GroupId && !b.IsArchived && b.DeviceType == DeviceType.NetworkBridge);
            if (!ok) throw ApiException.Bad("Network Bridge must be a Network Bridge device of the same group.");
            bridgeId = req.NetworkBridgeId;
        }

        var description = Text.Clean(req.Description);
        if (name.Length > 200) throw ApiException.Bad("Device Name must be 200 characters or fewer.");
        d.Name = name;
        d.Description = description;
        d.DeviceType = type;
        d.NetworkBridgeId = bridgeId;
        d.IpAddress = ip;
    }

    private async Task UnlinkBridgedDevicesAsync(int bridgeId)
    {
        if (bridgeId == 0) return;
        var bridged = await db.Devices.Where(x => x.NetworkBridgeId == bridgeId).ToListAsync();
        foreach (var b in bridged) b.NetworkBridgeId = null;
    }
}
