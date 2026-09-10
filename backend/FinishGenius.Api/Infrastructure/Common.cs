using System.Text.Json;
using FinishGenius.Api.Data;
using FinishGenius.Api.Domain;

namespace FinishGenius.Api.Infrastructure;

/// <summary>Throw from anywhere to return <c>{ message }</c> with the given HTTP status.</summary>
public class ApiException(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
    public static ApiException NotFound(string what = "Record") => new(StatusCodes.Status404NotFound, $"{what} not found.");
    public static ApiException Bad(string message) => new(StatusCodes.Status400BadRequest, message);
}

public class ApiExceptionMiddleware(RequestDelegate next, ILogger<ApiExceptionMiddleware> log)
{
    public async Task Invoke(HttpContext ctx)
    {
        try
        {
            await next(ctx);
        }
        catch (ApiException ex)
        {
            await Write(ctx, ex.Status, ex.Message);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Unhandled error on {Path}", ctx.Request.Path);
            await Write(ctx, StatusCodes.Status500InternalServerError, "An unexpected error occurred. Please try again or contact support.");
        }
    }

    private static async Task Write(HttpContext ctx, int status, string message)
    {
        if (ctx.Response.HasStarted) return;
        ctx.Response.Clear();
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/json";
        await ctx.Response.WriteAsync(JsonSerializer.Serialize(new { message }));
    }
}

public class PagedResult<T>
{
    public List<T> Items { get; set; } = [];
    public int Total { get; set; }
}

public class AuditService(AppDbContext db, CurrentUser me)
{
    /// <summary>Adds an audit row to the change tracker; it is saved with the caller's SaveChanges.</summary>
    public void Log(string entityType, int entityId, string action, string? details = null, int? groupId = null)
    {
        db.AuditLogs.Add(new AuditLog
        {
            EntityType = entityType,
            EntityId = entityId,
            Action = action,
            Details = details is { Length: > 4000 } ? details[..4000] : details,
            GroupId = groupId,
            UserId = me.Id == 0 ? null : me.Id,
            UserName = me.UserName,
        });
    }
}

public static class Text
{
    public static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    public static string Req(string? s, string field) =>
        string.IsNullOrWhiteSpace(s) ? throw ApiException.Bad($"{field} is required.") : s.Trim();
}

public static class MaterialTypes
{
    public static string Label(MaterialType t) => t switch
    {
        MaterialType.Base => "Base",
        MaterialType.Pigment => "Pigment",
        MaterialType.Dye => "Dye",
        MaterialType.Equipment => "Equipment",
        MaterialType.Sundry => "Sundry",
        MaterialType.Formula => "Formula",
        MaterialType.Product => "Product",
        _ => t.ToString(),
    };
}
