using System.Globalization;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.Queue;
using DfE.CheckPerformanceData.Application.RequestSubmission;
using DfE.CheckPerformanceData.Application.RulesEngine;
using DfE.CheckPerformanceData.Application.ZendeskClient;
using DfE.CheckPerformanceData.Infrastructure.ZendeskClient;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.RulesEngineWorker.Consumers;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Worker;

/// <summary>
/// Verifies that <see cref="ZendeskConsumer"/> composes the correct Zendesk
/// ticket for each <see cref="DecisionStatus"/>. The most important contract:
/// the ticket the human reviewer sees must say what the engine decided and which
/// rule got us there, and must carry the school and pupil identifiers.
/// </summary>
public sealed class ZendeskConsumerTicketCompositionTests
{
    private readonly IQueueService _queueService = Substitute.For<IQueueService>();
    private readonly IZendeskService _zendesk = Substitute.For<IZendeskService>();
    private readonly IPortalDbContext _dbContext = Substitute.For<IPortalDbContext>();
    private readonly IZendeskTicketFieldService _ticketFieldService = Substitute.For<IZendeskTicketFieldService>();
    private readonly SchoolCheckingExerciseSettings _settings = new()
    {
        TargetViewTitle = "School Checking Exercise",
        BrandId = 1234,
        GroupId = 5678,
    };

    private ZendeskConsumer NewConsumer() =>
        new(_queueService, _zendesk, _dbContext, _ticketFieldService, _settings);

    [Fact]
    public void Approved_ProducesTaskTicket_WithNormalPriority()
    {
        var consumer = NewConsumer();
        var decision = new Decision(DecisionStatus.AutoApproved, "Deceased", "DEC-1", new[] { "always true" });

        var ticket = consumer.BuildTicket(NewMessage("Deceased"), decision).Ticket;

        Assert.Equal("task", ticket.Type);
        Assert.Equal("normal", ticket.Priority);
        Assert.Equal("open", ticket.Status);
        Assert.Contains("Auto-Approved", ticket.Subject);
        Assert.Contains("Deceased", ticket.Subject);
        Assert.Equal(1234, ticket.BrandId);
        Assert.Equal(5678, ticket.GroupId);
    }

    [Fact]
    public void Rejected_ProducesTaskTicket_WithNormalPriority()
    {
        var consumer = NewConsumer();
        var decision = new Decision(DecisionStatus.AutoRejected, "ElectiveHomeEducation", "EHE-KS4",
            new[] { "keyStage == \"KS4\" → true", "dateOfRemoval > 2025-01-16 → true" });

        var ticket = consumer.BuildTicket(NewMessage("Elective home education"), decision).Ticket;

        Assert.Equal("task", ticket.Type);
        Assert.Equal("normal", ticket.Priority);
        Assert.Contains("Auto-Rejected", ticket.Subject);
        Assert.Contains("ElectiveHomeEducation", ticket.Subject);
    }

    [Fact]
    public void Scrutiny_ProducesHighPriorityQuestionTicket()
    {
        var consumer = NewConsumer();
        var decision = new Decision(DecisionStatus.Scrutiny, "YearGroupChange", "YGC-DEF",
            new[] { "otherwise → true" });

        var ticket = consumer.BuildTicket(NewMessage("Year group change"), decision).Ticket;

        Assert.Equal("question", ticket.Type);
        Assert.Equal("high", ticket.Priority);
        Assert.Equal("new", ticket.Status);
        Assert.Contains("Requires Scrutiny", ticket.Subject);
    }

    [Fact]
    public void Description_IncludesOutcomeRuleAndAnswers()
    {
        var consumer = NewConsumer();
        var msg = NewMessage("Deceased", Answer("Reason for removal", "Death certificate provided."));
        var decision = new Decision(DecisionStatus.AutoApproved, "Deceased", "DEC-1", Array.Empty<string>());

        var ticket = consumer.BuildTicket(msg, decision).Ticket;

        Assert.Contains("Outcome: Deceased", ticket.Description);
        Assert.Contains("Decision: AutoApproved", ticket.Description);
        Assert.Contains("Matched rule: DEC-1", ticket.Description);
        Assert.Contains("Reason for removal: Death certificate provided.", ticket.Description);
    }

    // The evaluation trace is admin-only: it is persisted on the change request and shown on
    // the admin requests page (admin/windows/{id}/requests). DeriveDecision passes an empty trace, but this pins the
    // stronger invariant — even handed a populated Decision, BuildTicket must not leak the
    // trace (or the pupil field values its leaf lines quote) into a Zendesk ticket.
    [Fact]
    public void Description_ExcludesTrace_EvenWhenTheDecisionCarriesOne()
    {
        var consumer = NewConsumer();
        var msg = NewMessage("Deceased", Answer("Reason for removal", "Death certificate provided."));
        var decision = new Decision(DecisionStatus.AutoApproved, "Deceased", "DEC-1",
            new[] { "dateOfDeath >= 2026-01-01 → true (got 2026-03-14)", "second trace line" });

        var ticket = consumer.BuildTicket(msg, decision).Ticket;

        Assert.DoesNotContain("Trace:", ticket.Description);
        Assert.DoesNotContain("dateOfDeath", ticket.Description);
        Assert.DoesNotContain("2026-03-14", ticket.Description);
        Assert.DoesNotContain("second trace line", ticket.Description);

        // The rest of the description is unaffected.
        Assert.Contains("Matched rule: DEC-1", ticket.Description);
        Assert.Contains("Reason for removal: Death certificate provided.", ticket.Description);
    }

    [Fact]
    public void CustomFields_OmittedWhenIdsUnset()
    {
        var consumer = NewConsumer(); // engine custom-field IDs all 0
        var decision = new Decision(DecisionStatus.AutoApproved, "Deceased", "DEC-1", Array.Empty<string>());

        var ticket = consumer.BuildTicket(NewMessage("Deceased"), decision).Ticket;

        Assert.DoesNotContain(ticket.CustomFields ?? new(), f => (string)f.Value! == "AutoApproved");
    }

    [Fact]
    public void PupilFields_AreMappedFromConfiguredFieldIds()
    {
        _ticketFieldService.GetFieldIdFromConfig(ZendeskTicketFieldConstants.SchoolUrnName).Returns(10L);
        _ticketFieldService.GetFieldIdFromConfig(ZendeskTicketFieldConstants.CypmdName).Returns(11L);
        _ticketFieldService.GetFieldIdFromConfig(ZendeskTicketFieldConstants.UpnName).Returns(12L);
        _ticketFieldService.GetFieldIdFromConfig(ZendeskTicketFieldConstants.SurnameCypmdName).Returns(13L);
        _ticketFieldService.GetFieldIdFromConfig(ZendeskTicketFieldConstants.ForenameCypmdName).Returns(14L);
        _ticketFieldService.GetFieldIdFromConfig(ZendeskTicketFieldConstants.DateOfBirthCypmdName).Returns(15L);

        var consumer = NewConsumer();
        var decision = new Decision(DecisionStatus.Scrutiny, "Other", "OTH-DEF", Array.Empty<string>());

        var ticket = consumer.BuildTicket(NewMessage("Other"), decision).Ticket;

        Assert.Contains(ticket.CustomFields!, f => f.Id == 10 && (string)f.Value! == "123456");
        Assert.Contains(ticket.CustomFields!, f => f.Id == 11 && (string)f.Value! == "1001");
        // UPN comes from the pupil's Upn (upper-cased), not the internal pupil Id.
        Assert.Contains(ticket.CustomFields!, f => f.Id == 12 && (string)f.Value! == "UPN1");
        // Surname/forename are upper-cased before mapping.
        Assert.Contains(ticket.CustomFields!, f => f.Id == 13 && (string)f.Value! == "SMITH");
        Assert.Contains(ticket.CustomFields!, f => f.Id == 14 && (string)f.Value! == "BOB");
        // dd/MM/yyyy is normalised to ISO yyyy-MM-dd.
        Assert.Contains(ticket.CustomFields!, f => f.Id == 15 && (string)f.Value! == "2010-01-01");
    }

    // The Zendesk CYPMD_ID field is an integer field: Zendesk rejects the WHOLE ticket (422
    // "CYPMD ID: is invalid") when the value is not an integer. A pupil with a non-numeric id
    // (the UAT seed used "CY0045") must therefore lose that one field, not the ticket.
    [Fact]
    public void CypmdField_IsOmitted_WhenTheIdIsNotAnInteger()
    {
        _ticketFieldService.GetFieldIdFromConfig(ZendeskTicketFieldConstants.CypmdName).Returns(11L);

        var consumer = NewConsumer();
        var decision = new Decision(DecisionStatus.Scrutiny, "Other", "OTH-DEF", Array.Empty<string>());

        var ticket = consumer.BuildTicket(NewMessage("Other", "KS4", cypmdId: "CY0045"), decision).Ticket;

        Assert.DoesNotContain(ticket.CustomFields!, f => f.Id == 11);
    }

    [Fact]
    public void CypmdField_IsOmitted_WhenTheIdIsEmpty()
    {
        _ticketFieldService.GetFieldIdFromConfig(ZendeskTicketFieldConstants.CypmdName).Returns(11L);

        var consumer = NewConsumer();
        var decision = new Decision(DecisionStatus.Scrutiny, "Other", "OTH-DEF", Array.Empty<string>());

        var ticket = consumer.BuildTicket(NewMessage("Other", "KS4", cypmdId: ""), decision).Ticket;

        Assert.DoesNotContain(ticket.CustomFields!, f => f.Id == 11);
    }

    [Fact]
    public void UpnField_IsUpperCased()
    {
        _ticketFieldService.GetFieldIdFromConfig(ZendeskTicketFieldConstants.UpnName).Returns(12L);

        var consumer = NewConsumer();
        var decision = new Decision(DecisionStatus.Scrutiny, "Other", "OTH-DEF", Array.Empty<string>());

        var ticket = consumer.BuildTicket(NewMessage("Other", "KS4", upn: "upn1"), decision).Ticket;

        Assert.Contains(ticket.CustomFields!, f => f.Id == 12 && (string)f.Value! == "UPN1");
    }

    // --- US1: message-derived fields (FR-002..006, FR-009, FR-010, FR-011) ---

    [Fact]
    public void RemoveTicket_PopulatesMessageDerivedFields()
    {
        _ticketFieldService.GetFieldIdFromConfig(ZendeskTicketFieldConstants.DciRefCypmdName).Returns(20L);
        _ticketFieldService.GetFieldIdFromConfig(ZendeskTicketFieldConstants.AgeCypmdName).Returns(21L);
        _ticketFieldService.GetFieldIdFromConfig(ZendeskTicketFieldConstants.CycleYearName).Returns(22L);
        _ticketFieldService.GetFieldIdFromConfig(ZendeskTicketFieldConstants.CycleMonthName).Returns(23L);
        _ticketFieldService.GetFieldIdFromConfig(ZendeskTicketFieldConstants.KeyStageName).Returns(24L);
        _ticketFieldService.GetFieldIdFromConfig(ZendeskTicketFieldConstants.CorrectionTypeName).Returns(25L);
        _ticketFieldService.GetFieldIdFromConfig(ZendeskTicketFieldConstants.CorrectionReason31Name).Returns(26L);
        _ticketFieldService.GetFieldIdFromConfig(ZendeskTicketFieldConstants.ReasonForRemovalName).Returns(27L);
        _ticketFieldService.GetOptionValue(ZendeskTicketFieldConstants.CorrectionReason31Name, "pupil-died").Returns("4_31");
        _ticketFieldService.GetOptionValue(ZendeskTicketFieldConstants.ReasonForRemovalName, "pupil-died").Returns("deceased");
        _ticketFieldService.GetOptionValue(ZendeskTicketFieldConstants.KeyStageName, "KS2").Returns("ks2");

        var consumer = NewConsumer();
        var decision = new Decision(DecisionStatus.AutoApproved, "Deceased", "DEC-1", Array.Empty<string>());
        var msg = NewMessage("Remove - pupil-died", "KS2");

        var ticket = consumer.BuildTicket(msg, decision).Ticket;

        Assert.Contains(ticket.CustomFields!, f => f.Id == 20 && (string)f.Value! == "REF");
        Assert.Contains(ticket.CustomFields!, f => f.Id == 21 && (string)f.Value! == "14");
        Assert.Contains(ticket.CustomFields!, f => f.Id == 22 && (string)f.Value! == msg.SubmittedAt.Year.ToString(CultureInfo.InvariantCulture));
        Assert.Contains(ticket.CustomFields!, f => f.Id == 23 && (string)f.Value! == msg.SubmittedAt.Month.ToString(CultureInfo.InvariantCulture));
        Assert.Contains(ticket.CustomFields!, f => f.Id == 24 && (string)f.Value! == "ks2");
        Assert.Contains(ticket.CustomFields!, f => f.Id == 25 && (string)f.Value! == "31_");
        Assert.Contains(ticket.CustomFields!, f => f.Id == 26 && (string)f.Value! == "4_31");
        Assert.Contains(ticket.CustomFields!, f => f.Id == 27 && (string)f.Value! == "deceased");
    }

    [Fact]
    public void RemoveTicket_PopulatesCorrectionReason_ForYearGroupChange()
    {
        _ticketFieldService.GetFieldIdFromConfig(ZendeskTicketFieldConstants.CorrectionTypeName).Returns(25L);
        _ticketFieldService.GetFieldIdFromConfig(ZendeskTicketFieldConstants.CorrectionReason31Name).Returns(26L);
        _ticketFieldService.GetOptionValue(ZendeskTicketFieldConstants.CorrectionReason31Name, "year-group-change").Returns("17_31");

        var consumer = NewConsumer();
        var decision = new Decision(DecisionStatus.AutoApproved, "YearGroupChange", "YGC-1", Array.Empty<string>());

        var ticket = consumer.BuildTicket(NewMessage("Remove - year-group-change", "KS4June"), decision).Ticket;

        Assert.Contains(ticket.CustomFields!, f => f.Id == 25 && (string)f.Value! == "31_");
        Assert.Contains(ticket.CustomFields!, f => f.Id == 26 && (string)f.Value! == "17_31");
    }

    [Fact]
    public void RemoveTicket_PopulatesKeyStage_ForKS4JuneWindow()
    {
        _ticketFieldService.GetFieldIdFromConfig(ZendeskTicketFieldConstants.KeyStageName).Returns(24L);
        _ticketFieldService.GetOptionValue(ZendeskTicketFieldConstants.KeyStageName, "KS4June").Returns("ks4");

        var consumer = NewConsumer();
        var decision = new Decision(DecisionStatus.AutoApproved, "Deceased", "DEC-1", Array.Empty<string>());

        var ticket = consumer.BuildTicket(NewMessage("Remove - pupil-died", "KS4June"), decision).Ticket;

        Assert.Contains(ticket.CustomFields!, f => f.Id == 24 && (string)f.Value! == "ks4");
    }

    [Fact]
    public void IncludeTicket_PopulatesMessageDerivedFields_ButOmitsCorrectionFields()
    {
        _ticketFieldService.GetFieldIdFromConfig(ZendeskTicketFieldConstants.CorrectionTypeName).Returns(25L);
        _ticketFieldService.GetFieldIdFromConfig(ZendeskTicketFieldConstants.CorrectionReason31Name).Returns(26L);
        _ticketFieldService.GetFieldIdFromConfig(ZendeskTicketFieldConstants.ReasonForRemovalName).Returns(27L);

        var consumer = NewConsumer();
        var decision = new Decision(DecisionStatus.AutoApproved, "Inclusion", "INC-1", Array.Empty<string>());

        var ticket = consumer.BuildTicket(NewMessage("Include"), decision).Ticket;

        Assert.DoesNotContain(ticket.CustomFields ?? new(), f => f.Id == 25);
        Assert.DoesNotContain(ticket.CustomFields ?? new(), f => f.Id == 26);
        Assert.DoesNotContain(ticket.CustomFields ?? new(), f => f.Id == 27);
    }

    [Fact]
    public void RemoveTicket_OmitsCorrectionReason_WhenRemovalReasonUnmapped_AndCreationSucceeds()
    {
        _ticketFieldService.GetFieldIdFromConfig(ZendeskTicketFieldConstants.CorrectionTypeName).Returns(25L);
        _ticketFieldService.GetFieldIdFromConfig(ZendeskTicketFieldConstants.CorrectionReason31Name).Returns(26L);
        _ticketFieldService.GetFieldIdFromConfig(ZendeskTicketFieldConstants.ReasonForRemovalName).Returns(27L);
        // "Remove - other" maps to no correction-reason code (FR-014 edge case). Its
        // reason-for-removal value is answer-aware (FR-004): this fixture sends no evidence
        // answer at all, so the documented safe-triage default applies (FR-004: no evidence
        // answer -> _with_evidence).

        var consumer = NewConsumer();
        var decision = new Decision(DecisionStatus.AutoApproved, "Other", "OTH-1", Array.Empty<string>());

        var ticket = consumer.BuildTicket(NewMessage("Remove - other"), decision).Ticket;

        Assert.Contains(ticket.CustomFields!, f => f.Id == 25 && (string)f.Value! == "31_");
        Assert.DoesNotContain(ticket.CustomFields!, f => f.Id == 26);
        Assert.Contains(ticket.CustomFields!, f => f.Id == 27 && (string)f.Value! == "other_-_with_evidence");
    }

    [Fact]
    public void MessageDerivedFields_OmittedWhenSourceAbsent_AndCreationSucceeds()
    {
        _ticketFieldService.GetFieldIdFromConfig(ZendeskTicketFieldConstants.KeyStageName).Returns(24L);
        _ticketFieldService.GetFieldIdFromConfig(ZendeskTicketFieldConstants.CorrectionTypeName).Returns(25L);
        _ticketFieldService.GetFieldIdFromConfig(ZendeskTicketFieldConstants.CorrectionReason31Name).Returns(26L);
        // KS4 window has no key-stage option (only ks1-ks3 confirmed) -> omitted (FR-006/FR-014).
        // "Remove - other" has no correction-reason mapping -> omitted.

        var consumer = NewConsumer();
        var decision = new Decision(DecisionStatus.AutoApproved, "Other", "OTH-1", Array.Empty<string>());

        var ticket = consumer.BuildTicket(NewMessage("Remove - other"), decision).Ticket;

        Assert.DoesNotContain(ticket.CustomFields ?? new(), f => f.Id == 24);
        Assert.DoesNotContain(ticket.CustomFields ?? new(), f => f.Id == 26);
        Assert.Contains(ticket.CustomFields!, f => f.Id == 25 && (string)f.Value! == "31_");
    }

    // --- US2: LDS matched pupil ID + DfE Establishment Number (FR-007, FR-008) ---

    [Fact]
    public void LdsMatchedPupilIdAndDfeEstablishmentNumber_AreMappedFromConfiguredFieldIds()
    {
        _ticketFieldService.GetFieldIdFromConfig(ZendeskTicketFieldConstants.LdsMatchedPupilIdName).Returns(30L);
        _ticketFieldService.GetFieldIdFromConfig(ZendeskTicketFieldConstants.DfeEstablishmentNumberName).Returns(31L);

        var consumer = NewConsumer();
        var decision = new Decision(DecisionStatus.AutoApproved, "Deceased", "DEC-1", Array.Empty<string>());
        var msg = NewMessage("Remove - pupil-died", "KS4", laestab: "860/4070", matchRef: 10042);

        var ticket = consumer.BuildTicket(msg, decision).Ticket;

        Assert.Contains(ticket.CustomFields!, f => f.Id == 30 && (string)f.Value! == "10042");
        Assert.Contains(ticket.CustomFields!, f => f.Id == 31 && (string)f.Value! == "860/4070");
    }

    [Fact]
    public void LdsMatchedPupilIdAndDfeEstablishmentNumber_AreOmittedWhenAbsent()
    {
        _ticketFieldService.GetFieldIdFromConfig(ZendeskTicketFieldConstants.LdsMatchedPupilIdName).Returns(30L);
        _ticketFieldService.GetFieldIdFromConfig(ZendeskTicketFieldConstants.DfeEstablishmentNumberName).Returns(31L);

        var consumer = NewConsumer();
        var decision = new Decision(DecisionStatus.AutoApproved, "Deceased", "DEC-1", Array.Empty<string>());

        var ticket = consumer.BuildTicket(NewMessage("Remove - pupil-died"), decision).Ticket;

        Assert.DoesNotContain(ticket.CustomFields ?? new(), f => f.Id == 30);
        Assert.DoesNotContain(ticket.CustomFields ?? new(), f => f.Id == 31);
    }

    // --- US3: admission date + decision reason (FR-012, FR-013) ---

    [Fact]
    public void AdmissionDate_IsMappedFromPupilEntryDate()
    {
        _ticketFieldService.GetFieldIdFromConfig(ZendeskTicketFieldConstants.AdmissionDateName).Returns(40L);

        var consumer = NewConsumer();
        var decision = new Decision(DecisionStatus.AutoApproved, "Deceased", "DEC-1", Array.Empty<string>());
        var msg = NewMessage("Remove - pupil-died", "KS2", entryDate: "01/09/2024");

        var ticket = consumer.BuildTicket(msg, decision).Ticket;

        // dd/MM/yyyy from the pupil record is normalised to ISO yyyy-MM-dd.
        Assert.Contains(ticket.CustomFields!, f => f.Id == 40 && (string)f.Value! == "2024-09-01");
    }

    [Fact]
    public void AdmissionDate_IsOmitted_WhenEntryDateAbsent()
    {
        _ticketFieldService.GetFieldIdFromConfig(ZendeskTicketFieldConstants.AdmissionDateName).Returns(40L);

        var consumer = NewConsumer();
        var decision = new Decision(DecisionStatus.AutoApproved, "Deceased", "DEC-1", Array.Empty<string>());

        var ticket = consumer.BuildTicket(NewMessage("Remove - pupil-died"), decision).Ticket;

        Assert.DoesNotContain(ticket.CustomFields ?? new(), f => f.Id == 40);
    }

    [Fact]
    public void AdmissionDate_IsOmitted_WhenEntryDateUnparseable()
    {
        _ticketFieldService.GetFieldIdFromConfig(ZendeskTicketFieldConstants.AdmissionDateName).Returns(40L);

        var consumer = NewConsumer();
        var decision = new Decision(DecisionStatus.AutoApproved, "Deceased", "DEC-1", Array.Empty<string>());
        // A supplier ENTRYDAT that matches no accepted format must never be sent raw to
        // Zendesk (it would 422 the whole ticket); the field is omitted instead.
        var msg = NewMessage("Remove - pupil-died", "KS2", entryDate: "01-09-2024");

        var ticket = consumer.BuildTicket(msg, decision).Ticket;

        Assert.DoesNotContain(ticket.CustomFields ?? new(), f => f.Id == 40);
    }

    [Fact]
    public void AllCustomFields_Omitted_WhenConfiguredFieldIdIsZero()
    {
        // production.yml sets every ZendeskTicketFields__*Id to 0. A configured 0 must be
        // treated as "not configured" (FR-014); sending Id=0 to Zendesk 422s the whole ticket.
        _ticketFieldService.GetFieldIdFromConfig(ZendeskTicketFieldConstants.DciRefCypmdName).Returns(0L);
        _ticketFieldService.GetFieldIdFromConfig(ZendeskTicketFieldConstants.AgeCypmdName).Returns(0L);
        _ticketFieldService.GetFieldIdFromConfig(ZendeskTicketFieldConstants.CycleYearName).Returns(0L);
        _ticketFieldService.GetFieldIdFromConfig(ZendeskTicketFieldConstants.CycleMonthName).Returns(0L);
        _ticketFieldService.GetFieldIdFromConfig(ZendeskTicketFieldConstants.KeyStageName).Returns(0L);
        _ticketFieldService.GetFieldIdFromConfig(ZendeskTicketFieldConstants.CorrectionTypeName).Returns(0L);
        _ticketFieldService.GetFieldIdFromConfig(ZendeskTicketFieldConstants.CorrectionReason31Name).Returns(0L);
        _ticketFieldService.GetFieldIdFromConfig(ZendeskTicketFieldConstants.ReasonForRemovalName).Returns(0L);
        _ticketFieldService.GetFieldIdFromConfig(ZendeskTicketFieldConstants.LdsMatchedPupilIdName).Returns(0L);
        _ticketFieldService.GetFieldIdFromConfig(ZendeskTicketFieldConstants.DfeEstablishmentNumberName).Returns(0L);
        _ticketFieldService.GetFieldIdFromConfig(ZendeskTicketFieldConstants.AdmissionDateName).Returns(0L);
        _ticketFieldService.GetFieldIdFromConfig(ZendeskTicketFieldConstants.DecisionReasonApprovedName).Returns(0L);
        _ticketFieldService.GetFieldIdFromConfig(ZendeskTicketFieldConstants.DecisionReasonRejectedName).Returns(0L);
        _ticketFieldService.GetOptionValue(ZendeskTicketFieldConstants.CorrectionReason31Name, "pupil-died").Returns("4_31");
        _ticketFieldService.GetOptionValue(ZendeskTicketFieldConstants.ReasonForRemovalName, "pupil-died").Returns("deceased");
        _ticketFieldService.GetOptionValue(ZendeskTicketFieldConstants.KeyStageName, "KS4June").Returns("ks4");

        var consumer = NewConsumer();
        var decision = new Decision(DecisionStatus.AutoApproved, "Deceased", "DEC-1", Array.Empty<string>());
        var msg = NewMessage("Remove - pupil-died", "KS4June", laestab: "860/4070", matchRef: 10042, entryDate: "01/09/2024");

        var ticket = consumer.BuildTicket(msg, decision).Ticket;

        Assert.DoesNotContain(ticket.CustomFields ?? new(), f => f.Id == 0);
    }

    [Fact]
    public void DecisionReasonApproved_IsMappedFromOutcomeKey()
    {
        _ticketFieldService.GetFieldIdFromConfig(ZendeskTicketFieldConstants.DecisionReasonApprovedName).Returns(50L);
        _ticketFieldService.GetOptionValue(ZendeskTicketFieldConstants.DecisionReasonApprovedName, "Deceased").Returns("deceased_criteria_met");

        var consumer = NewConsumer();
        var decision = new Decision(DecisionStatus.AutoApproved, "Deceased", "DEC-1", Array.Empty<string>());

        var ticket = consumer.BuildTicket(NewMessage("Remove - pupil-died"), decision).Ticket;

        Assert.Contains(ticket.CustomFields!, f => f.Id == 50 && (string)f.Value! == "deceased_criteria_met");
    }

    [Fact]
    public void DecisionReasonApproved_IsOmitted_WhenOutcomeUnmapped()
    {
        _ticketFieldService.GetFieldIdFromConfig(ZendeskTicketFieldConstants.DecisionReasonApprovedName).Returns(50L);

        var consumer = NewConsumer();
        var decision = new Decision(DecisionStatus.AutoApproved, "Other", "OTH-1", Array.Empty<string>());

        var ticket = consumer.BuildTicket(NewMessage("Remove - other"), decision).Ticket;

        Assert.DoesNotContain(ticket.CustomFields ?? new(), f => f.Id == 50);
    }

    [Fact]
    public void DecisionReasonApproved_IsMappedForAnyAutoApprovedOutcomeKey()
    {
        _ticketFieldService.GetFieldIdFromConfig(ZendeskTicketFieldConstants.DecisionReasonApprovedName).Returns(50L);
        _ticketFieldService.GetOptionValue(ZendeskTicketFieldConstants.DecisionReasonApprovedName, "NotOnRoll").Returns("not_on_roll_criteria_met");

        var consumer = NewConsumer();
        var decision = new Decision(DecisionStatus.AutoApproved, "NotOnRoll", "NOR-NONPOST16", Array.Empty<string>());

        var ticket = consumer.BuildTicket(NewMessage("Remove - not-on-roll"), decision).Ticket;

        Assert.Contains(ticket.CustomFields!, f => f.Id == 50 && (string)f.Value! == "not_on_roll_criteria_met");
    }

    [Fact]
    public void DecisionReasonApproved_IsOmitted_WhenDecisionNotAutoApproved()
    {
        _ticketFieldService.GetFieldIdFromConfig(ZendeskTicketFieldConstants.DecisionReasonApprovedName).Returns(50L);
        _ticketFieldService.GetOptionValue(ZendeskTicketFieldConstants.DecisionReasonApprovedName, "Deceased").Returns("deceased_criteria_met");

        var consumer = NewConsumer();
        var decision = new Decision(DecisionStatus.Scrutiny, "Deceased", "DEC-1", Array.Empty<string>());

        var ticket = consumer.BuildTicket(NewMessage("Remove - pupil-died"), decision).Ticket;

        Assert.DoesNotContain(ticket.CustomFields ?? new(), f => f.Id == 50);
    }

    [Fact]
    public void DecisionReasonApproved_IsOmitted_WhenAutoRejectedAndFlagDisabled()
    {
        _ticketFieldService.GetFieldIdFromConfig(ZendeskTicketFieldConstants.DecisionReasonApprovedName).Returns(50L);
        _ticketFieldService.GetFieldIdFromConfig(ZendeskTicketFieldConstants.DecisionReasonRejectedName).Returns(51L);
        _ticketFieldService.GetOptionValue(ZendeskTicketFieldConstants.DecisionReasonApprovedName, "Deceased").Returns("deceased_criteria_met");
        _ticketFieldService.GetOptionValue(ZendeskTicketFieldConstants.DecisionReasonRejectedName, "Deceased").Returns("deceased_criteria_not_met");

        var consumer = NewConsumer();
        var decision = new Decision(DecisionStatus.AutoRejected, "Deceased", "DEC-1", Array.Empty<string>());

        var ticket = consumer.BuildTicket(NewMessage("Remove - pupil-died"), decision).Ticket;

        Assert.DoesNotContain(ticket.CustomFields ?? new(), f => f.Id == 50);
        Assert.DoesNotContain(ticket.CustomFields ?? new(), f => f.Id == 51);
    }

    [Fact]
    public void DecisionReasonRejected_IsMapped_WhenAutoRejectedAndFlagEnabled()
    {
        _ticketFieldService.GetFieldIdFromConfig(ZendeskTicketFieldConstants.DecisionReasonApprovedName).Returns(50L);
        _ticketFieldService.GetFieldIdFromConfig(ZendeskTicketFieldConstants.DecisionReasonRejectedName).Returns(51L);
        _ticketFieldService.GetOptionValue(ZendeskTicketFieldConstants.DecisionReasonApprovedName, "Deceased").Returns("deceased_criteria_met");
        _ticketFieldService.GetOptionValue(ZendeskTicketFieldConstants.DecisionReasonRejectedName, "Deceased").Returns("deceased_criteria_not_met");
        _ticketFieldService.PopulateDecisionReasonForAutoRejected.Returns(true);

        var consumer = NewConsumer();
        var decision = new Decision(DecisionStatus.AutoRejected, "Deceased", "DEC-1", Array.Empty<string>());

        var ticket = consumer.BuildTicket(NewMessage("Remove - pupil-died"), decision).Ticket;

        Assert.Contains(ticket.CustomFields!, f => f.Id == 51 && (string)f.Value! == "deceased_criteria_not_met");
        Assert.DoesNotContain(ticket.CustomFields ?? new(), f => f.Id == 50);
    }

    [Fact]
    public void DecisionReasonRejected_IsOmitted_WhenAutoApproved()
    {
        _ticketFieldService.GetFieldIdFromConfig(ZendeskTicketFieldConstants.DecisionReasonApprovedName).Returns(50L);
        _ticketFieldService.GetFieldIdFromConfig(ZendeskTicketFieldConstants.DecisionReasonRejectedName).Returns(51L);
        _ticketFieldService.GetOptionValue(ZendeskTicketFieldConstants.DecisionReasonApprovedName, "Deceased").Returns("deceased_criteria_met");
        _ticketFieldService.GetOptionValue(ZendeskTicketFieldConstants.DecisionReasonRejectedName, "Deceased").Returns("deceased_criteria_not_met");
        _ticketFieldService.PopulateDecisionReasonForAutoRejected.Returns(true);

        var consumer = NewConsumer();
        var decision = new Decision(DecisionStatus.AutoApproved, "Deceased", "DEC-1", Array.Empty<string>());

        var ticket = consumer.BuildTicket(NewMessage("Remove - pupil-died"), decision).Ticket;

        Assert.Contains(ticket.CustomFields!, f => f.Id == 50 && (string)f.Value! == "deceased_criteria_met");
        Assert.DoesNotContain(ticket.CustomFields ?? new(), f => f.Id == 51);
    }

    // --- Feature 456: KS4 included Pincl rendered as a description line (US1 / FR-002, FR-003) ---

    [Fact]
    public void Description_IncludesPinclLine_ForKs4AddBackPupil()
    {
        var consumer = NewConsumer();
        // The ticket 90082 shape: a KS4 add-back where the engine fell back to Scrutiny.
        var msg = NewMessage("Remove - completed-ks4-elsewhere", "KS4June", pincl: 403);
        var decision = new Decision(DecisionStatus.Scrutiny, "CompletedKs4Elsewhere", "CKS4-DEF", Array.Empty<string>());

        var ticket = consumer.BuildTicket(msg, decision).Ticket;

        Assert.Contains($"Matched rule: CKS4-DEF{Environment.NewLine}Pincl: 403", ticket.Description);
    }

    public static IEnumerable<object[]> Ks4IncludedPinclCodes =>
        PupilInclusion.Ks4IncludedPinclCodes.Select(code => new object[] { code });

    [Theory]
    [MemberData(nameof(Ks4IncludedPinclCodes))]
    public void Description_IncludesPinclLine_ForEveryKs4IncludedCode(int pinclCode)
    {
        var consumer = NewConsumer();
        var decision = new Decision(DecisionStatus.AutoApproved, "Deceased", "DEC-1", Array.Empty<string>());

        var ticket = consumer.BuildTicket(NewMessage("Remove - pupil-died", "KS4", pincl: pinclCode), decision).Ticket;

        Assert.Contains($"Pincl: {pinclCode}", ticket.Description);
    }

    [Fact]
    public void Description_ExcludesPinclLine_ForNonIncludedPincl()
    {
        var consumer = NewConsumer();
        var decision = new Decision(DecisionStatus.AutoApproved, "Deceased", "DEC-1", Array.Empty<string>());

        foreach (var code in new[] { 402, 404, 0 })
        {
            var ticket = consumer.BuildTicket(NewMessage("Remove - pupil-died", "KS4", pincl: code), decision).Ticket;
            Assert.DoesNotContain("Pincl:", ticket.Description);
        }
    }

    [Fact]
    public void Description_ExcludesPinclLine_ForNonRemoveRequests()
    {
        var consumer = NewConsumer();
        var decision = new Decision(DecisionStatus.AutoApproved, "Deceased", "DEC-1", Array.Empty<string>());

        foreach (var whatToChange in new[] { "Include", "Add - pupil-died", "Results enquiry - Incorrect grade" })
        {
            var ticket = consumer.BuildTicket(NewMessage(whatToChange, "KS4", pincl: 403), decision).Ticket;
            Assert.DoesNotContain("Pincl:", ticket.Description);
        }
    }

    [Fact]
    public void Description_ExcludesPinclLine_ForPost16Window()
    {
        var consumer = NewConsumer();
        var decision = new Decision(DecisionStatus.Scrutiny, "Deceased", "P16-DEF", Array.Empty<string>());

        // A Post16 pupil cannot hold a KS4 included Pincl: Post16 inclusion is ingest-stamped
        // (Post16PupilRecord.Included) and its seeded codes are 501/502/505/506, so the
        // IsKs4Included gate alone excludes Post16 - no window-type check is needed (FR-003).
        foreach (var code in new[] { 501, 0 })
        {
            var ticket = consumer.BuildTicket(NewMessage("Remove - student-died", "Post16", pincl: code), decision).Ticket;
            Assert.DoesNotContain("Pincl:", ticket.Description);
        }
    }

    // The Pincl line is description-only (SC-003): it must never become a custom field. With the
    // engine field IDs unset (all 0, as in production.yml), the eligible add-back request renders
    // "Pincl: 403" in the description while CustomFields stays empty.
    [Fact]
    public void Description_IncludesPinclLine_WithoutAddingACustomField()
    {
        var consumer = NewConsumer();
        var decision = new Decision(DecisionStatus.Scrutiny, "CompletedKs4Elsewhere", "CKS4-DEF", Array.Empty<string>());

        var ticket = consumer.BuildTicket(NewMessage("Remove - completed-ks4-elsewhere", "KS4June", pincl: 403), decision).Ticket;

        Assert.Contains("Pincl: 403", ticket.Description);
        Assert.DoesNotContain(ticket.CustomFields ?? new(), f => (string)f.Value! == "CompletedKs4Elsewhere");
    }

    // --- Feature 457: "Reason for removal" field for all mapped removal reasons (US1 / FR-001) ---

    [Theory]
    [MemberData(nameof(Ks4IncludedPinclCodes))]
    public void ReasonForRemovalField_IsAddBackRemoval_ForEveryKs4IncludedCode(int pinclCode)
    {
        _ticketFieldService.GetFieldIdFromConfig(ZendeskTicketFieldConstants.ReasonForRemovalName).Returns(27L);

        var consumer = NewConsumer();
        // year-group-change maps per-reason today -> proves Pincl wins over the reason (FR-001).
        var decision = new Decision(DecisionStatus.AutoApproved, "YearGroupChange", "YGC-1", Array.Empty<string>());

        var ticket = consumer.BuildTicket(NewMessage("Remove - year-group-change", "KS4June", pincl: pinclCode), decision).Ticket;

        Assert.Contains(ticket.CustomFields!, f => f.Id == 27 && (string)f.Value! == "add_back_removal");
        Assert.Contains($"Pincl: {pinclCode}", ticket.Description);
    }

    [Theory]
    [InlineData("Remove - completed-ks4-elsewhere")]
    [InlineData("Remove - not-on-roll")]
    [InlineData("Remove - other")]
    public void ReasonForRemovalField_IsAddBackRemoval_AcrossMultipleReasons(string requestTypeCode)
    {
        _ticketFieldService.GetFieldIdFromConfig(ZendeskTicketFieldConstants.ReasonForRemovalName).Returns(27L);

        var consumer = NewConsumer();
        var decision = new Decision(DecisionStatus.AutoApproved, "YearGroupChange", "YGC-1", Array.Empty<string>());

        var ticket = consumer.BuildTicket(NewMessage(requestTypeCode, "KS4June", pincl: 403), decision).Ticket;

        Assert.Contains(ticket.CustomFields!, f => f.Id == 27 && (string)f.Value! == "add_back_removal");
        Assert.Contains("Pincl: 403", ticket.Description);
    }

    [Theory]
    [InlineData(402)]
    [InlineData(404)]
    [InlineData(0)]
    public void ReasonForRemovalField_NotAddBack_ForNonIncludedPincl(int pinclCode)
    {
        _ticketFieldService.GetFieldIdFromConfig(ZendeskTicketFieldConstants.ReasonForRemovalName).Returns(27L);

        var consumer = NewConsumer();
        var decision = new Decision(DecisionStatus.AutoApproved, "YearGroupChange", "YGC-1", Array.Empty<string>());

        var ticket = consumer.BuildTicket(NewMessage("Remove - year-group-change", "KS4June", pincl: pinclCode), decision).Ticket;

        Assert.DoesNotContain(ticket.CustomFields ?? new(), f => (string)f.Value! == "add_back_removal");
        Assert.DoesNotContain("Pincl:", ticket.Description);
    }

    [Theory]
    [InlineData(501)]
    [InlineData(0)]
    public void ReasonForRemovalField_NotAddBack_ForPost16(int pinclCode)
    {
        // A Post16 pupil cannot hold a KS4-included Pincl: Post16 inclusion is ingest-stamped
        // (seeded codes 501/502/505/506), so IsKs4Included alone excludes Post16 from US1.
        _ticketFieldService.GetFieldIdFromConfig(ZendeskTicketFieldConstants.ReasonForRemovalName).Returns(27L);

        var consumer = NewConsumer();
        var decision = new Decision(DecisionStatus.AutoApproved, "Deceased", "DEC-1", Array.Empty<string>());

        var ticket = consumer.BuildTicket(NewMessage("Remove - student-died", "Post16", pincl: pinclCode), decision).Ticket;

        Assert.DoesNotContain(ticket.CustomFields ?? new(), f => (string)f.Value! == "add_back_removal");
        Assert.DoesNotContain("Pincl:", ticket.Description);
    }

    // --- Feature 457: "other" evidence variant (US2b / FR-004) ---

    [Theory]
    [InlineData("KS4June")]
    [InlineData("Post16")]
    public void ReasonForRemovalField_Other_WithEvidence(string windowType)
    {
        _ticketFieldService.GetFieldIdFromConfig(ZendeskTicketFieldConstants.ReasonForRemovalName).Returns(27L);

        var consumer = NewConsumer();
        var decision = new Decision(DecisionStatus.AutoApproved, "Other", "OTH-1", Array.Empty<string>());
        var evidence = Answer("evidence", "", files: AnEvidenceFile());

        var ticket = consumer.BuildTicket(NewMessage("Remove - other", windowType, pincl: 0, answers: new[] { evidence }), decision).Ticket;

        Assert.Contains(ticket.CustomFields!, f => f.Id == 27 && (string)f.Value! == "other_-_with_evidence");
    }

    [Theory]
    [InlineData("KS4June")]
    [InlineData("Post16")]
    public void ReasonForRemovalField_Other_EvidenceAnswerButNoFiles(string windowType)
    {
        _ticketFieldService.GetFieldIdFromConfig(ZendeskTicketFieldConstants.ReasonForRemovalName).Returns(27L);

        var consumer = NewConsumer();
        var decision = new Decision(DecisionStatus.AutoApproved, "Other", "OTH-1", Array.Empty<string>());
        var evidence = Answer("evidence", "", files: new List<FileRecord>());

        var ticket = consumer.BuildTicket(NewMessage("Remove - other", windowType, pincl: 0, answers: new[] { evidence }), decision).Ticket;

        Assert.Contains(ticket.CustomFields!, f => f.Id == 27 && (string)f.Value! == "other_-_evidence_not_required");
    }

    [Theory]
    [InlineData("KS4June")]
    [InlineData("Post16")]
    public void ReasonForRemovalField_Other_NoEvidenceAnswer_DefaultsWithEvidence(string windowType)
    {
        _ticketFieldService.GetFieldIdFromConfig(ZendeskTicketFieldConstants.ReasonForRemovalName).Returns(27L);

        var consumer = NewConsumer();
        var decision = new Decision(DecisionStatus.AutoApproved, "Other", "OTH-1", Array.Empty<string>());

        var ticket = consumer.BuildTicket(NewMessage("Remove - other", windowType, pincl: 0), decision).Ticket;

        Assert.Contains(ticket.CustomFields!, f => f.Id == 27 && (string)f.Value! == "other_-_with_evidence");
    }

    // --- Feature 457: Post16 "not-on-roll" sub-options (US2c / FR-003) ---

    [Theory]
    [InlineData("apprentice", "not_on_roll_apprentice")]
    [InlineData("external-candidate", "not_on_roll_external")]
    [InlineData("international-student", "not_on_roll_international_student")]
    public void ReasonForRemovalField_Post16NotOnRoll_SubOptions(string subReason, string expected)
    {
        _ticketFieldService.GetFieldIdFromConfig(ZendeskTicketFieldConstants.ReasonForRemovalName).Returns(27L);

        var consumer = NewConsumer();
        var decision = new Decision(DecisionStatus.AutoApproved, "NotOnRoll", "NOR-1", Array.Empty<string>());
        var subAnswer = Answer("not-on-roll-reason", "A label", rawValue: subReason);

        var ticket = consumer.BuildTicket(
            NewMessage("Remove - not-on-roll", "Post16", pincl: 0, answers: new[] { subAnswer }),
            decision).Ticket;

        Assert.Contains(ticket.CustomFields!, f => f.Id == 27 && (string)f.Value! == expected);
    }

    [Theory]
    [InlineData("other", true, "not_on_roll_other_with_evidence")]
    [InlineData("other", false, "not_on_roll_other_evidence_not_required")]
    public void ReasonForRemovalField_Post16NotOnRoll_OtherSubReason_EvidenceVariant(
        string subReason, bool hasEvidence, string expected)
    {
        _ticketFieldService.GetFieldIdFromConfig(ZendeskTicketFieldConstants.ReasonForRemovalName).Returns(27L);

        var consumer = NewConsumer();
        var decision = new Decision(DecisionStatus.AutoApproved, "NotOnRoll", "NOR-1", Array.Empty<string>());
        var subAnswer = Answer("not-on-roll-reason", "A label", rawValue: subReason);
        // hasEvidence=false -> FileUpload answer present but with no files (vs. no answer at all,
        // which FR-004 defaults to _with_evidence and is covered by its own test below).
        var evidence = hasEvidence ? Answer("evidence", "", files: AnEvidenceFile()) : Answer("evidence", "", files: new List<FileRecord>());
        var ticket = consumer.BuildTicket(
            NewMessage("Remove - not-on-roll", "Post16", pincl: 0, answers: new[] { subAnswer, evidence }),
            decision).Ticket;

        Assert.Contains(ticket.CustomFields!, f => f.Id == 27 && (string)f.Value! == expected);
    }

    // Same evidence default as FR-004: a "not-on-roll-reason: other" answer with NO evidence
    // answer at all on the document -> _with_evidence (safer triage).
    [Fact]
    public void ReasonForRemovalField_Post16NotOnRoll_OtherSubReason_NoEvidenceAnswer_DefaultsWithEvidence()
    {
        _ticketFieldService.GetFieldIdFromConfig(ZendeskTicketFieldConstants.ReasonForRemovalName).Returns(27L);

        var consumer = NewConsumer();
        var decision = new Decision(DecisionStatus.AutoApproved, "NotOnRoll", "NOR-1", Array.Empty<string>());
        var subAnswer = Answer("not-on-roll-reason", "A label", rawValue: "other");

        var ticket = consumer.BuildTicket(
            NewMessage("Remove - not-on-roll", "Post16", pincl: 0, answers: new[] { subAnswer }),
            decision).Ticket;

        Assert.Contains(ticket.CustomFields!, f => f.Id == 27 && (string)f.Value! == "not_on_roll_other_with_evidence");
    }

    [Fact]
    public void ReasonForRemovalField_Post16NotOnRoll_MissingSubAnswer_FallsBackToNotOnRoll()
    {
        _ticketFieldService.GetFieldIdFromConfig(ZendeskTicketFieldConstants.ReasonForRemovalName).Returns(27L);

        var consumer = NewConsumer();
        var decision = new Decision(DecisionStatus.AutoApproved, "NotOnRoll", "NOR-1", Array.Empty<string>());

        var ticket = consumer.BuildTicket(NewMessage("Remove - not-on-roll", "Post16", pincl: 0), decision).Ticket;

        Assert.Contains(ticket.CustomFields!, f => f.Id == 27 && (string)f.Value! == "not_on_roll");
    }

    [Fact]
    public void ReasonForRemovalField_Ks4NotOnRoll_MapsPlain()
    {
        _ticketFieldService.GetFieldIdFromConfig(ZendeskTicketFieldConstants.ReasonForRemovalName).Returns(27L);

        var consumer = NewConsumer();
        var decision = new Decision(DecisionStatus.AutoApproved, "NotOnRoll", "NOR-1", Array.Empty<string>());
        var onSchoolRoll = Answer("on-school-roll", "Yes");

        var ticket = consumer.BuildTicket(
            NewMessage("Remove - not-on-roll", "KS4June", pincl: 0, answers: new[] { onSchoolRoll }),
            decision).Ticket;

        Assert.Contains(ticket.CustomFields!, f => f.Id == 27 && (string)f.Value! == "not_on_roll");
    }

    // --- helpers ---

    private static RequestDocument NewMessage(string whatToChange, params AnswerRecord[] answers) =>
        NewMessage(whatToChange, "KS4", laestab: null, matchRef: 0, entryDate: null, upn: "UPN1", cypmdId: "1001", answers: answers);

    private static RequestDocument NewMessage(string whatToChange, string windowType, string? laestab = null, int matchRef = 0, string? entryDate = null, string? upn = "UPN1", string cypmdId = "1001", int pincl = 0, params AnswerRecord[] answers) => new()
    {
        ReferenceNumber = "REF",
        CheckingWindowId = Guid.NewGuid(),
        CheckingWindowType = windowType,
        ChangeRequestId = Guid.NewGuid(),
        RequestTypeCode = whatToChange,
        SubmittedAt = DateTime.UtcNow,
        SubmittedBy = new UserDetails { UserId = "u", DisplayName = "x" },
        School = new SchoolDetails { Urn = "123456", Name = "Test School", Laestab = laestab ?? string.Empty },
        Pupil = new PupilDetails
        {
            Id = "p1", CypmdId = cypmdId, Firstname = "Bob", Surname = "Smith",
            DateOfBirth = "01/01/2010", Sex = "M", Age = 14, Upn = upn, MatchRef = matchRef,
            EntryDate = entryDate ?? string.Empty, Pincl = pincl,
        },
        Answers = answers.ToList(),
    };

    private static AnswerRecord Answer(string title, string value, string? rawValue = null, List<FileRecord>? files = null) =>
        new()
        {
            QuestionId = title,
            QuestionTitle = title,
            Type = files is null ? "text" : "FileUpload",
            Value = value,
            RawValue = rawValue ?? value,
            Files = files,
        };

    private static List<FileRecord> AnEvidenceFile() =>
        new() { new FileRecord { OriginalFileName = "evidence.pdf", StoredFileName = "s1.pdf", PageCount = 1, FileSizeBytes = 4096 } };
}
