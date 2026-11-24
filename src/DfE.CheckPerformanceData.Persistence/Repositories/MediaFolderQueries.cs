using DfE.CheckPerformanceData.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.Persistence.Repositories;

// Shared reads over the media folder tree.
internal static class MediaFolderQueries
{
    // The folder and every folder inside it, as a query that EF composes into the caller's SQL.
    // UNION (not UNION ALL) drops a row already found, so even a loop in bad data ends.
    public static IQueryable<Guid> SubtreeIds(IPortalDbContext context, Guid folderId) =>
        context.MediaFolders
            .FromSqlInterpolated($@"WITH RECURSIVE subtree AS (
                    SELECT * FROM media_folders WHERE id = {folderId}
                    UNION
                    SELECT child.* FROM media_folders child JOIN subtree parent ON child.parent_id = parent.id)
                SELECT * FROM subtree")
            .Select(f => f.Id);
}
