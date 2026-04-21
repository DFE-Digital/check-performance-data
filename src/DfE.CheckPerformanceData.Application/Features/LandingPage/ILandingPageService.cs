namespace DfE.CheckPerformanceData.Application.Features.LandingPage;

public interface ILandingPageService
{
    Task<LandingPageResult?> GetLandingPageDataAsync(CancellationToken cancellationToken);
}