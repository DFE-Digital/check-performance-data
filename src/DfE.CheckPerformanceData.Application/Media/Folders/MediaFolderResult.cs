namespace DfE.CheckPerformanceData.Application.Media.Folders;

/// <summary>
/// The result of a folder change. <see cref="Folder"/> is the folder as saved when the change was
/// made. <see cref="Error"/> is the message to show the editor when it was refused.
/// </summary>
public sealed record MediaFolderResult(MediaFolderOutcome Outcome, MediaFolderDto? Folder = null, string? Error = null)
{
    public bool Succeeded => Outcome == MediaFolderOutcome.Done;

    public static MediaFolderResult Done(MediaFolderDto? folder = null) => new(MediaFolderOutcome.Done, folder);

    public static MediaFolderResult Refused(MediaFolderOutcome outcome, string error) => new(outcome, null, error);
}
