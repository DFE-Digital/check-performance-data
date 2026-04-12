using DfE.CheckPerformanceData.Application;
using DfE.CheckPerformanceData.Domain.Entities;
using DfE.CheckPerformanceData.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.Infrastructure.Persistence;

public sealed class PortalDbContext(DbContextOptions<PortalDbContext> options) : DbContext(options), IPortalDbContext
{
    public DbSet<ContentBlock> ContentBlocks => Set<ContentBlock>();
    public DbSet<ContentBlockVersion> ContentBlockVersions => Set<ContentBlockVersion>();
    public DbSet<WikiPage> WikiPages => Set<WikiPage>();
    public DbSet<WikiPageVersion> WikiPageVersions => Set<WikiPageVersion>();
    public DbSet<Workflow> Workflows => Set<Workflow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new ContentBlockConfiguration());
        modelBuilder.ApplyConfiguration(new ContentBlockVersionConfiguration());
        modelBuilder.ApplyConfiguration(new WikiPageConfiguration());
        modelBuilder.ApplyConfiguration(new WikiPageVersionConfiguration());
    }
}