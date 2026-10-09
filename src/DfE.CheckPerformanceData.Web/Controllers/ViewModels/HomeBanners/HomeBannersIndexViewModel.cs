using DfE.CheckPerformanceData.Application.HomeBanners;

namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels.HomeBanners;

public sealed class HomeBannersIndexViewModel
{
    public required IReadOnlyList<HomeBannerDto> Banners { get; init; }
    /// <summary>UK wall-clock now, shown on the page so an editor can read the statuses against it.</summary>
    public required DateTime Now { get; init; }
}
