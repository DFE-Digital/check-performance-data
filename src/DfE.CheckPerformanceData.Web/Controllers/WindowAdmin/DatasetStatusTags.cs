using DfE.CheckPerformanceData.Application.WindowManagement;

namespace DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;

/// <summary>The tag the admin's Data tab shows for a <see cref="DatasetStatus"/>.</summary>
/// <remarks>
/// There is no default case: a new status with no wording throws rather than showing an admin a
/// blank or wrong tag. <c>DatasetStatusTests</c> pins that every status has its own.
/// </remarks>
public static class DatasetStatusTags
{
    public static string Label(DatasetStatus status) => status switch
    {
        DatasetStatus.Live => "Live",
        DatasetStatus.NotValidated => "Not validated",
        DatasetStatus.NotSupplied => "Not supplied",
        DatasetStatus.Retired => "Retired",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
    };

    public static string CssClass(DatasetStatus status) => status switch
    {
        DatasetStatus.Live => "govuk-tag--green",
        DatasetStatus.NotValidated => "govuk-tag--yellow",
        DatasetStatus.NotSupplied => "govuk-tag--light-blue",
        DatasetStatus.Retired => "govuk-tag--grey",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
    };
}
