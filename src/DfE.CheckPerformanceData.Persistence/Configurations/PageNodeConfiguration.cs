using DfE.CheckPerformanceData.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DfE.CheckPerformanceData.Persistence.Configurations;

internal sealed class PageNodeConfiguration : IEntityTypeConfiguration<PageNode>
{
    public void Configure(EntityTypeBuilder<PageNode> builder)
    {
        builder.HasKey(n => n.Id);
        builder.Property(n => n.Id).ValueGeneratedNever(); // app supplies GUIDs
        builder.Property(n => n.Segment).IsRequired().HasMaxLength(256);
        builder.Property(n => n.Path).IsRequired().HasMaxLength(2048);
        builder.Property(n => n.Title).IsRequired();
        builder.Property(n => n.PageType).IsRequired().HasMaxLength(32);

        builder.HasIndex(n => n.Path).IsUnique().HasFilter("\"DeletedDate\" IS NULL");
        builder.HasIndex(n => n.ParentId);
        builder.HasQueryFilter(n => n.DeletedDate == null);

        builder.HasOne<PageNode>().WithMany().HasForeignKey(n => n.ParentId)
            .HasConstraintName("FK_PageNode_PageNode_ParentId")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(n => n.Versions).WithOne(v => v.PageNode)
            .HasForeignKey(v => v.PageNodeId).OnDelete(DeleteBehavior.Cascade);
    }
}
