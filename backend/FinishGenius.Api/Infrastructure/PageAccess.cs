using System.Security.Claims;
using System.Text.Json;
using FinishGenius.Api.Domain;

namespace FinishGenius.Api.Infrastructure;

/// <summary>
/// Pages and tabs the owner account can turn off per role ("Page Access"). A role never gets more than its built-in
/// access (<see cref="Access"/>): the owner only takes pages and tabs away. Page keys match frontend/src/lib/access.ts;
/// tab keys match the tabs of each page.
/// </summary>
public static class PageCatalog
{
    public sealed record Tab(string Key, string Label);
    public sealed record Page(string Key, string Label, string Section, string[] DefaultRoles, Tab[] Tabs);

    private const string SA = Roles.SystemAdmin, SUP = Roles.SupportAgent, GA = Roles.GroupAdmin, P = Roles.FGPro, PP = Roles.FGProPlus;
    private static readonly string[] Everyone = [P, PP, GA, SUP, SA];

    public static readonly Page[] Pages =
    [
        new("dashboard", "Dashboard", "Operations", [PP, GA, SA],
            [new("processList", "My Work Processes"), new("deviceList", "Devices"), new("departmentManagement", "Department Management")]),
        new("myWork", "My Work", "Operations", [PP, GA, SA], [new("workProgress", "My Work Progress"), new("processManagement", "Processes Management")]),
        new("workInstructions", "Work Instructions", "Operations", [P, PP, GA, SA], []),
        new("processSteps", "Process Steps", "Processes", [P, GA, SA], []),
        new("processSchedules", "Process Schedules", "Processes", [P, GA, SA], []),
        new("materialQuantities", "Material Quantities", "Processes", [P, GA, SA], []),
        new("pricing", "Pricing", "Processes", [P, GA, SA], []),
        new("materials", "Equipment & Materials", "Formulation", [P, GA, SA],
        [
            new("base", "Base Materials"), new("pigment", "Pigments"), new("dye", "Dyes"), new("equipment", "Equipment"),
            new("sundry-items", "Sundry Items"), new("reorder-materials", "Reorder Materials"), new("order-history", "Order History"),
            new("vendors", "Vendors"), new("reports", "Reports"),
        ]),
        new("formulas", "Formulas", "Formulation", [P, GA, SA], []),
        new("photos", "Photo Gallery", "Formulation", Everyone, []),
        new("groups", "Groups", "Administration", Everyone, []),
        new("users", "Users", "Administration", [GA, SA], []),
        new("materialCategories", "Material Categories", "Administration", [GA, SA],
        [
            new("1", "Base"), new("2", "Pigment"), new("3", "Dye"), new("4", "Equipment"), new("5", "Sundry"), new("6", "Formula"), new("7", "Product"),
        ]),
        new("subSteps", "Sub Step Setup", "Administration", [SA], []),
        new("import", "Import", "Administration", [GA, SA], []),
        new("settings", "System Settings", "Administration", [SA], []),
        new("messages", "DPM Center & Help (messages)", "Header", Everyone, []),
    ];

    public static Page? Find(string key) => Pages.FirstOrDefault(p => p.Key == key);

    /// <summary>
    /// API areas that belong to one page (or a few). A request is refused when the owner turned all of them off for the
    /// caller. APIs shared by many pages (materials, categories, documents, groups, files…) are not listed.
    /// </summary>
    public static readonly (string Prefix, string[] Pages)[] Apis =
    [
        ("/api/dashboard", ["dashboard"]),
        ("/api/departments", ["dashboard", "myWork"]),
        ("/api/my-work", ["myWork", "dashboard"]),
        ("/api/work-instructions", ["workInstructions"]),
        ("/api/process-steps", ["processSteps", "processSchedules"]),
        ("/api/process-schedules", ["processSchedules", "materialQuantities", "pricing", "myWork"]),
        ("/api/formulas", ["formulas"]),
        ("/api/dispensing", ["formulas"]),
        ("/api/photos", ["photos"]),
        ("/api/purchase-orders", ["materials"]),
        ("/api/inventory", ["materials"]),
        ("/api/vendors", ["materials"]),
        ("/api/material-locations", ["materials"]),
        ("/api/material-reports", ["materials"]),
        ("/api/material-import", ["import", "materials"]),
        ("/api/sub-steps", ["subSteps"]),
        ("/api/users", ["users"]),
        ("/api/messages", ["messages", "myWork"]),
        ("/api/settings", ["settings"]),
    ];

    /// <summary>Used by pages everyone keeps (the profile page).</summary>
    public static readonly string[] AlwaysAllowed = ["/api/users/me"];
}

public sealed class PageAccessSettings
{
    /// <summary>Page key → roles it is on for (only pages that differ from their built-in access).</summary>
    public Dictionary<string, string[]> Pages { get; set; } = [];
    /// <summary>"page.tab" → roles it is on for (only tabs that differ).</summary>
    public Dictionary<string, string[]> Tabs { get; set; } = [];
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

/// <summary>
/// The owner's page / tab switches, saved in App_Data/page-access.json (kept by deploy/Install-IIS.ps1), so they apply
/// to every database.
/// </summary>
public class PageAccessService
{
    public const string SettingsFile = "App_Data/page-access.json";

    private readonly string _path;
    private readonly ILogger<PageAccessService> _log;
    private readonly object _writeLock = new();
    private volatile PageAccessSettings _settings;

    public PageAccessService(IWebHostEnvironment env, ILogger<PageAccessService> log)
    {
        _log = log;
        _path = Path.Combine(env.ContentRootPath, SettingsFile);
        _settings = Load();
    }

    private PageAccessSettings Load()
    {
        try
        {
            if (File.Exists(_path)) return JsonSerializer.Deserialize<PageAccessSettings>(File.ReadAllText(_path)) ?? new();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            _log.LogError(e, "Page access settings {Path} could not be read; every page uses its built-in access", _path);
        }
        return new();
    }

    /// <summary>Roles the page is on for.</summary>
    public string[] PageRoles(PageCatalog.Page page) =>
        _settings.Pages.TryGetValue(page.Key, out var roles) ? page.DefaultRoles.Intersect(roles ?? []).ToArray() : page.DefaultRoles;

    /// <summary>Roles the tab is on for (the page itself must be on too).</summary>
    public string[] TabRoles(PageCatalog.Page page, string tab) =>
        _settings.Tabs.TryGetValue($"{page.Key}.{tab}", out var roles) ? page.DefaultRoles.Intersect(roles ?? []).ToArray() : page.DefaultRoles;

    /// <summary>Pages and "page.tab" keys a user with these roles does not see.</summary>
    public (List<string> Pages, List<string> Tabs) HiddenFor(IReadOnlyCollection<string> roles)
    {
        var pages = new List<string>();
        var tabs = new List<string>();
        foreach (var page in PageCatalog.Pages)
        {
            var seeing = PageRoles(page).Where(roles.Contains).ToList();
            if (seeing.Count == 0)
            {
                pages.Add(page.Key);
                continue;
            }
            foreach (var tab in page.Tabs)
                if (!TabRoles(page, tab.Key).Any(seeing.Contains)) tabs.Add($"{page.Key}.{tab.Key}");
        }
        return (pages, tabs);
    }

    /// <summary>
    /// False when the API belongs to pages this caller would normally see but the owner turned all of them off.
    /// (Pages a role never had stay governed by the controllers' own role checks.)
    /// </summary>
    public bool ApiAllowed(PathString path, IReadOnlyCollection<string> roles)
    {
        if (PageCatalog.AlwaysAllowed.Any(a => path.StartsWithSegments(a))) return true;
        foreach (var (prefix, keys) in PageCatalog.Apis)
        {
            if (!path.StartsWithSegments(prefix)) continue;
            var normally = keys.Select(PageCatalog.Find).OfType<PageCatalog.Page>().Where(p => p.DefaultRoles.Any(roles.Contains)).ToList();
            return normally.Count == 0 || normally.Any(p => PageRoles(p).Any(roles.Contains));
        }
        return true;
    }

    /// <summary>Saves the switches (roles outside a page's built-in access are ignored) and applies them at once.</summary>
    public void Save(IDictionary<string, string[]?> pages, IDictionary<string, string[]?> tabs, string user)
    {
        var settings = new PageAccessSettings { UpdatedAt = DateTime.UtcNow, UpdatedBy = user };
        foreach (var (key, roles) in pages)
        {
            var page = PageCatalog.Find(key) ?? throw ApiException.Bad($"Unknown page \"{key}\".");
            var on = page.DefaultRoles.Where(r => roles?.Contains(r) == true).ToArray();
            if (on.Length != page.DefaultRoles.Length) settings.Pages[page.Key] = on;
        }
        foreach (var (key, roles) in tabs)
        {
            var dot = key.IndexOf('.');
            var page = dot > 0 ? PageCatalog.Find(key[..dot]) : null;
            if (page == null || page.Tabs.All(t => t.Key != key[(dot + 1)..])) throw ApiException.Bad($"Unknown tab \"{key}\".");
            var on = page.DefaultRoles.Where(r => roles?.Contains(r) == true).ToArray();
            if (on.Length != page.DefaultRoles.Length) settings.Tabs[key] = on;
        }
        lock (_writeLock)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, _path, overwrite: true);
            _settings = settings;
        }
        _log.LogWarning("Page access changed by {User}: {Pages} pages and {Tabs} tabs restricted", user, settings.Pages.Count, settings.Tabs.Count);
    }

    /// <summary>The catalog with the current switches, for the Page Access page.</summary>
    public object Describe()
    {
        var settings = _settings;
        return new
        {
            Roles = Roles.All.Select(r => new { Key = r, Label = Roles.Labels[r] }),
            Sections = PageCatalog.Pages.GroupBy(p => p.Section).Select(g => new
            {
                Title = g.Key,
                Pages = g.Select(p => new
                {
                    p.Key, p.Label, p.DefaultRoles, AllowedRoles = PageRoles(p),
                    Tabs = p.Tabs.Select(t => new { t.Key, t.Label, AllowedRoles = TabRoles(p, t.Key) }),
                }),
            }),
            settings.UpdatedAt,
            settings.UpdatedBy,
        };
    }
}

/// <summary>
/// The owner account: configured on the server (Owner:Username + Owner:PasswordHash in appsettings.Local.json), never
/// stored in any database, so it signs in the same way on Dev and Prod. It sees every page and is the only account that
/// can open Page Access. It has no user record, so it does not change business data (see <see cref="PageAccessMiddleware"/>).
/// </summary>
public class OwnerAccount(IConfiguration config)
{
    public const string Claim = "owner";

    public string? Username => config["Owner:Username"]?.Trim() is { Length: > 0 } name ? name : null;

    public bool Matches(string? username, string? password)
    {
        var hash = config["Owner:PasswordHash"];
        return Username != null && !string.IsNullOrEmpty(hash)
               && string.Equals((username ?? "").Trim(), Username, StringComparison.OrdinalIgnoreCase)
               && Passwords.Verify(hash, password ?? "");
    }
}

/// <summary>
/// Refuses API calls for pages the owner turned off for the caller's roles, and write calls by the owner account outside
/// sign-in, Page Access and System Settings (records need a real user).
/// </summary>
public class PageAccessMiddleware(RequestDelegate next)
{
    private static readonly string[] OwnerWritable = ["/api/auth", "/api/access", "/api/settings"];

    public async Task InvokeAsync(HttpContext ctx, PageAccessService access)
    {
        var path = ctx.Request.Path;
        var user = ctx.User;
        if (path.StartsWithSegments("/api") && user.Identity?.IsAuthenticated == true)
        {
            string? refusal = null;
            if (user.HasClaim(OwnerAccount.Claim, "1"))
            {
                var method = ctx.Request.Method;
                var reading = HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method);
                if (!reading && !OwnerWritable.Any(p => path.StartsWithSegments(p)))
                    refusal = "The owner account can open every page but does not change data (it has no user record). Sign in with a regular account to make changes.";
            }
            else if (!access.ApiAllowed(path, user.FindAll(ClaimTypes.Role).Select(c => c.Value).ToHashSet()))
            {
                refusal = "This page has been turned off for your role.";
            }
            if (refusal != null)
            {
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                await ctx.Response.WriteAsJsonAsync(new { message = refusal });
                return;
            }
        }
        await next(ctx);
    }
}
