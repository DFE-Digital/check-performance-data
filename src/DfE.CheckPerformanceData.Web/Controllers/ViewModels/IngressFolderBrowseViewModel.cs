using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels;

public class IngressFolderBrowseViewModel
{
    public required Guid WindowId { get; init; }

    /// <summary>
    /// The container currently being browsed, or <c>null</c> when choosing a container.
    /// </summary>
    public string? Container { get; init; }

    public string? CurrentPath { get; init; }
    public string? ParentPath { get; init; }

    /// <summary>
    /// Container names when <see cref="Container"/> is <c>null</c>; otherwise sub-folders
    /// (blob prefixes) within the current container/path.
    /// </summary>
    public required IReadOnlyList<string> Folders { get; init; }

    public required IReadOnlyList<string> Files { get; init; }

    public bool IsChoosingContainer => Container is null;

    /// <summary>Which of the window's datasets this file is being chosen for, e.g. "pupils",
    /// "included", "nonincluded".</summary>
    public string Dataset { get; init; } = "pupils";

    /// <summary>Human label for the page heading, e.g. "Included pupils".</summary>
    public string DatasetLabel { get; init; } = "Pupils";

    /// <summary>
    /// The checking exercise that consumes this dataset (#319). Part of every link on the page,
    /// because a dataset name is only unique within one exercise.
    /// </summary>
    public CheckingExerciseType Exercise { get; init; }

    /// <summary>Route prefix shared by every link and the form action on this page.</summary>
    public string BaseUrl => $"/admin/windows/{WindowId}/{Exercise}/ingress-file/{Dataset}";
}