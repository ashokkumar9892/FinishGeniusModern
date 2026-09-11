using Microsoft.EntityFrameworkCore;

namespace FinishGenius.Api.Data;

/// <summary>
/// The app's context for databases marked <c>"Legacy": true</c>: same entities, mapped onto the old dbo tables by
/// <see cref="LegacyModel"/>. EF caches one model per context type, so the fg model (and its migrations) is unaffected.
/// Never migrate this context. <c>dotnet ef</c> commands must pass <c>--context AppDbContext</c>.
/// </summary>
public class LegacyAppDbContext(DbContextOptions<AppDbContext> options, Func<int> currentUserId, string label) : AppDbContext(options)
{
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        // The old tables are mostly unindexed. Loading collections in separate queries lets SQL Server filter each one by
        // its parents' keys instead of joining whole tables (a step's values: ~9 s as one query on Prod).
        new Microsoft.EntityFrameworkCore.Infrastructure.SqlServerDbContextOptionsBuilder(optionsBuilder)
            .UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        LegacyModel.Apply(b);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        var side = LegacyModel.BeforeSave(this, currentUserId(), label);
        foreach (var s in side.Where(s => !s.AfterSave)) Run(s);
        var saved = base.SaveChanges(acceptAllChangesOnSuccess);
        foreach (var s in side.Where(s => s.AfterSave)) Run(s);
        return saved + side.Count;
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        var side = LegacyModel.BeforeSave(this, currentUserId(), label);
        foreach (var s in side.Where(s => !s.AfterSave)) await RunAsync(s, cancellationToken);
        var saved = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        foreach (var s in side.Where(s => s.AfterSave)) await RunAsync(s, cancellationToken);
        return saved + side.Count;
    }

    private void Run(SideWrite s)
    {
        if (s.Build() is { } cmd) Database.ExecuteSqlRaw(cmd.Sql, cmd.Args);
        s.Then?.Invoke();
    }

    private async Task RunAsync(SideWrite s, CancellationToken ct)
    {
        if (s.Build() is { } cmd) await Database.ExecuteSqlRawAsync(cmd.Sql, cmd.Args, ct);
        s.Then?.Invoke();
    }
}
