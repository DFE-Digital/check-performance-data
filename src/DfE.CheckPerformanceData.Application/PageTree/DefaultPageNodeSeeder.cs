namespace DfE.CheckPerformanceData.Application.PageTree;

// Ensures the default root folder nodes exist. Idempotent: creates only the ones missing (by path),
// so it is safe to run on every startup and never touches existing nodes or their children.
public sealed class DefaultPageNodeSeeder(IPageNodeService pageNodes)
{
    public async Task SeedAsync()
    {
        foreach (var (segment, title) in DefaultPageNodeRoots.All)
        {
            if (await pageNodes.GetNodeByPathAsync(segment) is null)
                await pageNodes.CreatePageAsync(null, segment, title, "folder", "system");
        }
    }
}
