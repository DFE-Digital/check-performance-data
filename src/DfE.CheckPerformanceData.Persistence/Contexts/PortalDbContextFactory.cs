using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DfE.CheckPerformanceData.Persistence.Contexts;

public class PortalDbContextFactory : IDesignTimeDbContextFactory<PortalDbContext>
{
    public PortalDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<PortalDbContext>();
        optionsBuilder.UseNpgsql("Host=localhost;Database=cypd;Username=postgres;Password=postgres");

        return new PortalDbContext(optionsBuilder.Options, null);
    }
}