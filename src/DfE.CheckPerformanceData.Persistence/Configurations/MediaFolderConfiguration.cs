using DfE.CheckPerformanceData.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DfE.CheckPerformanceData.Persistence.Configurations;

internal sealed class MediaFolderConfiguration : IEntityTypeConfiguration<MediaFolder>
{
    /// <summary>The trigger check that refuses a move that puts a folder inside itself.</summary>
    public const string NoCycleConstraint = "ck_media_folders_no_cycle";

    public const string UniqueNameIndex = "ux_media_folders_parent_id_name_key";

    public void Configure(EntityTypeBuilder<MediaFolder> builder)
    {
        builder.ToTable("media_folders", t =>
        {
            t.HasCheckConstraint("ck_media_folders_name_not_blank", "btrim(name) <> ''");
            t.HasCheckConstraint("ck_media_folders_not_own_parent", "parent_id IS NULL OR parent_id <> id");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.Name).HasColumnName("name").IsRequired().HasMaxLength(100);
        builder.Property(x => x.NameKey)
            .HasColumnName("name_key")
            .HasMaxLength(100)
            .HasComputedColumnSql("lower(name)", stored: true);
        builder.Property(x => x.ParentId).HasColumnName("parent_id");

        // Restrict: a folder that holds a folder cannot be deleted, whatever the caller checked.
        builder.HasOne<MediaFolder>()
            .WithMany()
            .HasForeignKey(x => x.ParentId)
            .HasConstraintName("fk_media_folders_media_folders_parent_id")
            .OnDelete(DeleteBehavior.Restrict);

        // Nulls not distinct, so the rule also holds for folders at the top of the library.
        builder.HasIndex(x => new { x.ParentId, x.NameKey })
            .IsUnique()
            .AreNullsDistinct(false)
            .HasDatabaseName(UniqueNameIndex);

        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").HasColumnType("timestamp with time zone");
        builder.Property(x => x.CreatedBy).HasColumnName("created_by").HasMaxLength(200);
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc").HasColumnType("timestamp with time zone");
        builder.Property(x => x.UpdatedBy).HasColumnName("updated_by").HasMaxLength(200);
    }
}
