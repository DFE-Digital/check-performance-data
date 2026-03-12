using Microsoft.EntityFrameworkCore;

public class Workflow
{
    public int Id { get; set; }
    public int Name { get; set; }
}

public class PortalDbContext : DbContext
{
    public DbSet<Workflow> Workflows { get; set; }
}