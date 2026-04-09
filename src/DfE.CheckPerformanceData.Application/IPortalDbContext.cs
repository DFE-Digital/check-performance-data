using DfE.CheckPerformanceData.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.Application;

public interface IPortalDbContext
{
    DbSet<Workflow> Workflows { get; }
    DbSet<WikiPage> WikiPages { get; }
    DbSet<WikiPageVersion> WikiPageVersions { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}