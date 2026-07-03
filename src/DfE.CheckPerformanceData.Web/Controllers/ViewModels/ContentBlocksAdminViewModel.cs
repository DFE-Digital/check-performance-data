using DfE.CheckPerformanceData.Application.ContentBlocks;

namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels;

// The content-blocks management list. EditKey, when set (via ?edit=), is the one block whose
// inline editor is open — at most one at a time, so only a single rich-text editor initialises.
public sealed class ContentBlocksAdminViewModel
{
    public IReadOnlyList<ContentBlockDto> Blocks { get; init; } = [];
    public string? EditKey { get; init; }
    // ?page= filter: when set, Blocks has already been narrowed to blocks whose
    // LastSeenPath matches this request path. PageTitle is looked up for display.
    public string? FilterPagePath { get; init; }
    public string? FilterPageTitle { get; init; }
    public int TotalBlockCount { get; init; }
}
