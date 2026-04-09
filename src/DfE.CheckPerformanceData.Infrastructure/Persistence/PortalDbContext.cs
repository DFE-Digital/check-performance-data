using DfE.CheckPerformanceData.Application;
using DfE.CheckPerformanceData.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.Infrastructure.Persistence;

public sealed class PortalDbContext(DbContextOptions<PortalDbContext> options) : DbContext(options), IPortalDbContext
{
    public DbSet<Workflow> Workflows => Set<Workflow>();
    public DbSet<WikiPage> WikiPages => Set<WikiPage>();
    public DbSet<WikiPageVersion> WikiPageVersions => Set<WikiPageVersion>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WikiPage>(entity =>
        {
            entity.HasOne(w => w.Parent)
                .WithMany(w => w.Children)
                .HasForeignKey(w => w.ParentId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(w => new { w.ParentId, w.Slug })
                .IsUnique()
                .HasFilter("\"IsDeleted\" = false");

            entity.HasIndex(w => w.IsDeleted);

            entity.HasQueryFilter(w => !w.IsDeleted);
        });

        modelBuilder.Entity<WikiPageVersion>(entity =>
        {
            entity.HasOne(v => v.WikiPage)
                .WithMany(w => w.Versions)
                .HasForeignKey(v => v.WikiPageId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(v => new { v.WikiPageId, v.VersionNumber })
                .IsUnique();
        });
    }
}