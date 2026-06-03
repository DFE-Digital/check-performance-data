using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Web.Controllers;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Controllers;

public class PupilSuggestionsControllerTests
{
    private readonly ICheckYourPupilDataService _service = Substitute.For<ICheckYourPupilDataService>();
    private readonly PupilSuggestionsController _sut;

    private static readonly Guid WindowId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public PupilSuggestionsControllerTests()
    {
        _sut = new PupilSuggestionsController(_service);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("a")]           // too short
    [InlineData("   ")]         // whitespace
    public async Task Suggestions_WhenQueryTooShort_ReturnsEmptyArray(string? query)
    {
        var result = await _sut.Suggestions(WindowId, query, PupilFilter.Included, null);

        var json = Assert.IsType<JsonResult>(result);
        Assert.Equal(Array.Empty<object>(), json.Value);
        await _service.DidNotReceive().GetPupilSuggestionsAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<PupilFilter>(), Arg.Any<Guid?>());
    }

    [Fact]
    public async Task Suggestions_WhenQueryTooLong_ReturnsEmptyArray()
    {
        var query = new string('x', 101);

        var result = await _sut.Suggestions(WindowId, query, PupilFilter.Included, null);

        var json = Assert.IsType<JsonResult>(result);
        Assert.Equal(Array.Empty<object>(), json.Value);
    }

    [Fact]
    public async Task Suggestions_WithValidQuery_CallsServiceWithCorrectFilter()
    {
        _service.GetPupilSuggestionsAsync(WindowId, "Smi", PupilFilter.Included, null)
            .Returns([new PupilSuggestionDto(Guid.NewGuid(), "Smith, Jane, 01/01/2000")]);

        await _sut.Suggestions(WindowId, "Smi", PupilFilter.Included, null);

        await _service.Received(1).GetPupilSuggestionsAsync(WindowId, "Smi", PupilFilter.Included, null);
    }

    [Fact]
    public async Task Suggestions_WithFilterAll_PassesFilterAllToService()
    {
        _service.GetPupilSuggestionsAsync(WindowId, "Jo", PupilFilter.All, null)
            .Returns([]);

        await _sut.Suggestions(WindowId, "Jo", PupilFilter.All, null);

        await _service.Received(1).GetPupilSuggestionsAsync(WindowId, "Jo", PupilFilter.All, null);
    }

    [Fact]
    public async Task Suggestions_WithExcludePupilId_PassesExcludeIdToService()
    {
        var excludeId = Guid.NewGuid();
        _service.GetPupilSuggestionsAsync(WindowId, "Jo", PupilFilter.All, excludeId)
            .Returns([]);

        await _sut.Suggestions(WindowId, "Jo", PupilFilter.All, excludeId);

        await _service.Received(1).GetPupilSuggestionsAsync(WindowId, "Jo", PupilFilter.All, excludeId);
    }

    [Fact]
    public async Task Suggestions_ReturnsSuggestionsAsIdAndLabel()
    {
        var pupilId = Guid.NewGuid();
        _service.GetPupilSuggestionsAsync(WindowId, "Sm", PupilFilter.Included, null)
            .Returns([new PupilSuggestionDto(pupilId, "Smith, Jane, 01/01/2000")]);

        var result = await _sut.Suggestions(WindowId, "Sm", PupilFilter.Included, null);

        var json = Assert.IsType<JsonResult>(result);
        var items = Assert.IsAssignableFrom<IEnumerable<object>>(json.Value);
        var item = items.Single();
        var type = item.GetType();
        Assert.Equal(pupilId, type.GetProperty("id")!.GetValue(item));
        Assert.Equal("Smith, Jane, 01/01/2000", type.GetProperty("label")!.GetValue(item));
    }
}
