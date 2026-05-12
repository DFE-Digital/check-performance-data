using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.LandingPage;

public interface ILandingPageRepository
{
    Task<List<CheckingWindowDto>> GetOpenWindowsAsync(DateTime now, string urn, CancellationToken cancellationToken);
}