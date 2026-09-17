using System.Collections.Concurrent;
using System.Data;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace FinishGenius.Api.Infrastructure;

/// <summary>One sign-in attempt, as it is stored and shown on the Login Activity page.</summary>
public sealed class LoginAuditEntry
{
    public long Id { get; set; }
    public DateTime SignedInAtUtc { get; set; }
    public string UserName { get; set; } = "";
    public int? UserId { get; set; }
    /// <summary>Which configured database the sign-in opened ("Dev" / "Prod").</summary>
    public string DatabaseKey { get; set; } = "";
    public string? DatabaseLabel { get; set; }
    public bool Succeeded { get; set; }
    public string? FailureReason { get; set; }
    /// <summary>The owner account (anjiansh), which has no user record.</summary>
    public bool IsOwner { get; set; }
    public string? IpAddress { get; set; }
    /// <summary>"City, Region, Country" as resolved from the IP address, or "Local network".</summary>
    public string? Location { get; set; }
    public string? City { get; set; }
    public string? Region { get; set; }
    public string? Country { get; set; }
    public string? TimeZone { get; set; }
    public string? Isp { get; set; }
    public string? UserAgent { get; set; }
    /// <summary>The site address the browser used (host:port).</summary>
    public string? Host { get; set; }
}

/// <summary>
/// City / country for an IP address, from the free ip-api.com service. Private and loopback addresses are labelled
/// without any outside call, results are cached, and a failure just leaves the location empty — a sign-in is never
/// held up or refused because the lookup did not work. Turn the outside call off with <c>LoginAudit:GeoLookup: false</c>.
/// </summary>
public class IpGeolocator(IHttpClientFactory clients, IConfiguration config, ILogger<IpGeolocator> log)
{
    public sealed record Place(string? Text, string? City, string? Region, string? Country, string? TimeZone, string? Isp);

    private static readonly Place Unknown = new(null, null, null, null, null, null);
    private static readonly Place Local = new("Local network", null, null, null, null, null);
    private readonly ConcurrentDictionary<string, (Place Place, DateTime Until)> _cache = new();

    private bool Enabled => config.GetValue("LoginAudit:GeoLookup", true);

    public async Task<Place> LocateAsync(string? ip, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(ip)) return Unknown;
        if (IsPrivate(ip)) return Local;
        if (_cache.TryGetValue(ip, out var hit) && hit.Until > DateTime.UtcNow) return hit.Place;
        if (!Enabled) return Unknown;

        var place = Unknown;
        try
        {
            var client = clients.CreateClient(nameof(IpGeolocator));
            // Free endpoint: HTTP only, 45 lookups a minute — plenty for sign-ins, and each address is cached.
            var url = $"http://ip-api.com/json/{Uri.EscapeDataString(ip)}?fields=status,message,country,regionName,city,timezone,isp";
            using var response = await client.GetAsync(url, token);
            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
            var root = json.RootElement;
            if (root.TryGetProperty("status", out var status) && status.GetString() == "success")
            {
                string? Get(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
                    && v.GetString() is { Length: > 0 } s ? s : null;
                var city = Get("city");
                var region = Get("regionName");
                var country = Get("country");
                var text = string.Join(", ", new[] { city, region, country }.Where(p => p != null));
                place = new Place(text.Length > 0 ? text : null, city, region, country, Get("timezone"), Get("isp"));
            }
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
        {
            log.LogDebug(e, "IP location lookup failed for {Ip}; the sign-in is recorded without a location", ip);
        }
        // Cache failures too (briefly), so an unreachable lookup service does not slow down every sign-in.
        _cache[ip] = (place, DateTime.UtcNow.Add(place.Text == null ? TimeSpan.FromMinutes(10) : TimeSpan.FromHours(12)));
        return place;
    }

    /// <summary>Loopback, link-local and RFC1918 / unique-local addresses: nothing to look up.</summary>
    public static bool IsPrivate(string ip)
    {
        if (!IPAddress.TryParse(ip, out var address)) return false;
        if (IPAddress.IsLoopback(address)) return true;
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
            return address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || (address.GetAddressBytes()[0] & 0xFE) == 0xFC;
        var b = address.GetAddressBytes();
        return b[0] == 10
               || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
               || (b[0] == 192 && b[1] == 168)
               || (b[0] == 169 && b[1] == 254);
    }
}

/// <summary>
/// The sign-in log: every attempt (successful or not) is written to one table, <c>dbo.FG_LoginAudit</c>, on the
/// database named by <c>LoginAudit:Database</c> — the Production server by default — whichever database the user
/// actually signed in to, so the owner account sees one complete list. The table is created on first use (the
/// Production database is never migrated by EF; see Program.cs), and nothing here can fail a sign-in.
/// </summary>
public class LoginAuditService(DatabaseCatalog catalog, IConfiguration config, IpGeolocator geo, ILogger<LoginAuditService> log)
{
    public const string TableName = "dbo.FG_LoginAudit";

    private readonly SemaphoreSlim _tableLock = new(1, 1);
    private string? _readyFor;

    /// <summary>Recording can be turned off with <c>LoginAudit:Enabled: false</c>.</summary>
    public bool Enabled => config.GetValue("LoginAudit:Enabled", true);

    /// <summary>
    /// The database holding the log: <c>LoginAudit:Database</c> (a key from <c>Databases</c>), otherwise the
    /// Production one, otherwise the default. Null when that database is not configured on this server.
    /// </summary>
    public DatabaseTarget? Target
    {
        get
        {
            var key = config["LoginAudit:Database"]?.Trim();
            if (!string.IsNullOrEmpty(key)) return catalog.Find(key);
            return catalog.All.FirstOrDefault(d => d.Production) ?? catalog.Default;
        }
    }

    private const string CreateTableSql = $"""
        IF OBJECT_ID(N'{TableName}', N'U') IS NULL
        BEGIN
            CREATE TABLE {TableName}
            (
                Id              BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_FG_LoginAudit PRIMARY KEY,
                SignedInAtUtc   DATETIME2(0)  NOT NULL CONSTRAINT DF_FG_LoginAudit_At DEFAULT (SYSUTCDATETIME()),
                UserName        NVARCHAR(256) NOT NULL,
                UserId          INT           NULL,
                DatabaseKey     NVARCHAR(50)  NOT NULL,
                DatabaseLabel   NVARCHAR(100) NULL,
                Succeeded       BIT           NOT NULL,
                FailureReason   NVARCHAR(200) NULL,
                IsOwner         BIT           NOT NULL CONSTRAINT DF_FG_LoginAudit_Owner DEFAULT (0),
                IpAddress       NVARCHAR(64)  NULL,
                Location        NVARCHAR(200) NULL,
                City            NVARCHAR(100) NULL,
                Region          NVARCHAR(100) NULL,
                Country         NVARCHAR(100) NULL,
                TimeZone        NVARCHAR(64)  NULL,
                Isp             NVARCHAR(150) NULL,
                UserAgent       NVARCHAR(400) NULL,
                Host            NVARCHAR(200) NULL
            );
            CREATE INDEX IX_FG_LoginAudit_SignedInAtUtc ON {TableName} (SignedInAtUtc DESC);
            CREATE INDEX IX_FG_LoginAudit_UserName ON {TableName} (UserName, SignedInAtUtc DESC);
        END
        """;

    private async Task<SqlConnection> OpenAsync(DatabaseTarget target, CancellationToken token)
    {
        var connection = new SqlConnection(target.ConnectionString);
        await connection.OpenAsync(token);
        return connection;
    }

    /// <summary>Creates the table the first time it is needed (once per process, per database).</summary>
    private async Task EnsureTableAsync(DatabaseTarget target, CancellationToken token)
    {
        if (_readyFor == target.Key) return;
        await _tableLock.WaitAsync(token);
        try
        {
            if (_readyFor == target.Key) return;
            await using var connection = await OpenAsync(target, token);
            await using var command = new SqlCommand(CreateTableSql, connection) { CommandTimeout = 60 };
            await command.ExecuteNonQueryAsync(token);
            _readyFor = target.Key;
        }
        finally
        {
            _tableLock.Release();
        }
    }

    /// <summary>
    /// Records a sign-in attempt. Never throws: a problem with the log must not stop anyone signing in, so failures
    /// are only written to the application log.
    /// </summary>
    public async Task RecordAsync(HttpContext? ctx, string userName, int? userId, bool isOwner, DatabaseTarget database,
        bool succeeded, string? failureReason)
    {
        if (!Enabled) return;
        var target = Target;
        if (target == null)
        {
            log.LogWarning("Sign-in by {User} not recorded: LoginAudit:Database is \"{Key}\", which is not configured",
                userName, config["LoginAudit:Database"]);
            return;
        }
        var entry = new LoginAuditEntry
        {
            SignedInAtUtc = DateTime.UtcNow,
            UserName = Cut(userName, 256) ?? "(blank)",
            UserId = userId is > 0 ? userId : null,
            DatabaseKey = Cut(database.Key, 50) ?? "",
            DatabaseLabel = Cut(database.Label, 100),
            Succeeded = succeeded,
            FailureReason = Cut(failureReason, 200),
            IsOwner = isOwner,
            IpAddress = Cut(ClientIp(ctx), 64),
            UserAgent = Cut(ctx?.Request.Headers.UserAgent.ToString(), 400),
            Host = Cut(ctx?.Request.Host.Value, 200),
        };
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var place = await geo.LocateAsync(entry.IpAddress, timeout.Token);
            entry.Location = Cut(place.Text, 200);
            entry.City = Cut(place.City, 100);
            entry.Region = Cut(place.Region, 100);
            entry.Country = Cut(place.Country, 100);
            entry.TimeZone = Cut(place.TimeZone, 64);
            entry.Isp = Cut(place.Isp, 150);
        }
        catch (Exception e)
        {
            log.LogDebug(e, "IP location lookup failed; recording the sign-in without a location");
        }
        try
        {
            await EnsureTableAsync(target, CancellationToken.None);
            await using var connection = await OpenAsync(target, CancellationToken.None);
            await using var command = new SqlCommand($"""
                INSERT INTO {TableName}
                    (SignedInAtUtc, UserName, UserId, DatabaseKey, DatabaseLabel, Succeeded, FailureReason, IsOwner,
                     IpAddress, Location, City, Region, Country, TimeZone, Isp, UserAgent, Host)
                VALUES
                    (@at, @user, @userId, @dbKey, @dbLabel, @ok, @reason, @owner,
                     @ip, @location, @city, @region, @country, @tz, @isp, @agent, @host);
                """, connection) { CommandTimeout = 30 };
            command.Parameters.Add("@at", SqlDbType.DateTime2).Value = entry.SignedInAtUtc;
            Add(command, "@user", entry.UserName, 256);
            command.Parameters.Add("@userId", SqlDbType.Int).Value = (object?)entry.UserId ?? DBNull.Value;
            Add(command, "@dbKey", entry.DatabaseKey, 50);
            Add(command, "@dbLabel", entry.DatabaseLabel, 100);
            command.Parameters.Add("@ok", SqlDbType.Bit).Value = entry.Succeeded;
            Add(command, "@reason", entry.FailureReason, 200);
            command.Parameters.Add("@owner", SqlDbType.Bit).Value = entry.IsOwner;
            Add(command, "@ip", entry.IpAddress, 64);
            Add(command, "@location", entry.Location, 200);
            Add(command, "@city", entry.City, 100);
            Add(command, "@region", entry.Region, 100);
            Add(command, "@country", entry.Country, 100);
            Add(command, "@tz", entry.TimeZone, 64);
            Add(command, "@isp", entry.Isp, 150);
            Add(command, "@agent", entry.UserAgent, 400);
            Add(command, "@host", entry.Host, 200);
            await command.ExecuteNonQueryAsync();
        }
        catch (Exception e)
        {
            _readyFor = null; // the next sign-in checks the table again
            log.LogError(e, "Sign-in by {User} could not be recorded in {Table} on {Database}", userName, TableName, target.Key);
        }
    }

    public sealed record Query(DateTime? From, DateTime? To, string? Search, string? Result, string? DatabaseKey, int Take);

    /// <summary>The log, newest first, for the Login Activity page.</summary>
    public async Task<(List<LoginAuditEntry> Rows, int Total)> ReadAsync(Query query, CancellationToken token = default)
    {
        var target = Target ?? throw new ApiException(StatusCodes.Status503ServiceUnavailable,
            $"The sign-in log database (LoginAudit:Database = \"{config["LoginAudit:Database"]}\") is not configured on this server.");
        await EnsureTableAsync(target, token);

        var where = new List<string>();
        await using var connection = await OpenAsync(target, token);
        await using var command = new SqlCommand { Connection = connection, CommandTimeout = 60 };
        if (query.From is { } from)
        {
            where.Add("SignedInAtUtc >= @from");
            command.Parameters.Add("@from", SqlDbType.DateTime2).Value = from;
        }
        if (query.To is { } to)
        {
            where.Add("SignedInAtUtc < @to");
            command.Parameters.Add("@to", SqlDbType.DateTime2).Value = to;
        }
        if (Text.Clean(query.Search) is { } search)
        {
            where.Add("(UserName LIKE @q OR IpAddress LIKE @q OR Location LIKE @q OR Isp LIKE @q)");
            Add(command, "@q", $"%{search.Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]")}%", 300);
        }
        if (query.Result?.Equals("success", StringComparison.OrdinalIgnoreCase) == true) where.Add("Succeeded = 1");
        else if (query.Result?.Equals("failed", StringComparison.OrdinalIgnoreCase) == true) where.Add("Succeeded = 0");
        if (Text.Clean(query.DatabaseKey) is { } dbKey)
        {
            where.Add("DatabaseKey = @db");
            Add(command, "@db", dbKey, 50);
        }
        var filter = where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "";
        var take = Math.Clamp(query.Take, 1, 5000);
        command.CommandText = $"""
            SELECT COUNT(*) FROM {TableName} {filter};
            SELECT TOP ({take}) Id, SignedInAtUtc, UserName, UserId, DatabaseKey, DatabaseLabel, Succeeded, FailureReason,
                   IsOwner, IpAddress, Location, City, Region, Country, TimeZone, Isp, UserAgent, Host
            FROM {TableName} {filter}
            ORDER BY SignedInAtUtc DESC, Id DESC;
            """;

        await using var reader = await command.ExecuteReaderAsync(token);
        var total = await reader.ReadAsync(token) ? reader.GetInt32(0) : 0;
        await reader.NextResultAsync(token);
        var rows = new List<LoginAuditEntry>();
        while (await reader.ReadAsync(token))
        {
            string? Str(int i) => reader.IsDBNull(i) ? null : reader.GetString(i);
            rows.Add(new LoginAuditEntry
            {
                Id = reader.GetInt64(0),
                SignedInAtUtc = DateTime.SpecifyKind(reader.GetDateTime(1), DateTimeKind.Utc),
                UserName = reader.GetString(2),
                UserId = reader.IsDBNull(3) ? null : reader.GetInt32(3),
                DatabaseKey = reader.GetString(4),
                DatabaseLabel = Str(5),
                Succeeded = reader.GetBoolean(6),
                FailureReason = Str(7),
                IsOwner = reader.GetBoolean(8),
                IpAddress = Str(9),
                Location = Str(10),
                City = Str(11),
                Region = Str(12),
                Country = Str(13),
                TimeZone = Str(14),
                Isp = Str(15),
                UserAgent = Str(16),
                Host = Str(17),
            });
        }
        return (rows, total);
    }

    private static void Add(SqlCommand command, string name, string? value, int size) =>
        command.Parameters.Add(name, SqlDbType.NVarChar, size).Value = (object?)value ?? DBNull.Value;

    private static string? Cut(string? value, int max) =>
        Text.Clean(value) is { } s ? (s.Length > max ? s[..max] : s) : null;

    /// <summary>
    /// The caller's address. Behind IIS / a load balancer the real address arrives in X-Forwarded-For (first entry)
    /// or X-Real-IP; otherwise it is the socket address.
    /// </summary>
    public static string? ClientIp(HttpContext? ctx)
    {
        if (ctx == null) return null;
        var forwarded = ctx.Request.Headers["X-Forwarded-For"].ToString();
        if (!string.IsNullOrWhiteSpace(forwarded))
        {
            var first = forwarded.Split(',')[0].Trim();
            if (Normalise(first) is { } ip) return ip;
        }
        var real = ctx.Request.Headers["X-Real-IP"].ToString().Trim();
        if (Normalise(real) is { } realIp) return realIp;
        return ctx.Connection.RemoteIpAddress is { } address ? Normalise(address.ToString()) : null;
    }

    /// <summary>Strips a port and unwraps IPv4-mapped IPv6 ("::ffff:10.0.0.4" → "10.0.0.4").</summary>
    private static string? Normalise(string? value)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text)) return null;
        if (text.StartsWith('[') && text.IndexOf(']') > 0) text = text[1..text.IndexOf(']')];
        else if (text.Count(c => c == ':') == 1 && text.IndexOf(':') > 0) text = text[..text.IndexOf(':')];
        if (!IPAddress.TryParse(text, out var address)) return null;
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        return address.ToString();
    }
}
