namespace DfE.CheckPerformanceData.Application.Wiki;

public sealed class DuplicateWikiPageException : Exception
{
    public string Title { get; }
    public int? ParentId { get; }

    public DuplicateWikiPageException(string title, int? parentId)
        : base($"A page with the title \"{title}\" already exists at this location. Choose a different title.")
    {
        Title = title;
        ParentId = parentId;
    }
}
