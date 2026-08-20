using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;

namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels;

public class WindowViewModel(IReadOnlyList<WindowListItem> windows)
{
    public IReadOnlyList<WindowListItem> Windows { get; } = windows;
}

public class WindowListItem
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public bool IsOpen { get; init; } = false;
    public bool IsPublished { get; init; } = false;
}

public class WindowEditItem : AdminPage
{ 
    private string BaseEditUrl => $"/admin/windows/{WindowId}";
    public required string Title { get; set; }
    public string TitleLink
    {
        get => $"{BaseEditUrl}/title";
    }
    public string TurnaroundCommitment { get; set; } = string.Empty;
    public string TurnaroundCommitmentLink
    {
        get => $"{BaseEditUrl}/turnaround-commitment";
    }
    public bool IsOpen { get; set; } = false;
    public required DateTime StartDate { get; set; }
    public string StartDateLink {
        get => $"{BaseEditUrl}/start-date";
    }
    public required DateTime EndDate { get; set; }
    public string EndDateLink {
        get => $"{BaseEditUrl}/end-date";
    }

    public required KeyStages KeyStage { get; set; }
    public required CheckingWindowType CheckingWindowType { get; set; }
    public string CheckingWindowTypeLink {
        get => $"{BaseEditUrl}/checking-window-type";
    }
    //data
    /// <summary>One row pair per ingress dataset. A Post16 window has two (included +
    /// non-included); every other type has one.</summary>
    public IReadOnlyList<DatasetSummaryRow> Datasets { get; set; } = [];

    public string? OutputPath { get; set; }
    public bool ValidationSuccess { get; set; } = false;
    public DateTime? ValidationDate { get; set; }
    public bool IsPublished { get; set; } = false;
    public Guid? PublishedId { get; set; }
    
    // Every dataset must have both files — a Post16 window is not validatable until both the
    // included and non-included CSV/schema pairs are chosen, because they ingest in one run.
    private bool HasRequiredFiles => Datasets.Count > 0 && Datasets.All(d => d.IsComplete);

    private bool HasValidDates
    {
        get
        {
            var today = DateTime.UtcNow.Date;

            return StartDate.Date >= today
                   && EndDate.Date >= today
                   && EndDate.Date >= StartDate.Date;
        }
    }

    public bool IsValidatable => HasValidDates && HasRequiredFiles;

}

public sealed class DatasetSummaryRow
{
    public required Guid WindowId { get; init; }
    public required string Name { get; init; }
    public required string Label { get; init; }
    public string? IngressFile { get; init; }
    public string? SchemaFile { get; init; }

    public string IngressFileLink => $"/admin/windows/{WindowId}/ingress-file/{Name}";
    public string SchemaFileLink => $"/admin/windows/{WindowId}/schema-file/{Name}";

    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(IngressFile) && !string.IsNullOrWhiteSpace(SchemaFile);
}
