using System.Text.Json;
using System.Text.Json.Serialization;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Application.Journey.DateRules;
using DfE.CheckPerformanceData.Application.Journey.Validators;
using DfE.CheckPerformanceData.Domain.Enums;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Journey;

/// <summary>
/// Pins every name a shipped question-flow config references by string — <c>validator</c>
/// to an <see cref="IFormatValidator"/>, and <c>optionalWhen</c>/<c>visibleWhen</c> to an
/// <see cref="IJourneyCondition"/>. Nothing throws on an unresolved name at runtime: a
/// typo'd validator fails open (the format check is skipped, letting malformed answers
/// through) and a typo'd condition fails closed (the question stays mandatory, the option
/// stays hidden). Either way the config silently stops doing what it says.
/// </summary>
public sealed class QuestionFlowValidatorAlignmentTests
{
    // Mirrors FileSystemQuestionFlowClient's deserialization options.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public void EveryReferencedValidatorName_HasAnImplementation()
    {
        var implementedNames = ImplementedValidatorNames();

        var referenced = AllFlowQuestions()
            .Where(q => !string.IsNullOrWhiteSpace(q.Question.Validator))
            .Select(q => (q.File, Name: q.Question.Validator!, q.Question.Id))
            .Distinct();

        foreach (var (file, name, questionId) in referenced)
        {
            Assert.True(implementedNames.Contains(name),
                $"{file}: question '{questionId}' references validator '{name}', but no " +
                $"IFormatValidator implements it — the format check would silently be skipped.");
        }
    }

    /// <summary>
    /// The same guard for <c>optionalWhen</c> / <c>visibleWhen</c> condition names. These fail
    /// closed rather than open — an unresolved name leaves a question mandatory and an option
    /// hidden — so a typo silently disables the behaviour the config was asking for instead of
    /// throwing anywhere. Nothing else would catch it.
    /// </summary>
    [Fact]
    public void EveryReferencedConditionName_HasAnImplementation()
    {
        var implementedNames = ImplementedConditionNames();

        var referenced = AllFlowQuestions()
            .SelectMany(q => ReferencedConditionNames(q.Question)
                .Select(name => (q.File, Name: name, q.Question.Id)))
            .Distinct();

        foreach (var (file, name, questionId) in referenced)
        {
            Assert.True(implementedNames.Contains(name),
$"{file}: question '{questionId}' references journey condition '{name}', but no " +
                $"IJourneyCondition implements it - the condition silently fails closed.");
        }
    }

    /// <summary>
    /// The guard above only proves a condition TYPE exists - it scans the assembly. It cannot tell
    /// the difference between "the condition was written" and "the running app has it", because
    /// <see cref="OptionVisibilityService"/> and <see cref="QuestionOptionalityService"/> resolve
    /// conditions from the container.
    ///
    /// So a condition class added without its DependencyManager line leaves every other test green
    /// while the app sees an unregistered name and fails CLOSED: the gated option silently
    /// disappears for every school, and the gated mandatory rule silently stays on. That is a
    /// worse outcome than the bug the condition was written to fix, and nothing else in the suite
    /// observes it.
    /// </summary>
    [Fact]
    public void EveryImplementedCondition_IsRegisteredInTheContainer()
    {
var services = new ServiceCollection();
        services.AddApplicationDependencies();

        // Read the service descriptors instead of calling GetServices. Resolving would CONSTRUCT
        // every condition, and a condition is entitled to take services the Application layer does
        // not register - the not-on-roll college list is registered by the web host alongside the
        // blob clients. Registration is a claim about which types are wired, so compare types and
        // leave construction to the host that actually owns those dependencies.
        var registered = services
            .Where(d => d.ServiceType == typeof(IJourneyCondition))
            .Select(d => d.ImplementationType)
            .OfType<Type>()
            .ToHashSet();

        foreach (var conditionType in ImplementedConditionTypes())
        {
            Assert.True(registered.Contains(conditionType),
                $"IJourneyCondition '{conditionType.Name}' exists in the assembly but is not " +
                "registered in DependencyManager - the app resolves no condition for the name, so " +
                "anything gated on it fails closed.");
        }
    }

    /// <summary>
    /// The same guard for a page's <c>requireAtLeastOneWhen</c>, which gates the "answer at least
    /// one of these" rule on a condition (e.g. only the "Other" not-on-roll reason must be
    /// evidenced). It fails closed — an unresolved name leaves the rule ON — so a typo silently
    /// makes evidence mandatory for every branch that shares the page. Also pins that the gate is
    /// never set without the flag it gates, which would be inert.
    /// </summary>
    [Fact]
    public void EveryReferencedPageConditionName_HasAnImplementation()
    {
        var implementedNames = ImplementedConditionNames();

        foreach (var (file, page) in AllFlowPages())
        {
            if (page.RequireAtLeastOneWhen is not { Count: > 0 } names) continue;

            Assert.True(page.RequireAtLeastOne,
                $"{file}: page '{page.Id}' sets requireAtLeastOneWhen but not requireAtLeastOne — " +
                "the gate has no rule to gate and does nothing.");

            foreach (var name in names)
            {
                Assert.True(implementedNames.Contains(name),
                    $"{file}: page '{page.Id}' references journey condition '{name}', but no " +
                    "IJourneyCondition implements it — the requireAtLeastOne rule would silently " +
                    "stay on for every branch reaching the page.");
            }
        }
    }

    /// <summary>
    /// The same guard for the one rule that runs from code rather than config: the cross-field
    /// date rules in <see cref="PageDateRules"/> address questions by id, and nothing at runtime
    /// notices if the page or a question id is renamed in the flow JSON — the rules would simply
    /// stop matching and the validation would silently stop happening.
    /// </summary>
    [Fact]
    public void PageDateRules_QuestionIds_MatchTheShippedFlowConfig()
    {
        var page = AllFlowPages()
            .SingleOrDefault(p => p.Page.Id == PageDateRules.EalDetailsPageId);

        Assert.True(page.Page is not null,
            $"No flow config contains a page '{PageDateRules.EalDetailsPageId}', which " +
            $"PageDateRules addresses by id — its date rules would never run.");

        string[] ruleIds =
        [
            PageDateRules.StartedAtSchool,
            PageDateRules.FirstEnglishSchool,
            PageDateRules.ArrivedInEngland
        ];

        foreach (var id in ruleIds)
        {
            var question = page.Page!.Questions.SingleOrDefault(q => q.Id == id);
            Assert.True(question is not null,
                $"{page.File}: page '{page.Page.Id}' has no question '{id}', which PageDateRules " +
                $"compares — that rule would silently never fire.");
            Assert.True(question!.Type == QuestionType.Date,
                $"{page.File}: question '{id}' is {question.Type}, not Date — PageDateRules reads " +
                $"it as a date answer and would always see it as unanswered.");
        }

        // The optionality of the arrival date is load-bearing: three of the seven rules are only
        // evaluated when it has been filled in.
        Assert.True(
            page.Page!.Questions.Single(q => q.Id == PageDateRules.ArrivedInEngland).Optional,
            $"{page.File}: '{PageDateRules.ArrivedInEngland}' is no longer optional. PageDateRules " +
            $"treats a blank answer as 'rule not applicable' rather than an error — if the question " +
            $"is now mandatory, confirm that is still the intended behaviour.");
    }

    /// <summary>
    /// The same guard for the removal journey date rules in
    /// <see cref="RemovalJourneyDateRules"/>. The seven removal page ids and their three date
    /// question ids are addressed by code, and nothing at runtime notices if one is renamed in
    /// the flow JSON — the future-date check would simply stop matching and the validation would
    /// silently stop happening.
    /// </summary>
    [Fact]
    public void RemovalJourneyDateRules_PageAndQuestionIds_MatchTheShippedFlowConfig()
    {
        string[] removalPageIds =
        [
            RemovalJourneyDateRules.PermanentExclusionPageId,
            RemovalJourneyDateRules.ChildMissingEducationPageId,
            RemovalJourneyDateRules.PupilDiedPageId,
            RemovalJourneyDateRules.ElectiveHomeEducationPageId,
            RemovalJourneyDateRules.PermanentlyExcludedPageId,
            RemovalJourneyDateRules.PermanentlyLeftEnglandPageId,
            RemovalJourneyDateRules.StudentDiedPageId
        ];

        foreach (var pageId in removalPageIds)
        {
            var page = AllFlowPages().SingleOrDefault(p => p.Page.Id == pageId);
            Assert.True(page.Page is not null,
                $"No flow config contains a page '{pageId}', which RemovalJourneyDateRules " +
                $"addresses by id — its future-date rule would never run.");

            var inScopeDateQuestions = page.Page!.Questions
                .Where(q => q.Type == QuestionType.Date
                    && RemovalJourneyDateRules.RemovalDateQuestionIds.Contains(q.Id))
                .ToList();

            Assert.True(inScopeDateQuestions.Count == 1,
                $"{page.File}: page '{pageId}' should carry exactly one in-scope Date question " +
                $"(a single removal/exclusion date compared against today), but found " +
                $"{inScopeDateQuestions.Count}: {string.Join(", ", inScopeDateQuestions.Select(q => q.Id))}.");
        }

        // The three date question ids must all be covered by the config, so a typo'd constant
        // (or a renamed question) cannot silently leave one rule without a target page.
        foreach (var questionId in RemovalJourneyDateRules.RemovalDateQuestionIds)
        {
            var page = AllFlowPages().FirstOrDefault(p =>
                p.Page.Questions.Any(q => q.Id == questionId));
            Assert.True(page.Page is not null,
                $"No flow config page contains a question '{questionId}', which " +
                $"RemovalJourneyDateRules evaluates — that rule would silently never fire.");
        }
    }

    /// <summary>
    /// The same guard for the Add-a-pupil journey's date rules (AB#297310) in
    /// <see cref="AddJourneyDateRules"/>. Its two page ids and their date question ids are
    /// addressed by code, and nothing at runtime notices if one is renamed in the flow JSON —
    /// the future-date and admission-not-before-birth checks would simply stop matching and the
    /// validation would silently stop happening.
    /// </summary>
    [Fact]
    public void AddJourneyDateRules_PageAndQuestionIds_MatchTheShippedFlowConfig()
    {
        (string PageId, string QuestionId)[] pairs =
        [
            (AddJourneyDateRules.LearnerDetailsPageId, AddJourneyDateRules.DateOfBirth),
            (AddJourneyDateRules.AdmissionDetailsPageId, AddJourneyDateRules.AdmissionDate)
        ];

        foreach (var (pageId, questionId) in pairs)
        {
            var pages = AllFlowPages().Where(p => p.Page.Id == pageId).ToList();
            Assert.True(pages.Count > 0,
                $"No flow config contains a page '{pageId}', which AddJourneyDateRules " +
                $"addresses by id — its future-date rule would never run.");

            foreach (var page in pages)
            {
                var question = page.Page.Questions.SingleOrDefault(q => q.Id == questionId);
                Assert.True(question is not null,
                    $"{page.File}: page '{pageId}' has no question '{questionId}', which " +
                    $"AddJourneyDateRules compares — that rule would silently never fire.");
                Assert.True(question!.Type == QuestionType.Date,
                    $"{page.File}: question '{questionId}' is {question.Type}, not Date — " +
                    $"AddJourneyDateRules reads it as a date answer and would always see it as unanswered.");
            }
        }
    }

    /// <summary>
    /// The admission-not-before-birth rule compares two dates the user enters on different pages,
    /// so it only ever fires if both live in the same flow — a flow carrying one page without the
    /// other would leave the comparison permanently one-sided and silently unenforced.
    /// </summary>
    [Fact]
    public void AddJourneyDateRules_BothDatePages_LiveInTheSameFlow()
    {
        var flowsWithLearnerDetails = FlowFilesContaining(AddJourneyDateRules.LearnerDetailsPageId);
        var flowsWithAdmissionDetails = FlowFilesContaining(AddJourneyDateRules.AdmissionDetailsPageId);

        Assert.NotEmpty(flowsWithLearnerDetails);
        Assert.Equal(flowsWithLearnerDetails, flowsWithAdmissionDetails);
    }

    private static SortedSet<string> FlowFilesContaining(string pageId) =>
        new(AllFlowPages().Where(p => p.Page.Id == pageId).Select(p => p.File), StringComparer.Ordinal);

    /// <summary>
    /// Pins the scenario-006 invalid-date wording. The removal journeys substitute the
    /// question's own <c>validationFailure</c> for every invalid-date failure, so this string
    /// is the message users see — a copy edit here changes what gets shown. The generic EAL
    /// wording must NOT replace it (that page is excluded from the substitution).
    /// </summary>
    [Fact]
    public void PermanentlyLeftEngland_RollDateValidationFailure_IsThePinnedWording()
    {
        var page = AllFlowPages().Single(p => p.Page.Id == RemovalJourneyDateRules.PermanentlyLeftEnglandPageId);

        var validationFailure = page.Page.Questions
            .Single(q => q.Id == RemovalJourneyDateRules.DateRemovedFromRoll)
            .ValidationFailure;

        Assert.Equal(
            "Enter the date {pupilName} was removed from your school roll",
            validationFailure);
    }

    /// <summary>
    /// Pins the issue-496 label wording on the KS4 Remove "permanent-exclusion" page. The
    /// question's <c>title</c> substitutes the pupil's name at render time; the design copy adds
    /// "permanently" and must NOT gain a "the" before the name (the Figma note: ignore the word
    /// "the" before the student name). Hint and help copy are unchanged by design.
    /// </summary>
    [Fact]
    public void PermanentExclusion_DfeNumberTitle_IsThePinnedWording()
    {
        var page = AllFlowPages().Single(p => p.Page.Id == "permanent-exclusion");

        var question = page.Page.Questions.Single(q => q.Id == "permanent-exclusion-dfe-number");

        Assert.Equal(
            "What is the DfE number of the school which permanently excluded {pupilName}?",
            question.Title);
        Assert.DoesNotContain("the {pupilName}", question.Title);
        Assert.Equal("For example, 123/4567 or 1234567", question.Hint);
        Assert.Equal("How can I find a DfE number?", question.QuestionHelpTitle);
    }

    /// <summary>
    /// Pins the issue-496 error wording (FR-004) on the same question. Every failure — blank or
    /// malformed — must surface this one message: the config's <c>validationFailure</c> covers the
    /// blank case and the <c>PermanentExclusionDfeNumber</c> validator's
    /// <see cref="IFormatValidator.FailureMessage"/> the malformed case, so both must read
    /// identically to this pinned copy (see WholeNumberFormatValidator's doc comment for the rule).
    /// </summary>
    [Fact]
    public void PermanentExclusion_DfeNumberValidationFailure_IsThePinnedWording()
    {
        var page = AllFlowPages().Single(p => p.Page.Id == "permanent-exclusion");

        var question = page.Page.Questions.Single(q => q.Id == "permanent-exclusion-dfe-number");

        Assert.Equal("PermanentExclusionDfeNumber", question.Validator);
        Assert.Equal(
            "Enter the 7 digit DfE number of the school which permanently excluded the pupil",
            question.ValidationFailure);
    }

    /// <summary>
    /// Pins the other four DfE-number questions byte-for-byte (FR-008): the issue-496 wording names
    /// the school that excluded the pupil, which is TRUE ONLY of the permanent-exclusion page, so
    /// none of its four siblings in the KS4 Remove flow change. Their copy (and their DfeNumber
    /// validator and required-message) is pinned here so a future edit to the permanent-exclusion
    /// question cannot be Copy-Paste'd onto a sibling by mistake.
    /// </summary>
    [Fact]
    public void OtherDfeNumberQuestions_KeepTheirExistingWording()
    {
        (string PageId, string QuestionId, string Title, string? ValidationFailure, bool Optional)[] pinned =
        [
            ("permanently-excluded", "permanently-excluded-dfe-number",
                "What is the DfE number of the school {pupilName} went to?", null, true),
            ("dual-registered-moved", "dual-registered-moved-dfe-number",
                "What is the DfE number of the school {pupilName}'s exam results should be transferred to?",
                "Enter the DfE number of the school {pupilName}'s exam results should be transferred to", false),
            ("completed-ks4-elsewhere", "completed-ks4-elsewhere-dfe-number",
                "What is the DfE number of the school or college where {pupilName} completed KS4?",
                "Enter the DfE number of the school or college where {pupilName} completed KS4", false),
            ("year-group-change-higher", "year-group-higher-dfe-number",
                "What is the DfE number of the school {pupilName} was previously reported at the year of KS4?",
                "Enter the DfE number of the school {pupilName} was previously reported at in the year of KS4", false)
        ];

        foreach (var (pageId, questionId, title, validationFailure, optional) in pinned)
        {
            var page = AllFlowPages().Single(p => p.Page.Id == pageId);
            var question = page.Page.Questions.Single(q => q.Id == questionId);

            Assert.Equal(title, question.Title);
            Assert.Equal("DfeNumber", question.Validator);
            Assert.Equal(validationFailure, question.ValidationFailure);
            Assert.Equal(optional, question.Optional);
        }
    }

    /// <summary>
    /// AB#304117: the KS4 Remove "Child missing education" page offers exactly Ground H, Ground I
    /// and Other, in that order. The option *values* are what the rules engine reads
    /// (AnswerFieldMap copies <c>why-removed</c> into <c>childMissingEducationGround</c>, and the
    /// seed's <c>PMIE-OTHER-REJ</c> branch compares against "other"), so a renamed value would
    /// silently route every Other request to Scrutiny instead of auto-rejecting it. The two
    /// Ground options are pinned byte-for-byte because only Other may auto-reject.
    /// </summary>
    [Fact]
    public void ChildMissingEducation_WhyRemoved_OffersGroundHGroundIAndOther()
    {
        var page = AllFlowPages().Single(p => p.Page.Id == "child-missing-education");
        Assert.Equal("Remove_KS4June.json", page.File);

        var question = page.Page.Questions.Single(q => q.Id == "why-removed");
        Assert.Equal(QuestionType.Radio, question.Type);
        Assert.NotNull(question.Options);

        Assert.Equal(
            ["not-returned-after-agreed-leave", "no-agreed-leave-or-reason", "other"],
            question.Options!.Select(o => o.Value).ToArray());

        var groundH = question.Options[0];
        Assert.Equal("Not come back after an agreed period of leave", groundH.Label);
        Assert.Equal("Ground H of the School Attendance Regulations 2024", groundH.SubLabel);

        var groundI = question.Options[1];
        Assert.Equal("Been absent for a long time with no agreed leave and no clear reason", groundI.Label);
        Assert.Equal("Ground I of the School Attendance Regulations 2024", groundI.SubLabel);

        var other = question.Options[2];
        Assert.Equal("Other", other.Label);
        Assert.Null(other.SubLabel);
        Assert.Null(other.NextPageId); // the page's own nextPageId ("evidence") applies to every option
    }

    private static IEnumerable<string> ReferencedConditionNames(Question question)
    {
        foreach (var name in question.OptionalWhen ?? [])
            yield return name;

        foreach (var option in question.Options ?? [])
        foreach (var name in option.VisibleWhen ?? [])
            yield return name;
    }

    private static IEnumerable<Type> ImplementedConditionTypes()
        => typeof(IJourneyCondition).Assembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false }
                && typeof(IJourneyCondition).IsAssignableFrom(t));

    private static HashSet<string> ImplementedConditionNames()
    {
        return ImplementedConditionTypes()
            .Select(t => CreateWithSubstitutes<IJourneyCondition>(t).Name)
            .ToHashSet(StringComparer.Ordinal);
    }

    // A condition may take services (SchoolCanRecordNotOnRoll reads the college list), so each
    // constructor parameter gets a substitute. Name must not depend on them.
    private static T CreateWithSubstitutes<T>(Type type)
    {
        var constructor = type.GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
        var arguments = constructor.GetParameters()
            .Select(p => Substitute.For([p.ParameterType], []))
            .ToArray();
        return (T)constructor.Invoke(arguments);
    }

    private static HashSet<string> ImplementedValidatorNames()
    {
        var validatorTypes = typeof(IFormatValidator).Assembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false }
                && typeof(IFormatValidator).IsAssignableFrom(t));

        return validatorTypes
            .Select(t => ((IFormatValidator)Activator.CreateInstance(t)!).Name)
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// A single-question page must NOT set a page-level <c>title</c>.
    ///
    /// <c>JourneyViewModelBuilder</c> sets <c>IsPageHeading = isSingleQuestion &amp;&amp;
    /// string.IsNullOrEmpty(page.Title)</c>, and <c>Page.cshtml</c> renders its own <c>h1</c> only
    /// when the page has MORE than one question. So a single-question page that also sets a page
    /// title renders no <c>h1</c> at all — that title feeds only the browser title, and the
    /// question's legend/label drops from <c>--l</c> to <c>--m</c>.
    ///
    /// A page with no heading is a WCAG 2.2 AA failure that nothing else notices: the page still
    /// renders, still validates and still submits. Use <c>pageTitle</c> when the browser title needs
    /// to differ from the question (e.g. to keep a pupil name out of it).
    /// </summary>
    [Fact]
    public void SingleQuestionPages_LeaveThePageTitleEmpty_SoTheQuestionBecomesTheHeading()
    {
        foreach (var (file, page) in AllFlowPages())
        {
            if (page.Type != PageType.Question || page.Questions.Count != 1) continue;

            Assert.True(string.IsNullOrEmpty(page.Title),
                $"{file}: page '{page.Id}' has a single question but also sets a page-level title " +
                $"('{page.Title}'), which suppresses IsPageHeading and leaves the page with no <h1>. " +
                "Remove the page title; use 'pageTitle' if the browser title must differ.");
        }
    }

    /// <summary>
    /// The other half of the same rule, and the one the accessibility audit caught (#375): every
    /// page that does NOT hand its heading to a single question must set a <c>title</c>, because
    /// nothing else supplies one.
    ///
    /// <c>Page.cshtml</c> renders <c>ResolvedTitle</c> as the <c>h1</c> only when it is non-null,
    /// so a multi-question <c>Question</c> page or a <c>Content</c> page with no title renders no
    /// heading at all. <c>EvidenceUpload.cshtml</c> guards on the same condition.
    /// <c>ResultDetails.cshtml</c> and <c>QualificationDetails.cshtml</c> render the <c>h1</c>
    /// unconditionally, which is worse rather than better — an absent title gives an empty
    /// heading, which a screen reader announces as nothing.
    ///
    /// Eight pages in Remove_KS4June.json were in this state when the audit ran. The failure is
    /// invisible without a screen reader: the page renders, validates and submits exactly as it
    /// should, and the browser title is populated from <c>pageTitle</c> either way.
    /// </summary>
    [Fact]
    public void PagesWithoutASingleQuestionHeading_SetATitle_SoTheyRenderAnH1()
    {
        // PupilSearch, ResultSearch and QualificationSearch are excluded: their views build the
        // heading themselves (a govuk-label-wrapper h1 around the search input, or a hard-coded
        // fallback), so the rule for them is not "a title is present".
        PageType[] needATitle =
        [
            PageType.Content, PageType.EvidenceUpload,
            PageType.ResultDetails, PageType.QualificationDetails
        ];

        foreach (var (file, page) in AllFlowPages())
        {
            var singleQuestionSuppliesTheHeading =
                page.Type == PageType.Question && page.Questions.Count == 1;
            if (singleQuestionSuppliesTheHeading) continue;

            if (page.Type != PageType.Question && !needATitle.Contains(page.Type)) continue;

            Assert.False(string.IsNullOrWhiteSpace(page.Title),
                $"{file}: page '{page.Id}' ({page.Type}, {page.Questions.Count} questions) sets no " +
                "title, so nothing renders an <h1> — no question is promoted to the heading and the " +
                "page-level heading needs a title to render. Add 'title'; keep 'pageTitle' for the " +
                "browser title if it must differ.");
        }
    }

    /// <summary>
    /// An optional question's title must not spell out "(Optional)" — <c>JourneyViewModelBuilder</c>
    /// appends it, so a title containing it renders "… (Optional) (Optional)".
    /// </summary>
    [Fact]
    public void OptionalQuestions_DoNotRepeatTheOptionalSuffixInTheirTitle()
    {
        foreach (var (file, question) in AllFlowQuestions())
        {
            if (!question.Optional) continue;

            Assert.False(question.Title.Contains("(Optional)", StringComparison.OrdinalIgnoreCase),
                $"{file}: optional question '{question.Id}' spells out \"(Optional)\" in its title " +
                $"('{question.Title}'). The view model appends it, so this renders it twice.");
        }
    }

    /// <summary>
    /// A Checkbox option's nextPageId could only ever be ambiguous — several boxes may be
    /// ticked — so the engine ignores it and the page's own nextPageId decides. A config that
    /// sets one is silently not doing what it says, which is what this pins.
    /// </summary>
    [Fact]
    public void NoCheckboxOption_TriesToBranchTheFlow()
    {
        foreach (var (file, question) in AllFlowQuestions())
        {
            if (question.Type != QuestionType.Checkbox) continue;

            Assert.NotNull(question.Options);
            Assert.NotEmpty(question.Options!);
            Assert.All(question.Options!, option =>
                Assert.True(option.NextPageId is null,
                    $"{file}: checkbox question '{question.Id}' option '{option.Value}' sets " +
                    "nextPageId, but a multi-select cannot branch — the page's nextPageId decides."));
        }
    }

    /// <summary>
    /// The removal reason "Other" on a 16-19 window asks which years to remove the student from
    /// before it asks for evidence. Pinned because the reason option's nextPageId is the only
    /// thing that puts the page in the flow — repointing it back at "evidence" would drop the
    /// question with nothing else failing.
    /// </summary>
    [Fact]
    public void RemovePost16_OtherReason_AsksWhichYearsBeforeEvidence()
    {
        var pages = AllFlowPages().Where(p => p.File == "Remove_Post16.json").Select(p => p.Page).ToList();

        var reason = pages.Single(p => p.Id == "reason").Questions.Single(q => q.Id == "reason");
        var other = reason.Options!.Single(o => o.Value == "other");
        Assert.Equal("other", other.NextPageId);

        var years = pages.Single(p => p.Id == "other");
        Assert.Equal("other-evidence", years.NextPageId);
        var question = Assert.Single(years.Questions);
        Assert.Equal(QuestionType.Checkbox, question.Type);
        Assert.Equal(3, question.Options!.Count);
    }

    private static IEnumerable<(string File, Question Question)> AllFlowQuestions() =>
        AllFlowPages().SelectMany(p => p.Page.Questions.Select(q => (p.File, q)));

    /// <summary>
    /// The KS4 merge journey's second-record page labels itself as a CYPMD-ID search
    /// ("What is the CYPMD ID of the second duplicate record to be merged?" / "Start typing ID to
    /// search records"), so the config has to narrow its matching to that field. Asserted against
    /// the shipped file rather than an in-memory config because nothing sets
    /// <c>UnmappedMemberHandling</c> on the deserialiser — a misspelt key is dropped silently, so
    /// only a test that reads the real file catches it.
    /// </summary>
    [Fact]
    public void MergeKs4June_MatchPage_NarrowsTheSearchToTheCypmdId()
    {
        var page = AllFlowPages().Single(p => p.File == "Merge_KS4June.json" && p.Page.Id == "select-match-pupil").Page;

        Assert.Equal(PupilSearchField.CypmdId, page.PupilSearchField);
    }

    /// <summary>
    /// The narrowing is a property of one page, so nothing else may set it — least of all the
    /// 16-19 merge journey, whose second-record page carries the same shape and the same copy and is
    /// explicitly out of scope (#510). This is the test that stops the follow-up happening by
    /// accident: when it is done, this is the assertion that has to change.
    /// </summary>
    [Fact]
    public void NoPageOtherThanTheKs4MergeMatchPage_ConfiguresASearchField()
    {
        var configured = AllFlowPages()
            .Where(p => p.Page.PupilSearchField is not null)
            .Select(p => $"{p.File}:{p.Page.Id}")
            .ToList();

        Assert.Equal(["Merge_KS4June.json:select-match-pupil"], configured);
    }

    /// <summary>
    /// AB#304118 / FR-003. The copy is what made this page's behaviour a defect rather than a
    /// design: the label and hint already named the CYPMD ID while the search matched names. They
    /// are pinned verbatim so a later "improvement" cannot quietly reword the page the fix was
    /// built to match.
    /// </summary>
    [Fact]
    public void MergeKs4June_MatchPage_KeepsItsCyPmdIdCopyVerbatim()
    {
        var page = AllFlowPages().Single(p => p.File == "Merge_KS4June.json" && p.Page.Id == "select-match-pupil").Page;

        Assert.Equal("What is the CYPMD ID of the second duplicate record to be merged?", page.Title);
        Assert.Equal("Start typing ID to search records", page.Subheading);
    }

    private static IEnumerable<(string File, JourneyPage Page)> AllFlowPages()
    {
        foreach (var file in Directory.GetFiles(LocateFlowsDirectory(), "*.json").Order())
        {
            var config = JsonSerializer.Deserialize<QuestionFlowConfig>(File.ReadAllText(file), JsonOptions)!;
            foreach (var page in config.Pages)
                yield return (Path.GetFileName(file), page);
        }
    }

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
