using DfE.CheckPerformanceData.Application.HomeBanners;
using Microsoft.AspNetCore.Mvc;

namespace DfE.CheckPerformanceData.Web.ViewComponents;

// The start page's notification banners (#566). The service decides which are live; this only
// renders what it returns, and renders nothing at all when the list is empty.
public sealed class HomeBannersViewComponent(IHomeBannerService banners) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync() => View(await banners.GetLiveAsync());
}
