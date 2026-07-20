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
    public string? IngressFile { get; set; }
    public string IngressFileLink
    {
        get => $"{BaseEditUrl}/ingress-file";
    }
    public string? OutputPath { get; set; }
    public string? SchemaFile { get; set; }
    public string SchemaFileLink
    {
        get => $"{BaseEditUrl}/schema-file";
    }
    public bool ValidationSuccess { get; set; } = false;
    public DateTime? ValidationDate { get; set; }
    public bool IsPublished { get; set; } = false;
    public Guid? PublishedId { get; set; }
    
    private bool HasRequiredFiles =>
        !string.IsNullOrWhiteSpace(IngressFile) &&
        !string.IsNullOrWhiteSpace(SchemaFile);

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
