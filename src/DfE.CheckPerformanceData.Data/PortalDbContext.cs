using DfE.CheckPerformanceData.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.Data;

public class PortalDbContext(DbContextOptions<PortalDbContext> options) : DbContext(options)
{
    public DbSet<Workflow> Workflows => Set<Workflow>();
}