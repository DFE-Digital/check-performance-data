using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.Features.LandingPage;

public interface ILandingPageRepository
{
    Task<List<OpenCheckingWindowDto>> GetOpenWindowsAsync(DateOnly now,
        IEnumerable<KeyStages> organisationKeyStages, CancellationToken cancellationToken);
}