using DfE.CheckPerformanceData.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.Application;

public interface IPortalDbContext
{
    DbSet<Workflow> Workflows { get; }
    DbSet<WikiPage> WikiPages { get; }
    DbSet<WikiPageVersion> WikiPageVersions { get; }
    DbSet<ContentBlock> ContentBlocks { get; }
    DbSet<ContentBlockVersion> ContentBlockVersions { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}