using System.ComponentModel.DataAnnotations;

namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;

public sealed class WindowDateEditItem : AdminPage
{
    [Required(ErrorMessage = "Date can not be empty")]
    public DateTime? DateValue { get; init; }

    [Range(0, 23, ErrorMessage = "Hour must be between 0 and 23")]
    public int Hour { get; init; }

    [Range(0, 59, ErrorMessage = "Minute must be between 0 and 59")]
    public int Minute { get; init; }

    // The chosen day combined with the chosen time-of-day. Null until DateValue is supplied.
    public DateTime? DateTimeValue => DateValue?.Date.AddHours(Hour).AddMinutes(Minute);
}