using System.IO.Compression;
using System.Text.Json.Serialization;
using FinishGenius.Api.Data;
using Microsoft.AspNetCore.ResponseCompression;
using FinishGenius.Api.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

// dotnet FinishGenius.Api.dll hash-password "<password>"   prints a bcrypt hash for Owner:PasswordHash
if (args is [var command, var password] && command.Equals("hash-password", StringComparison.OrdinalIgnoreCase))
{
    Console.WriteLine(BCrypt.Net.BCrypt.HashPassword(password, BCrypt.Net.BCrypt.GenerateSalt(12)));
    return;
}

var builder = WebApplication.CreateBuilder(args);

// Secrets (connection strings, JWT key) live in appsettings.Local.json, which is not committed to git.
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);
// File storage folders set by a System Administrator on the System Settings page (kept in App_Data across deployments).
builder.Configuration.AddJsonFile(FileStorage.SettingsFile, optional: true, reloadOnChange: true);

// The user picks a database (Dev / Prod) on the sign-in page; every request then uses its session's database.
var databases = new DatabaseCatalog(builder.Configuration);
builder.Services.AddSingleton(databases);
builder.Services.AddScoped<DatabaseSelector>();
builder.Services.AddDbContext<AppDbContext>((sp, o) => o.UseSqlServer(sp.GetRequiredService<DatabaseSelector>().Current.ConnectionString, sql =>
{
    sql.MigrationsHistoryTable("__EFMigrationsHistory", AppDbContext.Schema);
    sql.EnableRetryOnFailure(3);
    sql.CommandTimeout(60);
}));
// A legacy database (Prod) is used as-is: the same entities mapped onto the old site's dbo tables (Data/LegacyModel.cs).
builder.Services.AddScoped<AppDbContext>(sp =>
{
    var options = sp.GetRequiredService<DbContextOptions<AppDbContext>>();
    var target = sp.GetRequiredService<DatabaseSelector>().Current;
    if (!target.Legacy) return new AppDbContext(options);
    var http = sp.GetRequiredService<IHttpContextAccessor>();
    return new LegacyAppDbContext(options,
        () => int.TryParse(http.HttpContext?.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var id) ? id : 0,
        target.Label);
});

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
var jwt = builder.Configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
{
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidIssuer = jwt.Issuer,
        ValidAudience = jwt.Audience,
        IssuerSigningKey = jwt.SigningKey(),
        ValidateIssuerSigningKey = true,
        ClockSkew = TimeSpan.FromMinutes(2),
    };
    // <img>/<video>/download links cannot send headers, so file URLs carry ?access_token=.
    o.Events = new JwtBearerEvents
    {
        OnMessageReceived = ctx =>
        {
            var token = ctx.Request.Query["access_token"];
            if (!string.IsNullOrEmpty(token) && ctx.Request.Path.StartsWithSegments("/api/files"))
                ctx.Token = token;
            return Task.CompletedTask;
        },
    };
});
builder.Services.AddAuthorization();

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<CurrentUser>();
builder.Services.AddScoped<AuditService>();
builder.Services.AddScoped<TokenService>();
builder.Services.AddSingleton<FileStorage>();
builder.Services.AddSingleton<Thumbnails>();
builder.Services.AddSingleton<PageAccessStore>();
builder.Services.AddSingleton<PageAccessService>();
builder.Services.AddSingleton<OwnerAccount>();
// Sign-in log (dbo.FG_LoginAudit on the Production database by default; LoginAudit section in appsettings).
builder.Services.AddHttpClient(nameof(IpGeolocator), c => c.Timeout = TimeSpan.FromSeconds(3));
builder.Services.AddSingleton<IpGeolocator>();
builder.Services.AddSingleton<LoginAuditService>();
builder.Services.AddScoped<FinishGenius.Api.Services.ScheduleCalculator>();
builder.Services.AddScoped<FinishGenius.Api.Services.GroupCopyService>();
builder.Services.AddScoped<FinishGenius.Api.Services.DeviceCommandService>();
builder.Services.AddScoped<FinishGenius.Api.Services.ColorMatchService>();
builder.Services.AddSingleton<FinishGenius.Api.Services.PhotoColorService>();

const long maxUpload = 250L * 1024 * 1024; // videos for work instructions
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = maxUpload);
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = maxUpload);
builder.Services.Configure<IISServerOptions>(o => o.MaxRequestBodySize = maxUpload);

builder.Services.AddControllers().AddJsonOptions(o =>
{
    o.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
    o.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
});

// Pages send large JSON lists (a process step builder is over a megabyte) and the browser downloads the app's
// scripts on the first visit: compressing both is what keeps a page under two seconds away from the server.
builder.Services.AddResponseCompression(o =>
{
    o.EnableForHttps = true;
    o.Providers.Add<BrotliCompressionProvider>();
    o.Providers.Add<GzipCompressionProvider>();
    o.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(["application/json", "image/svg+xml", "application/manifest+json"]);
});
builder.Services.Configure<BrotliCompressionProviderOptions>(o => o.Level = CompressionLevel.Fastest);
builder.Services.Configure<GzipCompressionProviderOptions>(o => o.Level = CompressionLevel.Fastest);

var app = builder.Build();

// Command-line tools (add --db Prod to work on another configured database; default = Database:Default):
//   dotnet FinishGenius.Api.dll migrate --db Prod                    creates/updates the fg schema and base data
//   dotnet FinishGenius.Api.dll import-legacy --source FGAPP --yes   one-off data import from the legacy database
//   dotnet FinishGenius.Api.dll import-legacy-formulas --source FGAPP --yes   additive formula extras (never deletes)
if (args.Length > 0 && args[0].ToLowerInvariant() is "import-legacy" or "migrate" or "import-legacy-formulas")
{
    string? Arg(string name)
    {
        var i = Array.FindIndex(args, a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
    var dbKey = Arg("--db");
    var target = dbKey == null ? databases.Default : databases.Find(dbKey)
        ?? throw new InvalidOperationException($"Unknown database '{dbKey}'. Configured: {string.Join(", ", databases.All.Select(d => d.Key))}.");
    Console.WriteLine($"Database: {target.Key} ({target.Label})");
    if (target.Legacy)
        throw new InvalidOperationException($"'{target.Key}' is a legacy database shared with the old site; '{args[0]}' never runs against it.");
    using var cliScope = app.Services.CreateScope();
    cliScope.ServiceProvider.GetRequiredService<DatabaseSelector>().Use(target);
    var cliDb = cliScope.ServiceProvider.GetRequiredService<AppDbContext>();
    cliDb.Database.Migrate();
    if (args[0].Equals("migrate", StringComparison.OrdinalIgnoreCase))
        await DbSeeder.SeedAsync(cliDb, app.Configuration, app.Logger);
    else if (args[0].Equals("import-legacy-formulas", StringComparison.OrdinalIgnoreCase))
        await new LegacyImporter(cliDb, app.Configuration, app.Logger)
            .RunFormulaExtrasAsync(Arg("--source") ?? "FGAPP", args.Any(a => a.Equals("--yes", StringComparison.OrdinalIgnoreCase)));
    else
        await new LegacyImporter(cliDb, app.Configuration, app.Logger)
            .RunAsync(Arg("--source") ?? "FGAPP", args.Any(a => a.Equals("--yes", StringComparison.OrdinalIgnoreCase)));
    return;
}

app.UseMiddleware<ApiExceptionMiddleware>();

foreach (var target in databases.All.Where(d => d.AutoMigrate))
{
    try
    {
        using var scope = app.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<DatabaseSelector>().Use(target);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.Migrate();
        await DbSeeder.SeedAsync(db, app.Configuration, app.Logger);
    }
    catch (Exception ex) when (target != databases.Default)
    {
        // An unreachable secondary database must not take the whole site down; sign-ins to it report the problem.
        app.Logger.LogError(ex, "Could not migrate database {Database}", target.Key);
    }
}

// Sign-in answers carry the session token and are tiny, so they are sent as they are: compressing a response that
// mixes a secret with anything a caller supplied is the shape of attack (BREACH) that compression over TLS invites.
app.UseWhen(ctx => !ctx.Request.Path.StartsWithSegments("/api/auth"), b => b.UseResponseCompression());

app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        // Vite puts a content hash in every file name under /assets, so a browser never needs to ask about them again.
        // Everything else (index.html, the logo) is revalidated, so a new build shows up right away.
        var path = ctx.Context.Request.Path.Value ?? "";
        ctx.Context.Response.Headers.CacheControl = path.StartsWith("/assets/", StringComparison.OrdinalIgnoreCase)
            ? "public, max-age=31536000, immutable"
            : "no-cache";
    },
});
app.UseAuthentication();
app.UseMiddleware<PageAccessMiddleware>(); // pages the owner turned off; the owner account itself does not write data
app.UseAuthorization();
app.MapControllers();
app.MapGet("/api/health", () => Results.Ok(new { status = "ok", time = DateTime.UtcNow }));
// Client-side routes (React Router) fall back to the SPA shell; unknown /api routes stay 404.
app.MapFallback(async ctx =>
{
    if (ctx.Request.Path.StartsWithSegments("/api"))
    {
        ctx.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }
    var index = Path.Combine(app.Environment.WebRootPath ?? "wwwroot", "index.html");
    if (File.Exists(index))
    {
        ctx.Response.ContentType = "text/html";
        ctx.Response.Headers.CacheControl = "no-cache"; // the shell names the current build's scripts
        await ctx.Response.SendFileAsync(index);
    }
    else
    {
        await ctx.Response.WriteAsync("Finish Genius API is running. Frontend not built yet (run build.ps1).");
    }
});

app.Run();
