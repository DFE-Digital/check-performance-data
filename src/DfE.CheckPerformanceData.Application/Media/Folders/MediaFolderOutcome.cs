namespace DfE.CheckPerformanceData.Application.Media.Folders;

/// <summary>What happened to a folder change, or to a move of images into a folder.</summary>
public enum MediaFolderOutcome
{
    Done,
    NotFound,
    ParentNotFound,
    NameInvalid,
    NameTaken,
    Cycle,
    NotEmpty
}
