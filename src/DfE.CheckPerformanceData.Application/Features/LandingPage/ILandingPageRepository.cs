using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.Features.LandingPage;

public interface ILandingPageRepository
{
    Task<List<OpenCheckingWindowDto>> GetOpenWindowsAsync(DateTimeOffset now,
        IEnumerable<KeyStages> organisationKeyStages, CancellationToken cancellationToken);
}