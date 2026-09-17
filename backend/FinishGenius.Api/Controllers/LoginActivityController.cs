using FinishGenius.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinishGenius.Api.Controllers;

/// <summary>Login Activity: who signed in, from which IP address and location, and when. Owner account only.</summary>
[ApiController]
[Authorize]
[Route("api/login-activity")]
public class LoginActivityController(LoginAuditService logins, DatabaseCatalog databases, CurrentUser me) : ControllerBase
{
    /// <summary>
    /// GET /api/login-activity?from=2026-09-01&amp;to=2026-09-15&amp;search=&amp;result=success|failed&amp;database=Prod&amp;take=1000
    /// <c>from</c> / <c>to</c> are UTC instants; <c>to</c> is exclusive.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] string? search,
        [FromQuery] string? result, [FromQuery] string? database, [FromQuery] int take = 1000, CancellationToken token = default)
    {
        EnsureOwner();
        var (rows, total) = await logins.ReadAsync(
            new LoginAuditService.Query(Utc(from), Utc(to), search, result, database, take), token);
        var target = logins.Target;
        return Ok(new
        {
            Items = rows,
            Total = total,
            StoredIn = target == null ? null : new { target.Key, target.Label, Table = LoginAuditService.TableName },
            Databases = databases.All.Select(d => new { d.Key, d.Label }),
        });
    }

    private static DateTime? Utc(DateTime? value) => value switch
    {
        null => null,
        { Kind: DateTimeKind.Utc } v => v,
        { Kind: DateTimeKind.Local } v => v.ToUniversalTime(),
        var v => DateTime.SpecifyKind(v.Value, DateTimeKind.Utc),
    };

    private void EnsureOwner()
    {
        if (!me.IsOwner) throw new ApiException(StatusCodes.Status403Forbidden, "Only the owner account can view login activity.");
    }
}
