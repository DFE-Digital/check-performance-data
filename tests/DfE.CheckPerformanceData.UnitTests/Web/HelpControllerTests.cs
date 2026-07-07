using DfE.CheckPerformanceData.Application.ContentBlocks;
using DfE.CheckPerformanceData.Application.Settings;
using DfE.CheckPerformanceData.Application.Wiki;
using DfE.CheckPerformanceData.Web.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web;

// Wiki Index was retired: /help now resolves via PageController's catch-all against the
// PageNode tree. HelpController still exposes attribute-routed management endpoints
// (Seed, Search, Deleted, etc.); the tests below pin the Seed feedback contract.
public sealed class HelpControllerTests
{
	private readonly IWikiService _wikiService = Substitute.For<IWikiService>();

	[Fact]
	public async Task Seed_SetsAddedFeedback_WhenPagesCreated()
	{
		_wikiService.CreatePageIfMissingAsync(Arg.Any<CreateWikiPageDto>())
			.Returns(ci => new WikiPageCreationResult(
				new WikiPageDto { Id = 1, Title = ci.Arg<CreateWikiPageDto>().Title }, Created: true));
		var sut = CreateSeederController();

		var result = await sut.Seed();

		Assert.IsType<RedirectResult>(result);
		Assert.Equal("Added 21 sample pages.", sut.TempData["SeedResult"]);
	}

	[Fact]
	public async Task Seed_SetsAlreadyPresentFeedback_WhenNothingCreated()
	{
		_wikiService.CreatePageIfMissingAsync(Arg.Any<CreateWikiPageDto>())
			.Returns(ci => new WikiPageCreationResult(
				new WikiPageDto { Id = 1, Title = ci.Arg<CreateWikiPageDto>().Title }, Created: false));
		var sut = CreateSeederController();

		await sut.Seed();

		Assert.Equal("Sample pages are already present. Nothing was added.", sut.TempData["SeedResult"]);
	}

	private HelpController CreateSeederController()
	{
		var seeder = new WikiSeeder(_wikiService);
		return new HelpController(_wikiService, seeder, Substitute.For<ISettingService>(), Substitute.For<IContentBlockSearchService>(), NullLogger<HelpController>.Instance)
		{
			ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
			TempData = new TempDataDictionary(new DefaultHttpContext(), Substitute.For<ITempDataProvider>())
		};
	}
}
