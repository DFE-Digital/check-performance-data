using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.LandingPage;

public interface ILandingPageRepository
{
    Task<List<CheckingWindowDto>> GetOpenWindowsAsync(DateTime now,
        IEnumerable<KeyStages> organisationKeyStages, string laestab, CancellationToken cancellationToken);
    
    Task<List<CheckingWindowDto>> GetClosedWindowsAsync(DateTime now,
        IEnumerable<KeyStages> organisationKeyStages, string laestab, CancellationToken cancellationToken);
}