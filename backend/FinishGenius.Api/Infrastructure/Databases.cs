namespace FinishGenius.Api.Infrastructure;

/// <summary>One database the user can sign in to (e.g. "Dev", "Prod"), from the <c>Databases</c> config section.</summary>
public class DatabaseTarget
{
    public string Key { get; init; } = "";
    public string Label { get; init; } = "";
    public string ConnectionString { get; init; } = "";
    /// <summary>Create/update the fg schema and seed base data on startup.</summary>
    public bool AutoMigrate { get; init; }
    /// <summary>Live data: the UI shows a warning on sign-in and a highlighted badge in the header.</summary>
    public bool Production { get; init; }
    /// <summary>
    /// The old Finish Genius database used as-is: the app reads and writes its dbo tables through
    /// <see cref="FinishGenius.Api.Data.LegacyAppDbContext"/> (screens not mapped yet say so) and never migrates it.
    /// </summary>
    public bool Legacy { get; init; }
}

/// <summary>
/// The configured databases. <c>appsettings.Local.json</c>:
/// <code>"Databases": { "Dev": { "Label": "Development", "ConnectionString": "..." }, "Prod": { ... } }, "Database": { "Default": "Dev", "Use": "Production" }</code>
/// A plain <c>ConnectionStrings:Default</c> (older config) still works as a single "Dev" database.
/// </summary>
public class DatabaseCatalog
{
    /// <summary>JWT claim carrying the session's database key.</summary>
    public const string Claim = "db";

    private readonly IConfiguration _config;
    private readonly string[] _productionHosts;

    public IReadOnlyList<DatabaseTarget> All { get; }
    public DatabaseTarget Default { get; }

    public DatabaseCatalog(IConfiguration config)
    {
        _config = config;
        var autoMigrate = config.GetValue("Database:AutoMigrate", true);
        var list = config.GetSection("Databases").GetChildren()
            .Where(s => IsSet(s["ConnectionString"]))
            .Select(s => new DatabaseTarget
            {
                Key = s.Key,
                Label = s["Label"] ?? s.Key,
                ConnectionString = s["ConnectionString"]!,
                AutoMigrate = !s.GetValue("Legacy", false) && s.GetValue("AutoMigrate", autoMigrate),
                Production = s.GetValue("Production", false),
                Legacy = s.GetValue("Legacy", false),
            })
            .ToList();

        var single = config.GetConnectionString("Default");
        if (list.Count == 0 && IsSet(single))
            list.Add(new DatabaseTarget { Key = "Dev", Label = "Development", ConnectionString = single!, AutoMigrate = autoMigrate });

        if (list.Count == 0)
            throw new InvalidOperationException("No database is configured. Copy appsettings.Local.example.json to appsettings.Local.json and fill in \"Databases\".");
        All = list;
        Default = Find(config["Database:Default"]) ?? list[0];
        _productionHosts = config.GetSection("Database:ProductionHosts").Get<string[]>() ?? [];
    }

    /// <summary>
    /// <c>Database:Use</c> — the database this site signs in to, by key or label ("Prod", "Production", "Dev",
    /// "Development"). Empty = decided by the site address (<see cref="ForHost"/>). Read on every request, so a change in
    /// appsettings.Local.json applies without a restart.
    /// </summary>
    public DatabaseTarget? Forced
    {
        get
        {
            var use = _config["Database:Use"]?.Trim();
            if (string.IsNullOrEmpty(use)) return null;
            return Find(use) ?? All.FirstOrDefault(d => d.Label.Equals(use, StringComparison.OrdinalIgnoreCase))
                ?? throw new ApiException(StatusCodes.Status503ServiceUnavailable,
                    $"Database:Use is \"{use}\", but no database has that name. Configured: {string.Join(", ", All.Select(d => $"{d.Key} ({d.Label})"))}.");
        }
    }

    /// <summary>
    /// The database a sign-in opens. <c>Database:Use</c> wins when set; otherwise the address the browser used decides:
    /// a <c>Database:ProductionHosts</c> entry opens the Production database, anything else the development one. An entry
    /// without a port ("35.196.141.157", "app.finishgenius.net") matches that address on every port; an entry with a port
    /// ("host:9001") matches only that port.
    /// </summary>
    public DatabaseTarget ForHost(HostString host)
    {
        if (Forced is { } forced) return forced;
        var production = All.FirstOrDefault(d => d.Production);
        var development = Default.Production ? All.FirstOrDefault(d => !d.Production) ?? Default : Default;
        if (production == null || !host.HasValue) return development;

        var port = host.Port ?? 0;
        var isProduction = _productionHosts.Any(entry =>
        {
            var e = HostString.FromUriComponent(entry.Trim());
            if (!e.Host.Equals(host.Host, StringComparison.OrdinalIgnoreCase)) return false;
            return !e.Port.HasValue || e.Port == port;
        });
        return isProduction ? production : development;
    }

    public DatabaseTarget? Find(string? key) =>
        string.IsNullOrWhiteSpace(key) ? null : All.FirstOrDefault(d => d.Key.Equals(key.Trim(), StringComparison.OrdinalIgnoreCase));

    private static bool IsSet(string? connectionString) =>
        !string.IsNullOrWhiteSpace(connectionString) && !connectionString.Contains("__SET_ME__");
}

/// <summary>
/// Which database the current request works on: the session token's "db" claim, otherwise (sign-in) the one for the
/// address the browser used (<see cref="DatabaseCatalog.ForHost"/>). Command-line tools and startup pick one with <see cref="Use"/>.
/// </summary>
public class DatabaseSelector(IHttpContextAccessor http, DatabaseCatalog catalog)
{
    private DatabaseTarget? _chosen;

    public void Use(DatabaseTarget target) => _chosen = target;

    public DatabaseTarget Current => _chosen ??= Resolve();

    private DatabaseTarget Resolve()
    {
        var ctx = http.HttpContext;
        if (ctx == null) return catalog.Default;

        if (ctx.User.Identity?.IsAuthenticated == true)
        {
            var key = ctx.User.FindFirst(DatabaseCatalog.Claim)?.Value;
            if (key == null) return catalog.Forced ?? catalog.Default; // sessions opened before the database choice existed
            var session = catalog.Find(key) ?? throw new ApiException(StatusCodes.Status401Unauthorized,
                "The database for this session is no longer configured. Please sign in again.");
            // Database:Use was changed after this session signed in: send the user back to sign-in.
            if (catalog.Forced is { } forced && !forced.Key.Equals(session.Key, StringComparison.OrdinalIgnoreCase))
                throw new ApiException(StatusCodes.Status401Unauthorized, $"This site now uses the {forced.Label} database. Please sign in again.");
            return session;
        }

        return catalog.ForHost(ctx.Request.Host);
    }
}
