using DfE.CheckPerformanceData.Application;
using DfE.CheckPerformanceData.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.Infrastructure.Persistence;

public class PortalDbContext(DbContextOptions<PortalDbContext> options) : DbContext(options), IPortalDbContext
{
    public DbSet<CheckingWindow> CheckingWindows => Set<CheckingWindow>();
}