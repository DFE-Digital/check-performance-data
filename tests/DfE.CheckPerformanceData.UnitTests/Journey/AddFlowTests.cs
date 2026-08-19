using System.Text.Json;
using System.Text.Json.Serialization;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.Journey.DateRules;

namespace DfE.CheckPerformanceData.Application.UnitTests.Journey;

/// <summary>
/// Pins the shipped <c>Add_KS4June.json</c>, <c>Add_KS4Autumn.json</c> and <c>Add_KS2.json</c>
/// flows (AB#297310). Page and question ids are a serialization contract — they are written into
/// session state and into the ChangeRequests row — so a rename after merge orphans stored data.
/// The copy is pinned because it is what the user reads.
/// </summary>
public sealed class AddFlowTests
{
    // Mirrors QuestionFlowBlobClient's deserialization options.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private const string Ks4June = "Add_KS4June";
    private const string Ks4Autumn = "Add_KS4Autumn";
    private const string Ks2 = "Add_KS2";

    private static readonly Dictionary<string, QuestionFlowConfig> Flows = new()
    {
        [Ks4June] = Load(Ks4June),
        [Ks4Autumn] = Load(Ks4Autumn),
        [Ks2] = Load(Ks2)
    };

    private static readonly string[] AllFlowNames = [Ks4June, Ks4Autumn, Ks2];

    private static JourneyPage Page(string flow, string id) =>
        Flows[flow].Pages.SingleOrDefault(p => p.Id == id)
        ?? throw new Xunit.Sdk.XunitException($"{flow}.json has no page '{id}'.");

    private static Question Question(string flow, string pageId, string questionId) =>
        Page(flow, pageId).Questions.Single(q => q.Id == questionId);

    // ── Structure: shared by all three flows ─────────────────────────────────

    [Theory]
    [MemberData(nameof(AllFlows))]
    public void The_journey_starts_at_learner_details(string flow)
    {
        Assert.Equal(AddJourneyDateRules.LearnerDetailsPageId, Flows[flow].FirstPageId);
    }

    [Theory]
    [MemberData(nameof(AllFlows))]
    public void The_page_chain_is_learner_details_then_admission_details_then_evidence(string flow)
    {
        Assert.Equal(
            [AddJourneyDateRules.LearnerDetailsPageId, AddJourneyDateRules.AdmissionDetailsPageId, "evidence"],
            Flows[flow].Pages.Select(p => p.Id).ToArray());

        Assert.Equal(AddJourneyDateRules.AdmissionDetailsPageId, Page(flow, AddJourneyDateRules.LearnerDetailsPageId).NextPageId);
        Assert.Equal("evidence", Page(flow, AddJourneyDateRules.AdmissionDetailsPageId).NextPageId);
        Assert.Null(Page(flow, "evidence").NextPageId);
    }

    [Theory]
    [MemberData(nameof(AllFlows))]
    public void Only_learner_details_is_flagged_pupilFromAnswers(string flow)
    {
        Assert.True(Page(flow, AddJourneyDateRules.LearnerDetailsPageId).PupilFromAnswers);
        Assert.False(Page(flow, AddJourneyDateRules.AdmissionDetailsPageId).PupilFromAnswers);
        Assert.False(Page(flow, "evidence").PupilFromAnswers);
    }

    // ── Learner details questions ────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(AllFlows))]
    public void FirstName_and_LastName_are_required_freetext_capped_at_150(string flow)
    {
        var firstName = Question(flow, AddJourneyDateRules.LearnerDetailsPageId, "first-name");
        var lastName = Question(flow, AddJourneyDateRules.LearnerDetailsPageId, "last-name");

        Assert.Equal(QuestionType.FreeText, firstName.Type);
        Assert.False(firstName.Optional);
        Assert.Equal(150, firstName.CharacterLimit);
        Assert.Equal("Enter the pupil's first name", firstName.ValidationFailure);

        Assert.Equal(QuestionType.FreeText, lastName.Type);
        Assert.False(lastName.Optional);
        Assert.Equal(150, lastName.CharacterLimit);
        Assert.Equal("Enter the pupil's last name", lastName.ValidationFailure);
    }

    [Theory]
    [MemberData(nameof(AllFlows))]
    public void DateOfBirth_is_a_required_date(string flow)
    {
        var question = Question(flow, AddJourneyDateRules.LearnerDetailsPageId, AddJourneyDateRules.DateOfBirth);

        Assert.Equal(QuestionType.Date, question.Type);
        Assert.False(question.Optional);
        Assert.Equal("Enter the pupil's date of birth", question.ValidationFailure);
    }

    [Theory]
    [MemberData(nameof(AllFlows))]
    public void Sex_offers_exactly_the_three_LDS_bound_values(string flow)
    {
        var question = Question(flow, AddJourneyDateRules.LearnerDetailsPageId, "sex");

        Assert.Equal(QuestionType.Radio, question.Type);
        Assert.False(question.Optional);
        Assert.Equal(["F", "M", "U"], question.Options!.Select(o => o.Value).ToArray());
    }

    [Theory]
    [MemberData(nameof(AllFlows))]
    public void Upn_is_optional_freetext_capped_at_13(string flow)
    {
        var question = Question(flow, AddJourneyDateRules.LearnerDetailsPageId, "upn");

        Assert.Equal(QuestionType.FreeText, question.Type);
        Assert.True(question.Optional);
        Assert.Equal(13, question.CharacterLimit);
    }

    // ── Admission details questions ──────────────────────────────────────────

    [Theory]
    [MemberData(nameof(AllFlows))]
    public void AdmissionDate_is_a_required_date(string flow)
    {
        var question = Question(flow, AddJourneyDateRules.AdmissionDetailsPageId, AddJourneyDateRules.AdmissionDate);

        Assert.Equal(QuestionType.Date, question.Type);
        Assert.False(question.Optional);
        Assert.Equal("Enter the date {pupilName} was admitted to your school", question.ValidationFailure);
    }

    [Theory]
    [MemberData(nameof(AllFlows))]
    public void SenStatus_offers_exactly_the_three_LDS_bound_values_in_E_K_N_order(string flow)
    {
        var question = Question(flow, AddJourneyDateRules.AdmissionDetailsPageId, "sen-status");

        Assert.Equal(QuestionType.Radio, question.Type);
        Assert.False(question.Optional);
        Assert.Equal(["E", "K", "N"], question.Options!.Select(o => o.Value).ToArray());
    }

    [Theory]
    [InlineData(Ks4June)]
    [InlineData(Ks4Autumn)]
    public void YearGroup_offers_10_and_11_on_KS4_flows(string flow)
    {
        var question = Question(flow, AddJourneyDateRules.AdmissionDetailsPageId, "year-group");

        Assert.Equal(QuestionType.Radio, question.Type);
        Assert.Equal(["10", "11"], question.Options!.Select(o => o.Value).ToArray());
    }

    [Fact]
    public void YearGroup_offers_3_to_6_on_the_KS2_flow()
    {
        var question = Question(Ks2, AddJourneyDateRules.AdmissionDetailsPageId, "year-group");

        Assert.Equal(QuestionType.Radio, question.Type);
        Assert.Equal(["3", "4", "5", "6"], question.Options!.Select(o => o.Value).ToArray());
    }

    // ── Evidence page ─────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(AllFlows))]
    public void Evidence_page_mirrors_the_previously_implemented_shape(string flow)
    {
        var page = Page(flow, "evidence");
        Assert.Equal(PageType.EvidenceUpload, page.Type);
        Assert.Equal("Provide evidence for the addition of {pupilName}", page.Title);

        var upload = Question(flow, "evidence", "evidence");
        Assert.Equal(QuestionType.FileUpload, upload.Type);
        Assert.True(upload.Optional);

        var howEvidenceSupports = Question(flow, "evidence", "how-evidence-supports");
        Assert.Equal(QuestionType.TextArea, howEvidenceSupports.Type);
        Assert.True(howEvidenceSupports.Optional);
        Assert.Equal(1000, howEvidenceSupports.CharacterLimit);
    }

    // ── Routing contract ──────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(AllFlows))]
    public void No_question_carries_useAsRequestType(string flow)
    {
        // The routing contract string for this journey must stay the bare "Add" — no question
        // may override it, or ExtractCurrentReasonType/BuildRequestTypeDescription would resolve
        // a different string than the AmendmentType the row is stored with.
        Assert.DoesNotContain(Flows[flow].Pages.SelectMany(p => p.Questions), q => q.UseAsRequestType);
    }

    // ── Convention guards (mirrors IncorrectGradeFlowTests / QuestionFlowValidatorAlignmentTests) ──

    [Theory]
    [MemberData(nameof(AllFlows))]
    public void MultiQuestionPages_CarryAPageTitle(string flow)
    {
        foreach (var page in Flows[flow].Pages.Where(p => p.Type == PageType.Question && p.Questions.Count > 1))
            Assert.False(string.IsNullOrEmpty(page.Title), $"{flow}: page '{page.Id}' has multiple questions but no page title.");
    }

    [Theory]
    [MemberData(nameof(AllFlows))]
    public void No_pageTitle_embeds_the_pupil_name(string flow)
    {
        foreach (var page in Flows[flow].Pages)
            Assert.False((page.PageTitle ?? "").Contains("{pupilName}"), $"{flow}: page '{page.Id}' leaks {{pupilName}} into the browser title.");
    }

    [Theory]
    [MemberData(nameof(AllFlows))]
    public void Every_non_optional_question_has_validationFailure_copy(string flow)
    {
        foreach (var question in Flows[flow].Pages.SelectMany(p => p.Questions).Where(q => !q.Optional))
            Assert.False(string.IsNullOrWhiteSpace(question.ValidationFailure), $"{flow}: '{question.Id}' has no validationFailure.");
    }

    [Theory]
    [MemberData(nameof(AllFlows))]
    public void No_question_id_is_reused_across_the_flow(string flow)
    {
        var ids = Flows[flow].Pages.SelectMany(p => p.Questions).Select(q => q.Id).ToArray();
        Assert.Equal(ids.Length, ids.Distinct().Count());
    }

    [Theory]
    [MemberData(nameof(AllFlows))]
    public void The_config_key_is_the_journey_and_window_type_this_flow_serves(string flow)
    {
        Assert.Equal(flow, Path.GetFileNameWithoutExtension(LocateFlowFile(flow)));
    }

    public static IEnumerable<object[]> AllFlows() => AllFlowNames.Select(f => new object[] { f });

    private static QuestionFlowConfig Load(string flow)
        => JsonSerializer.Deserialize<QuestionFlowConfig>(File.ReadAllText(LocateFlowFile(flow)), JsonOptions)!;

    private static string LocateFlowFile(string flow)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(
                dir.FullName,
                "src", "DfE.CheckPerformanceData.Web", "Data", "QuestionFlows", $"{flow}.json");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException($"Could not locate {flow}.json from " + AppContext.BaseDirectory);
    }
}
