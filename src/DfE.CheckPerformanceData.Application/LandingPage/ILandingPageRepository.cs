using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.LandingPage;

public interface ILandingPageRepository
{
    Task<List<CheckingWindowDto>> GetOpenWindowsAsync(DateTime now,
        IEnumerable<KeyStages> organisationKeyStages, CancellationToken cancellationToken);
}