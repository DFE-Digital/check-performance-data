using System.Text.Json;
using System.Text.Json.Serialization;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.UnitTests.Journey;

/// <summary>
/// Every shipped question-flow file is valid: it loads, it is named for a change and a window
/// type, and its pages, questions and options fit together. These tests check structure only.
/// They name no journey, page or wording, so a new flow file is checked with no new test.
///
/// Nothing at runtime notices a broken file. A nextPageId with a typo ends the journey early, a
/// page that nothing reaches is never shown, and a misspelt key is dropped by the deserialiser.
/// </summary>
public sealed class QuestionFlowConfigValidityTests
{
    // FileSystemQuestionFlowClient's options, plus Disallow. The app ignores an unknown key, so a
    // misspelt key ("nextPage" for "nextPageId") would silently change the journey. Keys are
    // case-insensitive, as in the app.
    private static readonly JsonSerializerOptions StrictOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// Pages a controller opens directly, not through a nextPageId. Each one needs a reason.
    /// </summary>
    private static readonly HashSet<(string File, string PageId)> EntryPages =
    [
        // ResultIssueController.LateResultsGuidancePageId: the late results guidance, shown before the journey starts.
        ("IncorrectGrade_Post16.json", "check-late-results"),
    ];

    private static readonly string[] PupilKeys = ["primary", "match"];

    public static TheoryData<string> FlowFiles()
    {
        var data = new TheoryData<string>();
        foreach (var file in Directory.GetFiles(LocateFlowsDirectory(), "*.json").Order())
            data.Add(Path.GetFileName(file));
        return data;
    }

    [Theory]
    [MemberData(nameof(FlowFiles))]
    public void The_file_name_is_a_change_and_a_window_type(string file)
    {
        // FileSystemQuestionFlowClient loads "{WhatToChange}_{CheckingWindowType}.json". Any other
        // name is a file no journey ever loads.
        var parts = Path.GetFileNameWithoutExtension(file).Split('_');

        Assert.True(parts.Length == 2
            && Enum.GetNames<WhatToChange>().Contains(parts[0], StringComparer.Ordinal)
            && Enum.GetNames<CheckingWindowType>().Contains(parts[1], StringComparer.Ordinal),
            $"{file}: the name must be {{WhatToChange}}_{{CheckingWindowType}}.json.");
    }

    [Theory]
    [MemberData(nameof(FlowFiles))]
    public void The_file_loads_with_no_unknown_keys(string file)
    {
        var config = Load(file);

        Assert.NotEmpty(config.Pages);
    }

    [Theory]
    [MemberData(nameof(FlowFiles))]
    public void Page_ids_are_unique(string file)
    {
        var duplicates = Load(file).Pages.GroupBy(p => p.Id).Where(g => g.Count() > 1).Select(g => g.Key);

        Assert.Empty(duplicates);
    }

    [Theory]
    [MemberData(nameof(FlowFiles))]
    public void The_first_page_exists(string file)
    {
        var config = Load(file);

        Assert.Contains(config.FirstPageId, config.Pages.Select(p => p.Id));
    }

    [Theory]
    [MemberData(nameof(FlowFiles))]
    public void Every_next_page_exists(string file)
    {
        var config = Load(file);
        var ids = config.Pages.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);

        foreach (var page in config.Pages)
        foreach (var target in NextPageIds(page))
            Assert.True(ids.Contains(target),
                $"{file}: page '{page.Id}' goes to '{target}', which is not a page in this file.");
    }

    [Theory]
    [MemberData(nameof(FlowFiles))]
    public void Every_page_can_be_reached(string file)
    {
        var config = Load(file);
        var pages = config.Pages.ToDictionary(p => p.Id, StringComparer.Ordinal);

        var reached = new HashSet<string>(StringComparer.Ordinal);
        var toVisit = new Stack<string>(
            EntryPages.Where(e => e.File == file).Select(e => e.PageId).Append(config.FirstPageId));
        while (toVisit.Count > 0)
        {
            var id = toVisit.Pop();
            if (!pages.TryGetValue(id, out var page) || !reached.Add(id)) continue;
            foreach (var target in NextPageIds(page)) toVisit.Push(target);
        }

        Assert.Empty(pages.Keys.Except(reached));
    }

    [Theory]
    [MemberData(nameof(FlowFiles))]
    public void Question_ids_are_unique_on_each_page(string file)
    {
        foreach (var page in Load(file).Pages)
        {
            var duplicates = page.Questions.GroupBy(q => q.Id).Where(g => g.Count() > 1).Select(g => g.Key);
            Assert.True(!duplicates.Any(),
                $"{file}: page '{page.Id}' has more than one question with id '{string.Join("', '", duplicates)}'.");
        }
    }

    [Theory]
    [MemberData(nameof(FlowFiles))]
    public void Every_radio_and_checkbox_has_options_with_unique_values(string file)
    {
        foreach (var page in Load(file).Pages)
        foreach (var question in page.Questions.Where(q => q.Type is QuestionType.Radio or QuestionType.Checkbox))
        {
            Assert.True(question.Options is { Count: > 0 },
                $"{file}: {question.Type} question '{question.Id}' on page '{page.Id}' has no options.");

            var values = question.Options!.Select(o => o.Value).ToList();
            Assert.True(values.Distinct(StringComparer.Ordinal).Count() == values.Count,
                $"{file}: question '{question.Id}' on page '{page.Id}' has two options with the same value.");
        }
    }

    [Theory]
    [MemberData(nameof(FlowFiles))]
    public void Every_autocomplete_names_a_data_source(string file)
    {
        foreach (var page in Load(file).Pages)
        foreach (var question in page.Questions.Where(q => q.Type == QuestionType.Autocomplete))
            Assert.False(string.IsNullOrWhiteSpace(question.DataSource),
                $"{file}: autocomplete question '{question.Id}' on page '{page.Id}' has no dataSource.");
    }

    [Theory]
    [MemberData(nameof(FlowFiles))]
    public void Every_pupil_search_page_sets_a_filter_and_a_key(string file)
    {
        foreach (var page in Load(file).Pages.Where(p => p.Type == PageType.PupilSearch))
        {
            Assert.True(page.PupilFilter is not null,
                $"{file}: pupil search page '{page.Id}' has no pupilFilter.");
            Assert.True(PupilKeys.Contains(page.PupilKey, StringComparer.Ordinal),
                $"{file}: pupil search page '{page.Id}' has pupilKey '{page.PupilKey}', not 'primary' or 'match'.");
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static IEnumerable<string> NextPageIds(JourneyPage page) =>
        page.Questions.SelectMany(q => q.Options ?? []).Select(o => o.NextPageId)
            .Append(page.NextPageId)
            .OfType<string>();

    private static QuestionFlowConfig Load(string file) =>
        JsonSerializer.Deserialize<QuestionFlowConfig>(
            File.ReadAllText(Path.Combine(LocateFlowsDirectory(), file)), StrictOptions)!;

    private static string LocateFlowsDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(
                dir.FullName,
                "src", "DfE.CheckPerformanceData.Web", "Data", "QuestionFlows");
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException(
            "Could not locate src/DfE.CheckPerformanceData.Web/Data/QuestionFlows from " +
            AppContext.BaseDirectory);
    }
}
