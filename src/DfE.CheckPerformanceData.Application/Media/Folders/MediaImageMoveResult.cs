namespace DfE.CheckPerformanceData.Application.Media.Folders;

/// <summary>
/// The result of moving images into a folder. <see cref="Moved"/> counts the images whose folder
/// changed; an image that was already there, or is not in the library, is not counted.
/// </summary>
public sealed record MediaImageMoveResult(MediaFolderOutcome Outcome, int Moved, string? Error = null)
{
    public bool Succeeded => Outcome == MediaFolderOutcome.Done;
}
