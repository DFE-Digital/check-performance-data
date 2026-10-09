using DfE.CheckPerformanceData.Application.HomeBanners;

namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels.HomeBanners;

public sealed class HomeBannerVersionsViewModel
{
    public required HomeBannerDto Banner { get; init; }
    public required IReadOnlyList<HomeBannerVersionDto> Versions { get; init; }
}
