using System.ComponentModel.DataAnnotations;
using DfE.CheckPerformanceData.Application.HomeBanners;
using GovUk.Frontend.AspNetCore;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels.HomeBanners;

/// <summary>
/// Create/edit form for a start-page banner (#566). Dates are entered as a GOV.UK date input
/// plus hour and minute, the way exercise dates are (ExerciseDatesItem), and are UK wall-clock.
/// </summary>
public sealed class HomeBannerFormModel
{
    public int? Id { get; set; }

    [Required(ErrorMessage = "Enter a heading")]
    [StringLength(HomeBannerRules.MaxHeadingLength, ErrorMessage = "Heading must be 200 characters or fewer")]
    public string Heading { get; set; } = string.Empty;

    [Required(ErrorMessage = "Enter the banner text")]
    public string Body { get; set; } = string.Empty;

    public bool IsEnabled { get; set; }

    [DateInput(ErrorMessagePrefix = "Show from")]
    public DateTime? ShowFromDate { get; set; }

    [Range(0, 23, ErrorMessage = "Show from hour must be between 0 and 23")]
    public int? ShowFromHour { get; set; }

    [Range(0, 59, ErrorMessage = "Show from minute must be between 0 and 59")]
    public int? ShowFromMinute { get; set; }

    [DateInput(ErrorMessagePrefix = "Show until")]
    public DateTime? ShowUntilDate { get; set; }

    [Range(0, 23, ErrorMessage = "Show until hour must be between 0 and 23")]
    public int? ShowUntilHour { get; set; }

    [Range(0, 59, ErrorMessage = "Show until minute must be between 0 and 59")]
    public int? ShowUntilMinute { get; set; }

    public DateTime? ShowFrom => ShowFromDate?.Date.AddHours(ShowFromHour ?? 0).AddMinutes(ShowFromMinute ?? 0);
    public DateTime? ShowUntil => ShowUntilDate?.Date.AddHours(ShowUntilHour ?? 0).AddMinutes(ShowUntilMinute ?? 0);

    public bool IsNew => Id is null;
    public string PageTitle => IsNew ? "Create a banner" : "Edit banner";
    public string PostUrl => IsNew ? "/admin/home-banners/new" : $"/admin/home-banners/{Id}/edit";

    /// <summary>Cross-field rules the attributes cannot express. Call only when attribute validation passed.</summary>
    public void ValidateDates(ModelStateDictionary modelState)
    {
        if (ShowFromDate is null && (ShowFromHour is not null || ShowFromMinute is not null))
            modelState.AddModelError(nameof(ShowFromDate), "Enter a Show from date, or clear the Show from time");
        if (ShowUntilDate is null && (ShowUntilHour is not null || ShowUntilMinute is not null))
            modelState.AddModelError(nameof(ShowUntilDate), "Enter a Show until date, or clear the Show until time");
        if (ShowFrom is { } from && ShowUntil is { } until && until <= from)
            modelState.AddModelError(nameof(ShowUntilDate), "Show until must be after Show from");
    }

    public HomeBannerContent ToContent() => new(Heading, Body, IsEnabled, ShowFrom, ShowUntil);

    public static HomeBannerFormModel From(HomeBannerDto b) => new()
    {
        Id = b.Id,
        Heading = b.Heading,
        Body = b.Body,
        IsEnabled = b.IsEnabled,
        ShowFromDate = b.ShowFrom?.Date,
        ShowFromHour = b.ShowFrom?.Hour,
        ShowFromMinute = b.ShowFrom?.Minute,
        ShowUntilDate = b.ShowUntil?.Date,
        ShowUntilHour = b.ShowUntil?.Hour,
        ShowUntilMinute = b.ShowUntil?.Minute
    };
}
