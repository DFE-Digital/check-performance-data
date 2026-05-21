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
        await db.Database.ExecuteSqlRawAsync("""                                                       
                                             UPDATE "__EFMigrationsHistory"                                                             
                                             SET "MigrationId" = '20260520102855_AddChangeRequest'                                      
                                             WHERE "MigrationId" = '20260518121634_AddChangeRequest'                                    
                                             """);    
        
        await db.Database.MigrateAsync();
        
        
    }
}
