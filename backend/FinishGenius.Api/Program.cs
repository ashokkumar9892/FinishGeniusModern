using System.Text.Json.Serialization;
using FinishGenius.Api.Data;
using FinishGenius.Api.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// Secrets (connection strings, JWT key) live in appsettings.Local.json, which is not committed to git.
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

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
builder.Services.AddScoped<FinishGenius.Api.Services.ScheduleCalculator>();
builder.Services.AddScoped<FinishGenius.Api.Services.GroupCopyService>();
builder.Services.AddScoped<FinishGenius.Api.Services.DeviceCommandService>();

const long maxUpload = 250L * 1024 * 1024; // videos for work instructions
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = maxUpload);
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = maxUpload);
builder.Services.Configure<IISServerOptions>(o => o.MaxRequestBodySize = maxUpload);

builder.Services.AddControllers().AddJsonOptions(o =>
{
    o.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
    o.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
});

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

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
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
        await ctx.Response.SendFileAsync(index);
    }
    else
    {
        await ctx.Response.WriteAsync("Finish Genius API is running. Frontend not built yet (run build.ps1).");
    }
});

app.Run();
