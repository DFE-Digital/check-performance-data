using System.ComponentModel.DataAnnotations;
using DfE.CheckPerformanceData.Domain.Enums;
using Microsoft.AspNetCore.Mvc.RazorPages;

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

public class WindowEditItem
{
    public required Guid Id { get; init; }
    private string baseEditUrl => $"/admin/windows/{Id}";
    public required string Title { get; set; }
    public string TitleLink
    {
        get => $"{baseEditUrl}/title";
    }
    public bool IsOpen { get; set; } = false;
    public required DateTime StartDate { get; set; }
    public string StartDateLink {
        get => $"{baseEditUrl}/start-date";
    }
    public required DateTime EndDate { get; set; }
    public string EndDateLink {
        get => $"{baseEditUrl}/end-date";
    }

    public required KeyStages KeyStage { get; set; }
    public required CheckingWindowType CheckingWindowType { get; set; }
    public string CheckingWindowTypeLink {
        get => $"{baseEditUrl}/checking-window-type";
    }
    //data
    public string IngressFile { get; set; }
    public string IngressFileLink
    {
        get => $"{baseEditUrl}/ingress-file";
    }
    public string OutputPath { get; set; }
    public string SchemaFile { get; set; }
    public string SchemaFileLink
    {
        get => $"{baseEditUrl}/schema-file";
    }
    public bool ValidationSuccess { get; set; } = false;
    public DateTime? ValidationDate { get; set; }
    public bool IsPublished { get; set; } = false;
    public Guid? PublishedId { get; set; }
}

public class WindowTitleEditItem
{
    public Guid WindowId { get; init; }
    [Required(ErrorMessage = "Title can not be empty"), MaxLength(200, ErrorMessage = "Title must be 200 characters or less")]
    public string Title { get; init; }
}