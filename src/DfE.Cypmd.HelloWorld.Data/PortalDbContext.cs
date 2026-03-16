using DfE.Cypmd.HelloWorld.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace DfE.Cypmd.HelloWorld.Data;

public class PortalDbContext(DbContextOptions<PortalDbContext> options) : DbContext(options)
{
    public DbSet<Workflow> Workflows => Set<Workflow>();
}