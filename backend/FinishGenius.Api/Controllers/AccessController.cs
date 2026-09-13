using FinishGenius.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinishGenius.Api.Controllers;

/// <summary>Page Access: which pages and tabs each role sees. Owner account only.</summary>
[ApiController]
[Authorize]
[Route("api/access")]
public class AccessController(PageAccessService access, CurrentUser me) : ControllerBase
{
    public record SaveRequest(Dictionary<string, string[]?>? Pages, Dictionary<string, string[]?>? Tabs);

    /// <summary>GET /api/access — every page and tab with the roles it is on for.</summary>
    [HttpGet]
    public IActionResult Get()
    {
        EnsureOwner();
        return Ok(access.Describe());
    }

    /// <summary>PUT /api/access { pages: { key: roles[] }, tabs: { "page.tab": roles[] } }</summary>
    [HttpPut]
    public IActionResult Save(SaveRequest r)
    {
        EnsureOwner();
        access.Save(r.Pages ?? [], r.Tabs ?? [], me.UserName);
        return Ok(new { message = "Page access saved. Users see the change when their page refreshes (within 2 minutes)." });
    }

    private void EnsureOwner()
    {
        if (!me.IsOwner) throw new ApiException(StatusCodes.Status403Forbidden, "Only the owner account can change page access.");
    }
}
