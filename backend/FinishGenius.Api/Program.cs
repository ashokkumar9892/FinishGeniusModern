using System.Text.Json.Serialization;
using FinishGenius.Api.Data;
using FinishGenius.Api.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// Secrets (connection string, JWT key) live in appsettings.Local.json, which is not committed to git.
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

var connectionString = builder.Configuration.GetConnectionString("Default");
if (string.IsNullOrWhiteSpace(connectionString) || connectionString.Contains("__SET_ME__"))
    throw new InvalidOperationException("ConnectionStrings:Default is not configured. Copy appsettings.Local.example.json to appsettings.Local.json and fill it in.");

builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlServer(connectionString, sql =>
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

// One-off data import from the legacy Finish Genius database:
//   dotnet FinishGenius.Api.dll import-legacy --source FGAPP --yes
if (args.Length > 0 && args[0].Equals("import-legacy", StringComparison.OrdinalIgnoreCase))
{
    var sourceIndex = Array.FindIndex(args, a => a.Equals("--source", StringComparison.OrdinalIgnoreCase));
    var source = sourceIndex >= 0 && sourceIndex + 1 < args.Length ? args[sourceIndex + 1] : "FGAPP";
    var confirmed = args.Any(a => a.Equals("--yes", StringComparison.OrdinalIgnoreCase));
    using var importScope = app.Services.CreateScope();
    var importDb = importScope.ServiceProvider.GetRequiredService<AppDbContext>();
    importDb.Database.Migrate();
    await new LegacyImporter(importDb, app.Configuration, app.Logger).RunAsync(source, confirmed);
    return;
}

app.UseMiddleware<ApiExceptionMiddleware>();

if (app.Configuration.GetValue("Database:AutoMigrate", true))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
    await DbSeeder.SeedAsync(db, app.Configuration, app.Logger);
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
