using FinishGenius.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace FinishGenius.Api.Data;

public static partial class LegacyModel
{
    /// <summary>
    /// Colour samples on the old site's database. Unlike every other mapping here, this is not an old table dressed
    /// up as a new one: the old site has nothing of the kind, so Finish Genius keeps its own table next to the
    /// legacy ones, named like the others it adds (dbo.FG_LoginAudit, dbo.FG_PageAccess). Nothing existing is
    /// touched, and the table is created on first use — see <see cref="LegacyTables"/>.
    /// </summary>
    private static void ColorSamples(ModelBuilder b) => b.Entity<ColorSample>(e =>
    {
        e.ToTable(LegacyTables.ColorSamples, "dbo");
        e.Property(x => x.Notes).HasMaxLength(4000);
        e.Ignore(x => x.Group); // the legacy Groups mapping is a query, and this table needs no join to it
    });
}
