using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Extensions;

namespace DfE.CheckPerformanceData.Web.Controllers.SubmittedRequest;

public sealed class SubmittedRequestViewModel
{
    public required Guid WindowId { get; init; }
    public required WhatToChange WhatToChange { get; init; }
    public required RequestStatus Status { get; init; }
    public bool ConfirmingDelete { get; init; }
    public required string PupilName { get; init; }
    public string? FirstRecordDisplay { get; init; }
    public string? SecondRecordDisplay { get; init; }
    public required IReadOnlyList<SubmittedRequestRow> Rows { get; init; }
    public required IReadOnlyList<SubmittedRequestFile> Files { get; init; }
    public required string ReferenceNumber { get; init; }
    public string? SubmittedByEmail { get; init; }
    public DateTime? SubmittedAt { get; init; }

    public string WhatToChangeLabel => WhatToChange switch
    {
        WhatToChange.Remove => "Remove a pupil from data",
        WhatToChange.Include => "Include a pupil in data",
        WhatToChange.Merge => "Merge duplicate pupil records",
        _ => WhatToChange.ToString()
    };

    public string WhatToChangeNoun => WhatToChange switch
    {
        WhatToChange.Remove => "removal",
        WhatToChange.Include => "inclusion",
        WhatToChange.Merge => "merge",
        _ => WhatToChange.ToString().ToLower()
    };

    public string SubmittedAtText => LondonTime.ToSubmittedAtText(SubmittedAt);

    public bool ShowDeleteButton => Status != RequestStatus.Withdrawn;

    public bool IsDraft => Status is RequestStatus.InProgress or RequestStatus.ReadyToSubmit;

    public string ByLineTitle => Status switch
    {
        RequestStatus.Withdrawn => "Withdrawn by",
        RequestStatus.NotSubmitted => "Saved by",
        RequestStatus.InProgress or RequestStatus.ReadyToSubmit => "Last saved by",
        _ => "Submitted by" // SubmittedUnCommitted, SubmittedCommitted
    };

    public bool ShowReferenceNumber => Status != RequestStatus.Withdrawn
                                     && Status != RequestStatus.NotSubmitted
                                     && !(ConfirmingDelete && IsDraft);

    public string ConfirmDeleteTitle => Status is RequestStatus.InProgress or RequestStatus.ReadyToSubmit
        ? $"Are you sure you want to delete {PupilName} from your saved requests"
        : $"Are you sure you want to delete {PupilName} from your submitted requests";
}

public sealed class SubmittedRequestRow
{
    public required string Title { get; init; }
    public required string DisplayValue { get; init; }
}

public sealed class SubmittedRequestFile
{
    public required string OriginalFileName { get; init; }
    public required string StoredFileName { get; init; }
    public required long FileSizeBytes { get; init; }

    public string FileType => Path.GetExtension(OriginalFileName).TrimStart('.').ToUpperInvariant();
    public string FormattedFileSize => $"{fileSizeKb:F2}KB";
    private double fileSizeKb => FileSizeBytes / 1024.0;
}
