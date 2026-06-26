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
    public void Description_IncludesOutcomeRuleTraceAndAnswers()
    {
        var consumer = NewConsumer();
        var msg = NewMessage("Deceased", Answer("Reason for removal", "Death certificate provided."));
        var decision = new Decision(DecisionStatus.AutoApproved, "Deceased", "DEC-1",
            new[] { "first trace line", "second trace line" });

        var ticket = consumer.BuildTicket(msg, decision).Ticket;

        Assert.Contains("Outcome: Deceased", ticket.Description);
        Assert.Contains("Decision: AutoApproved", ticket.Description);
        Assert.Contains("Matched rule: DEC-1", ticket.Description);
        Assert.Contains("first trace line", ticket.Description);
        Assert.Contains("second trace line", ticket.Description);
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

        Assert.Contains(ticket.CustomFields!, f => f.Id == 10 && (string)f.Value == "123456");
        Assert.Contains(ticket.CustomFields!, f => f.Id == 11 && (string)f.Value == "c1");
        Assert.Contains(ticket.CustomFields!, f => f.Id == 12 && (string)f.Value == "p1");
        // Surname/forename are upper-cased before mapping.
        Assert.Contains(ticket.CustomFields!, f => f.Id == 13 && (string)f.Value == "SMITH");
        Assert.Contains(ticket.CustomFields!, f => f.Id == 14 && (string)f.Value == "BOB");
        // dd/MM/yyyy is normalised to ISO yyyy-MM-dd.
        Assert.Contains(ticket.CustomFields!, f => f.Id == 15 && (string)f.Value == "2010-01-01");
    }

    // --- helpers ---

    private static RequestDocument NewMessage(string whatToChange, params AnswerRecord[] answers) => new()
    {
        ReferenceNumber = "REF",
        CheckingWindowId = Guid.NewGuid(),
        CheckingWindowType = "KS4",
        ChangeRequestId = Guid.NewGuid(),
        RequestTypeCode = whatToChange,
        SubmittedAt = DateTime.UtcNow,
        SubmittedBy = new UserDetails { UserId = "u", DisplayName = "x" },
        School = new SchoolDetails { Urn = "123456", Name = "Test School" },
        Pupil = new PupilDetails
        {
            Id = "p1", CypmdId = "c1", Firstname = "Bob", Surname = "Smith",
            DateOfBirth = "01/01/2010", Sex = "M", Age = 14, Upn = "UPN1",
        },
        Answers = answers.ToList(),
    };

    private static AnswerRecord Answer(string title, string value) =>
        new() { QuestionId = title, QuestionTitle = title, Type = "text", Value = value };
}
