using System.Globalization;
using System.Text.Json;
using FinishGenius.Api.Controllers;
using FinishGenius.Api.Data;
using FinishGenius.Api.Domain;
using FinishGenius.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace FinishGenius.Api.Services;

/// <summary>
/// Hardware integration point. The legacy app talked to dispense machines, scales and label printers through a SignalR hub
/// connected to the shop's Network Bridge (UDCP protocol). Here every hardware action becomes a <see cref="DeviceCommand"/>
/// that the bridge polls (<c>GET /api/bridge/commands</c>) and answers (<c>POST /api/bridge/commands/{id}/result</c>);
/// the result is applied to the formula exactly like the legacy success path.
/// </summary>
public class DeviceCommandService(AppDbContext db)
{
    public static readonly TimeSpan ScaleTimeout = TimeSpan.FromSeconds(30);
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Adds (does not save) a command for <paramref name="target"/>, relayed by its Network Bridge.</summary>
    public async Task<DeviceCommand> QueueAsync(Device target, string type, object? payload, int? formulaId, int? userId, TimeSpan ttl)
    {
        int bridgeId;
        if (target.DeviceType == DeviceType.NetworkBridge) bridgeId = target.Id;
        else if (target.NetworkBridgeId is { } b && await db.Devices.AnyAsync(d => d.Id == b && !d.IsArchived && d.DeviceType == DeviceType.NetworkBridge))
            bridgeId = b;
        else
            throw ApiException.Bad(type switch
            {
                DeviceCommandTypes.Dispense => "Please select Network Bridge.",
                DeviceCommandTypes.GetWeight or DeviceCommandTypes.Tare => $"Network bridge could not detect scale. \"{target.Name}\" is not connected to a Network Bridge (Dashboard › Devices).",
                DeviceCommandTypes.PrintLabel => $"The label printer \"{target.Name}\" is not connected to a Network Bridge (Dashboard › Devices).",
                _ => $"\"{target.Name}\" is not connected to a Network Bridge.",
            });

        var cmd = new DeviceCommand
        {
            GroupId = target.GroupId, DeviceId = target.Id, BridgeDeviceId = bridgeId, CommandType = type,
            Payload = payload == null ? null : JsonSerializer.Serialize(payload, Json), FormulaId = formulaId,
            CreatedBy = userId is > 0 ? userId : null, ExpiresAt = DateTime.UtcNow + ttl,
        };
        db.DeviceCommands.Add(cmd);
        return cmd;
    }

    /// <summary>Marks an open command whose time is up as expired; returns true when it changed.</summary>
    public static bool Expire(DeviceCommand c)
    {
        if (!DeviceCommandStatuses.IsOpen(c.Status) || DateTime.UtcNow <= c.ExpiresAt) return false;
        c.Status = DeviceCommandStatuses.Expired;
        c.ResultMessage ??= c.SentAt == null
            ? "No response from the network bridge (it did not pick up the request). Check that the bridge is running."
            : "Timeout. No response received from the network bridge.";
        c.CompletedAt = DateTime.UtcNow;
        return true;
    }

    /// <summary>Stores the bridge's answer and applies the legacy success side effects.</summary>
    public async Task ApplyResultAsync(DeviceCommand c, bool success, string? message, string? data)
    {
        c.Status = success ? DeviceCommandStatuses.Succeeded : DeviceCommandStatuses.Failed;
        c.ResultMessage = message is { Length: > 4000 } ? message[..4000] : message;
        c.ResultData = data is { Length: > 4000 } ? data[..4000] : data;
        c.CompletedAt = DateTime.UtcNow;

        var userName = c.CreatedBy == null ? null : await db.Users.Where(u => u.Id == c.CreatedBy).Select(u => u.Username).FirstOrDefaultAsync();
        if (c.CommandType == DeviceCommandTypes.Dispense && c.FormulaId is { } formulaId)
        {
            if (success) await ApplyDispenseSuccessAsync(formulaId, c, userName);
            else
            {
                var groupId = await db.Formulas.Where(f => f.Id == formulaId).Select(f => f.GroupId).FirstOrDefaultAsync();
                FormulaHistory.Add(db, formulaId, groupId, c.CreatedBy, userName, "Dispense failed", string.IsNullOrWhiteSpace(message) ? "Dispensing failed" : message);
            }
        }
        if (c.CommandType == DeviceCommandTypes.Purge && data != null)
            StorePurgeResults(c.BridgeDeviceId, ParsePurgeResults(data, "Manual"));
    }

    /// <summary>Legacy Dispense() success path: amounts to dispense move into Total Dispensed, undo snapshot, nozzle flag reset.</summary>
    private async Task ApplyDispenseSuccessAsync(int formulaId, DeviceCommand c, string? userName)
    {
        var f = await db.Formulas.Include(x => x.Ingredients).ThenInclude(i => i.Material).FirstOrDefaultAsync(x => x.Id == formulaId);
        if (f == null) return;

        // Snapshot of the state before the dispense (legacy spDispenseFormulationMaterialsTrack 'DespenseTrackByFormulaId').
        db.FormulaDispenseSnapshots.RemoveRange(await db.FormulaDispenseSnapshots.Where(s => s.FormulaId == formulaId).ToListAsync());
        foreach (var i in f.Ingredients)
            db.FormulaDispenseSnapshots.Add(new FormulaDispenseSnapshot
            {
                FormulaId = f.Id, IngredientId = i.Id, Grams = i.Grams, DispenseAmount = i.DispenseAmount, DispensedGrams = i.DispensedGrams, IsDispensed = i.IsDispensed,
            });

        var lines = new List<string>();
        foreach (var i in f.Ingredients.Where(i => i.DispenseAmount > 0).OrderBy(i => i.Sequence))
        {
            lines.Add($"{MaterialTypes.Label(i.Material?.MaterialType ?? MaterialType.Base)} {i.Material?.ProductName}: {FormulaUnits.G(i.DispenseAmount)} g");
            i.DispensedGrams = FormulaCalc.R(i.DispensedGrams + i.DispenseAmount, 4);
            i.DispenseAmount = 0;
        }
        f.DispenserId = c.DeviceId;

        var setting = await db.DispenseSettings.FirstOrDefaultAsync(s => s.GroupId == f.GroupId);
        if (setting == null)
        {
            setting = new DispenseSetting { GroupId = f.GroupId };
            db.DispenseSettings.Add(setting);
        }
        setting.IsNozzleCleaned = false;
        setting.LastDispensedAt = DateTime.UtcNow;

        var device = await db.Devices.Where(d => d.Id == c.DeviceId).Select(d => d.Name).FirstOrDefaultAsync();
        FormulaHistory.Add(db, f.Id, f.GroupId, c.CreatedBy, userName, "Dispense using network bridge",
            $"Dispense Machine: {device}\n" + string.Join("\n", lines), null, $"Device Id {c.DeviceId}");
    }

    public record PurgeResultInput(int CanisterNumber, bool Success, string? Message, DateTime? ExecutedAt, string? PurgeType);

    /// <summary>Legacy sp_SavePurgeSuccess / sp_SavePurgeFailed.</summary>
    public int StorePurgeResults(int bridgeId, IEnumerable<PurgeResultInput> results)
    {
        var n = 0;
        foreach (var r in results)
        {
            var msg = r.Message is { Length: > 400 } ? r.Message[..400] : r.Message;
            if (r.Success)
                db.PurgeSuccesses.Add(new PurgeSuccess
                {
                    BridgeDeviceId = bridgeId, CanisterNumber = r.CanisterNumber, Message = msg ?? "OK",
                    PurgeType = string.IsNullOrWhiteSpace(r.PurgeType) ? "Auto" : r.PurgeType.Trim(), ExecutedAt = r.ExecutedAt ?? DateTime.UtcNow,
                });
            else
                db.PurgeFailures.Add(new PurgeFailure { BridgeDeviceId = bridgeId, CanisterNumber = r.CanisterNumber, Message = msg, IsActive = true });
            n++;
        }
        return n;
    }

    /// <summary>Accepts <c>{ "results": [ { canisterNumber, success, message } ] }</c> or a bare array; anything else is ignored.</summary>
    public static List<PurgeResultInput> ParsePurgeResults(string data, string defaultType)
    {
        try
        {
            using var doc = JsonDocument.Parse(data);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("results", out var r)) root = r;
            if (root.ValueKind != JsonValueKind.Array) return [];
            var list = root.Deserialize<List<PurgeResultInput>>(Json) ?? [];
            return list.Select(x => x with { PurgeType = x.PurgeType ?? defaultType }).ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>Weight in grams from a GetWeight result ("123.4", "?" = unstable, or <c>{ "weight": 123.4 }</c>); kg scales ×1000.</summary>
    public static (decimal? Grams, string? Error) ParseWeight(string? data, DeviceType scaleType)
    {
        var text = data?.Trim();
        if (string.IsNullOrEmpty(text)) return (null, "Network bridge could not detect scale.");
        if (text == "?") return (null, "Unstable value on scale.");
        if (text.StartsWith('{'))
        {
            try
            {
                using var doc = JsonDocument.Parse(text);
                if (doc.RootElement.TryGetProperty("weight", out var w))
                    text = w.ValueKind == JsonValueKind.Number ? w.GetRawText() : w.GetString();
            }
            catch (JsonException) { }
        }
        if (!decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            return (null, "Network bridge could not detect scale.");
        if (scaleType == DeviceType.ScaleKilograms) value *= 1000m;
        return (FormulaCalc.R(value, 4), null);
    }
}
