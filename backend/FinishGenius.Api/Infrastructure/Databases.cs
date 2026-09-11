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
}

/// <summary>
/// The configured databases. <c>appsettings.Local.json</c>:
/// <code>"Databases": { "Dev": { "Label": "Development", "ConnectionString": "..." }, "Prod": { ... } }, "Database": { "Default": "Dev" }</code>
/// A plain <c>ConnectionStrings:Default</c> (older config) still works as a single "Dev" database.
/// </summary>
public class DatabaseCatalog
{
    /// <summary>Sign-in request header naming the database to open the session on.</summary>
    public const string Header = "X-FG-Database";
    /// <summary>JWT claim carrying the session's database key.</summary>
    public const string Claim = "db";

    public IReadOnlyList<DatabaseTarget> All { get; }
    public DatabaseTarget Default { get; }

    public DatabaseCatalog(IConfiguration config)
    {
        var autoMigrate = config.GetValue("Database:AutoMigrate", true);
        var list = config.GetSection("Databases").GetChildren()
            .Where(s => IsSet(s["ConnectionString"]))
            .Select(s => new DatabaseTarget
            {
                Key = s.Key,
                Label = s["Label"] ?? s.Key,
                ConnectionString = s["ConnectionString"]!,
                AutoMigrate = s.GetValue("AutoMigrate", autoMigrate),
                Production = s.GetValue("Production", false),
            })
            .ToList();

        var single = config.GetConnectionString("Default");
        if (list.Count == 0 && IsSet(single))
            list.Add(new DatabaseTarget { Key = "Dev", Label = "Development", ConnectionString = single!, AutoMigrate = autoMigrate });

        if (list.Count == 0)
            throw new InvalidOperationException("No database is configured. Copy appsettings.Local.example.json to appsettings.Local.json and fill in \"Databases\".");
        All = list;
        Default = Find(config["Database:Default"]) ?? list[0];
    }

    public DatabaseTarget? Find(string? key) =>
        string.IsNullOrWhiteSpace(key) ? null : All.FirstOrDefault(d => d.Key.Equals(key.Trim(), StringComparison.OrdinalIgnoreCase));

    private static bool IsSet(string? connectionString) =>
        !string.IsNullOrWhiteSpace(connectionString) && !connectionString.Contains("__SET_ME__");
}

/// <summary>
/// Which database the current request works on: the session token's "db" claim, otherwise (sign-in) the
/// <see cref="DatabaseCatalog.Header"/> header, otherwise the default. Command-line tools and startup pick one with <see cref="Use"/>.
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
            if (key == null) return catalog.Default; // sessions opened before the database choice existed
            return catalog.Find(key) ?? throw new ApiException(StatusCodes.Status401Unauthorized,
                "The database for this session is no longer configured. Please sign in again.");
        }

        var requested = ctx.Request.Headers[DatabaseCatalog.Header].ToString();
        if (string.IsNullOrWhiteSpace(requested)) return catalog.Default;
        return catalog.Find(requested) ?? throw ApiException.Bad($"Unknown database \"{requested}\".");
    }
}
