using Microsoft.Data.SqlClient;

namespace FinishGenius.Api.Infrastructure;

/// <summary>
/// Where the Page Access switches are kept: one row in <c>dbo.FG_PageAccess</c>, so that installing a new build
/// cannot lose them — a deployment replaces the site's files, and the database is not part of the package.
/// The table is created on first use (the Production database is never migrated by EF; see Program.cs), and
/// <c>App_Data/page-access.json</c> is still written next to it as a local copy, used only if the database cannot be
/// reached while the site starts.
/// </summary>
public class PageAccessStore(DatabaseCatalog catalog, IConfiguration config, ILogger<PageAccessStore> log)
{
    public const string TableName = "dbo.FG_PageAccess";

    private readonly object _tableLock = new();
    private string? _readyFor;

    /// <summary>
    /// The database holding the switches: <c>PageAccess:Database</c> (a key from <c>Databases</c>), otherwise the
    /// Production one, otherwise the default — the same rule as the sign-in log, so both live together.
    /// </summary>
    public DatabaseTarget? Target
    {
        get
        {
            var key = config["PageAccess:Database"]?.Trim();
            if (!string.IsNullOrEmpty(key)) return catalog.Find(key);
            return catalog.All.FirstOrDefault(d => d.Production) ?? catalog.Default;
        }
    }

    private const string CreateTableSql = $"""
        IF OBJECT_ID(N'{TableName}', N'U') IS NULL
        BEGIN
            CREATE TABLE {TableName}
            (
                Id            INT            NOT NULL CONSTRAINT PK_FG_PageAccess PRIMARY KEY,
                SettingsJson  NVARCHAR(MAX)  NOT NULL,
                UpdatedAtUtc  DATETIME2(0)   NOT NULL CONSTRAINT DF_FG_PageAccess_At DEFAULT (SYSUTCDATETIME()),
                UpdatedBy     NVARCHAR(256)  NULL
            );
        END
        """;

    private SqlConnection Open(DatabaseTarget target)
    {
        var connection = new SqlConnection(target.ConnectionString);
        connection.Open();
        return connection;
    }

    /// <summary>Creates the table the first time it is needed (once per process, per database).</summary>
    private void EnsureTable(SqlConnection connection, DatabaseTarget target)
    {
        if (_readyFor == target.Key) return;
        lock (_tableLock)
        {
            if (_readyFor == target.Key) return;
            using var command = new SqlCommand(CreateTableSql, connection) { CommandTimeout = 60 };
            command.ExecuteNonQuery();
            _readyFor = target.Key;
        }
    }

    /// <summary>The stored switches as JSON, "" when nothing is stored yet, null when the database cannot be read.</summary>
    public string? Read()
    {
        var target = Target;
        if (target == null)
        {
            log.LogWarning("Page access: PageAccess:Database is \"{Key}\", which is not configured", config["PageAccess:Database"]);
            return null;
        }
        try
        {
            using var connection = Open(target);
            EnsureTable(connection, target);
            using var command = new SqlCommand($"SELECT SettingsJson FROM {TableName} WHERE Id = 1", connection) { CommandTimeout = 30 };
            return command.ExecuteScalar() as string ?? "";
        }
        catch (Exception e) when (e is SqlException or InvalidOperationException)
        {
            log.LogError(e, "Page access could not be read from {Table} on {Database}", TableName, target.Key);
            return null;
        }
    }

    /// <summary>Stores the switches (one row, replaced each time). Throws when the database refuses the write.</summary>
    public void Write(string settingsJson, string user)
    {
        var target = Target ?? throw ApiException.Bad(
            $"Page access cannot be saved: PageAccess:Database is \"{config["PageAccess:Database"]}\", which is not configured on this server.");
        try
        {
            using var connection = Open(target);
            EnsureTable(connection, target);
            using var command = new SqlCommand($"""
                UPDATE {TableName} SET SettingsJson = @json, UpdatedAtUtc = SYSUTCDATETIME(), UpdatedBy = @user WHERE Id = 1;
                IF @@ROWCOUNT = 0 INSERT INTO {TableName} (Id, SettingsJson, UpdatedBy) VALUES (1, @json, @user);
                """, connection) { CommandTimeout = 30 };
            command.Parameters.AddWithValue("@json", settingsJson);
            command.Parameters.AddWithValue("@user", (object?)user ?? DBNull.Value);
            command.ExecuteNonQuery();
        }
        catch (Exception e) when (e is SqlException or InvalidOperationException)
        {
            log.LogError(e, "Page access could not be saved to {Table} on {Database}", TableName, target.Key);
            throw ApiException.Bad($"Page access could not be saved to {TableName} on the {target.Label} database: {e.Message}");
        }
    }
}
