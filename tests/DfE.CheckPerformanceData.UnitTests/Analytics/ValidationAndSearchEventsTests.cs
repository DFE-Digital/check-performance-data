using DfE.CheckPerformanceData.Application.Analytics;

namespace DfE.CheckPerformanceData.Application.UnitTests.Analytics;

public sealed class ValidationAndSearchEventsTests
{
    [Fact]
    public void ValidationErrorEvent_projects_count_codes_and_context()
    {
        var e = new ValidationErrorEvent
        {
            ErrorCount = 2,
            ErrorCodes = ["required", "bad_date"],
            WhatToChange = "Remove",
            FromSummary = true,
        };

        Assert.Equal("validation_error", e.EventType);
        var byName = e.Fields.ToDictionary(f => f.Name);
        Assert.Equal(2, byName["error_count"].Value);
        Assert.Equal(["required", "bad_date"], (IEnumerable<string>)byName["error_codes"].Value!);
        Assert.Equal("Remove", byName["what_to_change"].Value);
        Assert.Equal(true, byName["from_summary"].Value);
    }

    [Fact]
    public void ValidationErrorEvent_allows_null_context()
    {
        var e = new ValidationErrorEvent { ErrorCount = 1, ErrorCodes = ["no_selection"] };

        var byName = e.Fields.ToDictionary(f => f.Name);
        Assert.Null(byName["what_to_change"].Value);
        Assert.Null(byName["from_summary"].Value);
    }

    [Fact]
    public void ValidationErrorEvent_projects_error_fields_parallel_to_codes()
    {
        var e = new ValidationErrorEvent
        {
            ErrorCount = 2,
            ErrorCodes = ["required", "bad_date"],
            ErrorFields = ["howEvidenceSupports", "dateOfArrival"],
            WhatToChange = "Remove",
            FromSummary = false,
        };

        var byName = e.Fields.ToDictionary(f => f.Name);
        Assert.Equal(["howEvidenceSupports", "dateOfArrival"],
            (IEnumerable<string>)byName["error_fields"].Value!);
        Assert.False(byName["error_fields"].Hidden);
    }

    [Fact]
    public void ValidationErrorEvent_error_fields_defaults_to_null_and_is_omitted()
    {
        var e = new ValidationErrorEvent { ErrorCount = 1, ErrorCodes = ["no_selection"] };
        var byName = e.Fields.ToDictionary(f => f.Name);
        Assert.Null(byName["error_fields"].Value); // null values are omitted at send
    }

    [Fact]
    public void PupilDataSearchResultsEvent_projects_count_and_tab()
    {
        var e = new PupilDataSearchResultsEvent { ResultCount = 7, ActiveTab = "included" };

        Assert.Equal("pupil_data_search_results", e.EventType);
        var byName = e.Fields.ToDictionary(f => f.Name);
        Assert.Equal(7, byName["result_count"].Value);
        Assert.Equal("included", byName["active_tab"].Value);
    }
}
