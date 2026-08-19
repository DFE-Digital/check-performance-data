using System.Reflection;
using System.Text.Json;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.ResultsEnquiry;
using DfE.CheckPerformanceData.Web.Controllers;
using DfE.CheckPerformanceData.Web.Session;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Controllers;

// AB#296648 / AB#296999: the autocomplete source behind the "which of {pupil}'s results is
// incorrect?" page. The pupil is taken from the SESSION, never from the query string — a caller
// must not be able to enumerate another pupil's results by guessing an id.
public sealed class ResultSuggestionsControllerTests
{
    private static readonly Guid WindowId = Guid.Parse("6C2E1F4A-9B7D-4E38-8A15-3D9C2B4E7F01");
    private const string Laestab = "860/4070";
    private const string CypmdId = "500001";

    private readonly IStudentResultsClient _results = Substitute.For<IStudentResultsClient>();
    private readonly ICurrentUserService _currentUser = Substitute.For<ICurrentUserService>();
    private readonly FakeSession _session = new();
    private readonly ResultSuggestionsController _sut;

    public ResultSuggestionsControllerTests()
    {
        _currentUser.OrganisationLaestab.Returns(Laestab);

        var httpContext = new DefaultHttpContext();
        httpContext.Features.Set<ISessionFeature>(new TestSessionFeature(_session));

        _sut = new ResultSuggestionsController(_results, _currentUser)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };
    }

    private static StudentResultRecord Result(
        string qan, string qualName, string session = "S2024", string grade = "5",
        string source = ResultsFileTags.Post16Main, string cypmdId = CypmdId) => new()
        {
            CypmdId = cypmdId,
            Qan = qan,
            QualificationName = qualName,
            SyllabusCode = "1BS0",
            Session = session,
            Grade = grade,
            SourceFile = source
        };

    private static readonly StudentResultRecord[] BillysResults =
    [
        Result("6037116X", "GCSE (9-1) Bus. Studs:Single"),
        Result("60181576", "GCSE (9-1) French", source: ResultsFileTags.Post16LateResults1),
        Result("60370683", "Pearson BTEC L1/L2 Tech Award in Sport")
    ];

    private static PupilDto Pupil(string cypmdId) => new()
    {
        Id = Guid.NewGuid(),
        Firstname = "Billy",
        Surname = "B",
        Sex = "M",
        DateOfBirth = "12/03/2007",
        Age = 19,
        Cypmd_Id = cypmdId,
        Identifier = "9900000001"
    };

    private void SelectPupil(string cypmdId = CypmdId) =>
        _session.SetRequestState(WindowId, new RequestState
        {
            SelectedPupilId = Guid.NewGuid().ToString(),
            SelectedPupil = Pupil(cypmdId)
        });

    private void HasResults(params StudentResultRecord[] results) =>
        _results.GetResultsAsync(WindowId, Laestab, CypmdId, Arg.Any<CancellationToken>())
            .Returns(results);

    // Reads the anonymous-object JSON payload back as (value, label) pairs.
    private static (string Value, string Label)[] Payload(IActionResult result)
    {
        var json = Assert.IsType<JsonResult>(result);
        var doc = JsonSerializer.SerializeToElement(json.Value);
        return [.. doc.EnumerateArray().Select(e =>
            (e.GetProperty("value").GetString()!, e.GetProperty("label").GetString()!))];
    }

    [Fact]
    public async Task Matching_by_subject_returns_the_pinned_label_and_composite_value()
    {
        SelectPupil();
        HasResults(BillysResults);

        var payload = Payload(await _sut.Suggestions(WindowId, "GCSE", default));

        Assert.Equal(
            [
                ("6037116X|S2024|16to19_MAIN", "GCSE (9-1) Bus. Studs:Single, QAN: 6037116X, Session: S2024"),
                ("60181576|S2024|16to19_LR1", "GCSE (9-1) French, QAN: 60181576, Session: S2024")
            ],
            payload);
    }

    [Fact]
    public async Task Subject_matching_is_case_insensitive_and_matches_mid_string()
    {
        SelectPupil();
        HasResults(BillysResults);

        var payload = Payload(await _sut.Suggestions(WindowId, "french", default));

        Assert.Equal([("60181576|S2024|16to19_LR1", "GCSE (9-1) French, QAN: 60181576, Session: S2024")], payload);
    }

    [Fact]
    public async Task A_qan_prefix_matches()
    {
        SelectPupil();
        HasResults(BillysResults);

        var payload = Payload(await _sut.Suggestions(WindowId, "6037", default));

        // 6037116X matches by QAN prefix; 60370683 does too.
        Assert.Equal(
            ["6037116X|S2024|16to19_MAIN", "60370683|S2024|16to19_MAIN"],
            payload.Select(p => p.Value).ToArray());
    }

    [Fact]
    public async Task A_qan_substring_that_is_not_a_prefix_does_not_match()
    {
        // Prefix-only on QAN: a substring match would make almost every numeric query match
        // everything, which is noise rather than help.
        SelectPupil();
        HasResults(BillysResults);

        Assert.Empty(Payload(await _sut.Suggestions(WindowId, "116X", default)));
    }

    [Fact]
    public async Task Same_qualification_in_two_sessions_is_distinguishable_by_label_and_by_value()
    {
        // AB#296648: "each result shows enough detail for me to identify the right one". Distinct
        // VALUES alone are not enough — the user reads the labels, so those must differ too.
        SelectPupil();
        HasResults(
            Result("6037116X", "GCSE (9-1) Bus. Studs:Single", session: "S2024"),
            Result("6037116X", "GCSE (9-1) Bus. Studs:Single", session: "S2023"));

        var payload = Payload(await _sut.Suggestions(WindowId, "Bus", default));

        Assert.Equal(
            ["6037116X|S2024|16to19_MAIN", "6037116X|S2023|16to19_MAIN"],
            payload.Select(p => p.Value).ToArray());
        Assert.Equal(
            [
                "GCSE (9-1) Bus. Studs:Single, QAN: 6037116X, Session: S2024",
                "GCSE (9-1) Bus. Studs:Single, QAN: 6037116X, Session: S2023"
            ],
            payload.Select(p => p.Label).ToArray());
    }

    [Fact]
    public async Task Same_qualification_in_two_source_files_yields_distinct_values()
    {
        SelectPupil();
        HasResults(
            Result("6037116X", "GCSE (9-1) Bus. Studs:Single", source: ResultsFileTags.Post16Main),
            Result("6037116X", "GCSE (9-1) Bus. Studs:Single", source: ResultsFileTags.Post16Revised));

        var payload = Payload(await _sut.Suggestions(WindowId, "Bus", default));

        Assert.Equal(
            ["6037116X|S2024|16to19_MAIN", "6037116X|S2024|16to19_Revised"],
            payload.Select(p => p.Value).ToArray());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("G")]
    public async Task A_query_shorter_than_two_characters_returns_nothing_and_never_reads_results(string? query)
    {
        SelectPupil();

        Assert.Empty(Payload(await _sut.Suggestions(WindowId, query, default)));
        await _results.DidNotReceiveWithAnyArgs().GetResultsAsync(default, default!, default!);
    }

    [Fact]
    public async Task A_query_over_a_hundred_characters_returns_nothing()
    {
        SelectPupil();

        Assert.Empty(Payload(await _sut.Suggestions(WindowId, new string('x', 101), default)));
        await _results.DidNotReceiveWithAnyArgs().GetResultsAsync(default, default!, default!);
    }

    [Fact]
    public async Task With_no_pupil_in_session_it_returns_nothing_and_never_reads_results()
    {
        // Fail closed: without a session pupil there is no authorised scope to search within.
        Assert.Empty(Payload(await _sut.Suggestions(WindowId, "GCSE", default)));
        await _results.DidNotReceiveWithAnyArgs().GetResultsAsync(default, default!, default!);
    }

    [Fact]
    public async Task A_session_pupil_with_no_cypmd_id_returns_nothing()
    {
        _session.SetRequestState(WindowId, new RequestState
        {
            SelectedPupilId = Guid.NewGuid().ToString(),
            SelectedPupil = Pupil(string.Empty)
        });

        Assert.Empty(Payload(await _sut.Suggestions(WindowId, "GCSE", default)));
        await _results.DidNotReceiveWithAnyArgs().GetResultsAsync(default, default!, default!);
    }

    [Fact]
    public async Task The_search_is_scoped_to_the_session_pupil_and_the_signed_in_school()
    {
        SelectPupil("500009");
        _results.GetResultsAsync(WindowId, Laestab, "500009", Arg.Any<CancellationToken>()).Returns([]);

        await _sut.Suggestions(WindowId, "GCSE", default);

        await _results.Received(1).GetResultsAsync(WindowId, Laestab, "500009", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Session_state_is_read_per_window_so_another_windows_pupil_is_not_used()
    {
        _session.SetRequestState(Guid.NewGuid(), new RequestState
        {
            SelectedPupilId = Guid.NewGuid().ToString(),
            SelectedPupil = Pupil(CypmdId)
        });

        Assert.Empty(Payload(await _sut.Suggestions(WindowId, "GCSE", default)));
    }

    [Fact]
    public async Task No_match_returns_an_empty_array_rather_than_an_error()
    {
        SelectPupil();
        HasResults(BillysResults);

        Assert.Empty(Payload(await _sut.Suggestions(WindowId, "Latin", default)));
    }

    [Fact]
    public void The_route_is_pinned_and_the_action_is_get_only()
    {
        var method = typeof(ResultSuggestionsController).GetMethod(nameof(ResultSuggestionsController.Suggestions))!;

        var route = method.GetCustomAttribute<RouteAttribute>();
        Assert.Equal("/results/suggestions", route?.Template);
        Assert.NotNull(method.GetCustomAttribute<HttpGetAttribute>());
    }

    [Fact]
    public void The_controller_is_not_anonymous()
    {
        // A school's result data must never be readable without signing in. The global fallback
        // policy authorises; an [AllowAnonymous] here would silently opt out of it.
        Assert.Null(typeof(ResultSuggestionsController)
            .GetCustomAttribute<Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute>());
    }

    private sealed class FakeSession : ISession
    {
        private readonly Dictionary<string, byte[]> _store = new();

        public bool TryGetValue(string key, out byte[] value) => _store.TryGetValue(key, out value!);
        public void Set(string key, byte[] value) => _store[key] = value;
        public void Remove(string key) => _store.Remove(key);
        public void Clear() => _store.Clear();
        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public bool IsAvailable => true;
        public string Id => "test-session";
        public IEnumerable<string> Keys => _store.Keys;
    }

    private sealed class TestSessionFeature(ISession session) : ISessionFeature
    {
        public ISession Session { get; set; } = session;
    }
}
