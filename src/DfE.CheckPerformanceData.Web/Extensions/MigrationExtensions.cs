using DfE.CheckPerformanceData.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.Web.Extensions;

public static class MigrationExtensions
{
    public static async Task MigrateDatabaseAsync(this WebApplication app)
    {
        // if (!app.Environment.IsDevelopment())
        //     return;


        
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();

        // Some environments had AddChangeRequest applied under the old timestamp before the migration
        // file was renamed. Normalise the history so MigrateAsync sees it as already applied.
        // Guard with an existence check: fresh databases won't have __EFMigrationsHistory yet.
        await db.Database.ExecuteSqlRawAsync("""
            DO $$
            BEGIN
              IF EXISTS (
                SELECT 1 FROM information_schema.tables
                WHERE table_name = '__EFMigrationsHistory'
              ) THEN
                UPDATE "__EFMigrationsHistory"
                SET "MigrationId" = '20260520102855_AddChangeRequest'
                WHERE "MigrationId" = '20260518121634_AddChangeRequest';
              END IF;
            END $$;
            """);
        
        await db.Database.MigrateAsync();
        
        
    }
}
