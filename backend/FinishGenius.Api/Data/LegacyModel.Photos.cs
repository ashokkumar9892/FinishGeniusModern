using FinishGenius.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace FinishGenius.Api.Data;

/// <summary>Photo Gallery on the old tables: dbo.PhotoGallery + dbo.PhotoTags.</summary>
public static partial class LegacyModel
{
    private static void PhotoGallery(ModelBuilder b)
    {
        b.Entity<Photo>(e =>
        {
            // Old files live on the old server (AppData/PhotoGallery/...); like the legacy import they are expected under
            // uploads/photos/{group}/legacy/. Photos uploaded here keep their own relative path in PhotoFile.
            e.ToTable("PhotoGallery", "dbo");
            e.ToSqlQuery("""
                SELECT p.PhotoID, p.GroupID, ISNULL(NULLIF(LTRIM(RTRIM(p.PhotoName)), ''), p.PhotoFile) AS PhotoName,
                       CASE WHEN p.PhotoFile LIKE 'photos/%' THEN p.PhotoFile ELSE CONCAT('photos/', p.GroupID, '/legacy/', p.PhotoFile) END AS PhotoFile,
                       CAST(NULL AS nvarchar(200)) AS ContentType, p.CreatedBy, ISNULL(p.CreatedDate, CAST('2000-01-01' AS datetime)) AS CreatedDate
                FROM dbo.PhotoGallery p
                """);
            e.Property(x => x.Id).HasColumnName("PhotoID");
            e.Property(x => x.GroupId).HasColumnName("GroupID");
            e.Property(x => x.Name).HasColumnName("PhotoName");
            e.Property(x => x.StoredFile).HasColumnName("PhotoFile");
            e.Property(x => x.CreatedAt).HasColumnName("CreatedDate");
            e.Property(x => x.CreatedBy).HasAnnotation(NullDefault, "@user"); // NOT NULL in the old table
            ReadOnly(e.Property(x => x.ContentType));                        // served by file extension
            Max(e.Property(x => x.Name), 255);
            Max(e.Property(x => x.StoredFile), 255);
        });

        b.Entity<PhotoTag>(e =>
        {
            e.ToTable("PhotoTags", "dbo");
            // Blank legacy tags are skipped (like the legacy import).
            e.ToSqlQuery("""
                SELECT t.TagID, t.PhotoID, LTRIM(RTRIM(t.TagName)) AS TagName, t.GroupID, t.CreatedBy, t.CreatedDate
                FROM dbo.PhotoTags t WHERE LTRIM(RTRIM(ISNULL(t.TagName, ''))) <> ''
                """);
            e.Property(x => x.Id).HasColumnName("TagID");
            e.Property(x => x.PhotoId).HasColumnName("PhotoID");
            e.Property(x => x.Tag).HasColumnName("TagName");
            Max(e.Property(x => x.Tag), 50);
            e.Property<int>("GroupID");                                   // the photo's group, filled on insert
            e.Property<int>("CreatedBy").HasAnnotation(InsertValue, "@user");
            e.Property<DateTime?>("CreatedDate").HasAnnotation(InsertValue, "@now");
        });
    }

    private static int PhotoGroupId(DbContext db, PhotoTag tag) =>
        db.ChangeTracker.Entries<Photo>().Select(e => e.Entity).FirstOrDefault(p => p.Tags.Contains(tag) || p.Id == tag.PhotoId)?.GroupId
        ?? db.Set<Photo>().Where(p => p.Id == tag.PhotoId).Select(p => p.GroupId).First();

    /// <summary>Rows of the old tables that must go before a delete (NO ACTION foreign keys).</summary>
    private static void Deleting(EntityEntry entry, List<SideWrite> side)
    {
        switch (entry.Entity)
        {
            case Photo p:
                var id = p.Id;
                side.Add(new SideWrite(false, () => ("DELETE FROM dbo.ProcessSchedulePhotoGallery WHERE PhotoID = @id", [P("@id", id)])));
                break;
            default:
                DeletingProcess(entry, side);
                break;
        }
    }
}
