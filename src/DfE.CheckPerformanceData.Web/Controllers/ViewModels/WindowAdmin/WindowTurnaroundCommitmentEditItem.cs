using System.ComponentModel.DataAnnotations;

namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;

public sealed class WindowTurnaroundCommitmentEditItem : AdminPage
{
    [MaxLength(200, ErrorMessage = "Turnaround commitment must be 200 characters or less")]
    public string? TurnaroundCommitment { get; init; }
}