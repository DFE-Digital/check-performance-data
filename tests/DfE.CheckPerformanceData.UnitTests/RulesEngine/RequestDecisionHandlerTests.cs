using Azure.Storage.Blobs;
using DfE.CheckPerformanceData.Application.RequestSubmission;
using DfE.CheckPerformanceData.Application.RulesEngine;
using DfE.CheckPerformanceData.Application.ZendeskClient;
using DfE.CheckPerformanceData.Infrastructure.Services;
using DfE.CheckPerformanceData.Infrastructure.ZendeskClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace DfE.CheckPerformanceData.Application.UnitTests.RulesEngine;

/// <summary>
/// Verifies that <see cref="RequestDecisionHandler"/> composes the correct
/// Zendesk ticket for each <see cref="DecisionStatus"/>. The handler's most
/// important contract: the ticket the human reviewer sees must say what the
/// engine decided and which rule got us there — and carry the school's evidence.
/// </summary>
public sealed class RequestDecisionHandlerTests
{
    private readonly IZendeskService _zendesk = Substitute.For<IZendeskService>();
    private readonly IZendeskAttachmentService _attachments = Substitute.For<IZendeskAttachmentService>();
    private readonly IZendeskTicketFieldService _ticketFieldService = Substitute.For<IZendeskTicketFieldService>();
    private readonly BlobServiceClient _blobServiceClient = Substitute.For<BlobServiceClient>();
    private readonly BlobContainerClient _blobs = Substitute.For<BlobContainerClient>();
    private readonly SchoolCheckingExerciseSettings _settings = new()
    {
        TargetViewTitle = "School Checking Exercise",
        BrandId = 1234,
        GroupId = 5678,
    };

    public RequestDecisionHandlerTests()
    {
        _zendesk.CreateTicketAsync(Arg.Any<CreateTicketRequestDto>())
            .Returns(_ => new CreateTicketResponseDto { Ticket = new TicketDto { Id = 999 } });
        _blobServiceClient.GetBlobContainerClient(Arg.Any<string>()).Returns(_blobs);
    }

    [Fact]
    public async Task Approved_ProducesTaskTicket_WithNormalPriority()
    {
        var sut = NewSut();
        var msg = NewMessage("Deceased");
        var decision = new Decision(DecisionStatus.AutoApproved, "Deceased", "DEC-1",
            new[] { "always true" });

        await sut.HandleAsync(msg, decision, CancellationToken.None);

        await _zendesk.Received(1).CreateTicketAsync(Arg.Is<CreateTicketRequestDto>(t =>
            t.Ticket.Type == "task"
            && t.Ticket.Priority == "normal"
            && t.Ticket.Status == "open"
            && t.Ticket.Subject.Contains("Auto-Approved")
            && t.Ticket.Subject.Contains("Deceased")
            && t.Ticket.BrandId == 1234
            && t.Ticket.GroupId == 5678));
    }

    [Fact]
    public async Task Rejected_ProducesTaskTicket_WithNormalPriority()
    {
        var sut = NewSut();
        var msg = NewMessage("Elective home education");
        var decision = new Decision(DecisionStatus.AutoRejected, "ElectiveHomeEducation", "EHE-KS4",
            new[] { "keyStage == \"KS4\" → true", "dateOfRemoval > 2025-01-16 → true" });

        await sut.HandleAsync(msg, decision, CancellationToken.None);

        await _zendesk.Received(1).CreateTicketAsync(Arg.Is<CreateTicketRequestDto>(t =>
            t.Ticket.Type == "task"
            && t.Ticket.Priority == "normal"
            && t.Ticket.Subject.Contains("Auto-Rejected")
            && t.Ticket.Subject.Contains("ElectiveHomeEducation")));
    }

    [Fact]
    public async Task Scrutiny_ProducesHighPriorityQuestionTicket()
    {
        var sut = NewSut();
        var msg = NewMessage("Year group change");
        var decision = new Decision(DecisionStatus.Scrutiny, "YearGroupChange", "YGC-DEF",
            new[] { "otherwise → true" });

        await sut.HandleAsync(msg, decision, CancellationToken.None);

        await _zendesk.Received(1).CreateTicketAsync(Arg.Is<CreateTicketRequestDto>(t =>
            t.Ticket.Type == "question"
            && t.Ticket.Priority == "high"
            && t.Ticket.Status == "new"
            && t.Ticket.Subject.Contains("Requires Scrutiny")));
    }

    [Fact]
    public async Task Description_IncludesOutcomeRuleTraceAndAnswers()
    {
        var sut = NewSut();
        var msg = NewMessage("Deceased", Answer("Reason for removal", "Death certificate provided."));
        var decision = new Decision(DecisionStatus.AutoApproved, "Deceased", "DEC-1",
            new[] { "first trace line", "second trace line" });

        await sut.HandleAsync(msg, decision, CancellationToken.None);

        await _zendesk.Received(1).CreateTicketAsync(Arg.Is<CreateTicketRequestDto>(t =>
            t.Ticket.Description.Contains("Outcome: Deceased")
            && t.Ticket.Description.Contains("Decision: AutoApproved")
            && t.Ticket.Description.Contains("Matched rule: DEC-1")
            && t.Ticket.Description.Contains("first trace line")
            && t.Ticket.Description.Contains("second trace line")
            && t.Ticket.Description.Contains("Reason for removal: Death certificate provided.")));
    }

    [Fact]
    public async Task CustomFields_OmittedWhenIdsUnset()
    {
        var sut = NewSut(); // settings have all custom-field IDs at 0
        var msg = NewMessage("Deceased");
        var decision = new Decision(DecisionStatus.AutoApproved, "Deceased", "DEC-1", Array.Empty<string>());

        await sut.HandleAsync(msg, decision, CancellationToken.None);

        await _zendesk.Received(1).CreateTicketAsync(Arg.Is<CreateTicketRequestDto>(t =>
            t.Ticket.CustomFields == null || t.Ticket.CustomFields.Count == 0));
    }

    [Fact]
    public async Task CustomFields_PopulatedWhenIdsConfigured()
    {
        _settings.DecisionStatusCustomFieldId = 100;
        _settings.OutcomeKeyCustomFieldId = 200;
        _settings.MatchedRuleIdCustomFieldId = 300;
        var sut = NewSut();
        var msg = NewMessage("Inclusion");
        var decision = new Decision(DecisionStatus.AutoApproved, "Inclusion", "INC-ACC",
            new[] { "inclusionFlag in [...] → true" });

        await sut.HandleAsync(msg, decision, CancellationToken.None);

        await _zendesk.Received(1).CreateTicketAsync(Arg.Is<CreateTicketRequestDto>(t =>
            t.Ticket.CustomFields!.Any(f => f.Id == 100 && (string)f.Value == "AutoApproved")
            && t.Ticket.CustomFields!.Any(f => f.Id == 200 && (string)f.Value == "Inclusion")
            && t.Ticket.CustomFields!.Any(f => f.Id == 300 && (string)f.Value == "INC-ACC")));
    }

    [Fact]
    public async Task Uploads_AreAttached_OnAutoApproved()
    {
        var sut = NewSut();
        var msg = NewMessage("Terminal/Critical illness",
            FileAnswer(storedFileName: Guid.NewGuid().ToString(), originalFileName: "letter.pdf"));
        var decision = new Decision(DecisionStatus.AutoApproved, "TerminalCriticalIllness", "TCI-KS4-REJ",
            Array.Empty<string>());

        StubBlobRead();

        await sut.HandleAsync(msg, decision, CancellationToken.None);

        await _attachments.Received(1).AddAttachmentAsync(
            999, "letter.pdf", Arg.Any<Stream>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Uploads_AreAttached_OnScrutiny()
    {
        var sut = NewSut();
        var msg = NewMessage("Terminal/Critical illness",
            FileAnswer(storedFileName: Guid.NewGuid().ToString(), originalFileName: "letter.pdf"));
        var decision = new Decision(DecisionStatus.Scrutiny, "TerminalCriticalIllness", "TCI-DEF",
            Array.Empty<string>());

        // Scrutiny is exactly when the caseworker needs the uploaded files.
        StubBlobRead();

        await sut.HandleAsync(msg, decision, CancellationToken.None);

        await _attachments.Received(1).AddAttachmentAsync(
            999, "letter.pdf", Arg.Any<Stream>(), Arg.Any<string>());
    }

    [Fact]
    public async Task UploadFailure_IsLogged_DoesNotPreventTicketCreation()
    {
        var sut = NewSut();
        var msg = NewMessage("Elective home education",
            FileAnswer(storedFileName: Guid.NewGuid().ToString(), originalFileName: "evidence.pdf"));
        var decision = new Decision(DecisionStatus.AutoRejected, "ElectiveHomeEducation", "EHE-KS4",
            Array.Empty<string>());

        var blob = Substitute.For<BlobClient>();
        blob.OpenReadAsync(cancellationToken: Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("blob missing"));
        _blobs.GetBlobClient(Arg.Any<string>()).Returns(blob);

        // Should not throw despite the upload failure.
        await sut.HandleAsync(msg, decision, CancellationToken.None);

        await _zendesk.Received(1).CreateTicketAsync(Arg.Any<CreateTicketRequestDto>());
        await _attachments.DidNotReceive().AddAttachmentAsync(
            Arg.Any<long>(), Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<string>());
    }

    [Fact]
    public async Task NullMessage_Throws()
    {
        var sut = NewSut();
        var decision = new Decision(DecisionStatus.Scrutiny, "X", "Y", Array.Empty<string>());

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => sut.HandleAsync(null!, decision, CancellationToken.None));
    }

    [Fact]
    public async Task NullDecision_Throws()
    {
        var sut = NewSut();
        var msg = NewMessage("Deceased");

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => sut.HandleAsync(msg, null!, CancellationToken.None));
    }

    // --- helpers ---

    private RequestDecisionHandler NewSut() =>
        new(_zendesk,
            _attachments,
            _ticketFieldService,
            _blobServiceClient,
            Options.Create(_settings), NullLogger<RequestDecisionHandler>.Instance);

    private void StubBlobRead()
    {
        var blob = Substitute.For<BlobClient>();
        blob.OpenReadAsync(cancellationToken: Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Stream>(new MemoryStream(new byte[] { 1, 2, 3 })));
        _blobs.GetBlobClient(Arg.Any<string>()).Returns(blob);
    }

    private static RequestDocument NewMessage(string whatToChange, params AnswerRecord[] answers) => new()
    {
        ReferenceNumber = "REF",
        CheckingWindowId = Guid.NewGuid(),
        CheckingWindowType = "KS4",
        WhatToChange = whatToChange,
        SubmittedAt = DateTime.UtcNow,
        SubmittedBy = new UserDetails { UserId = "u", DisplayName = "x" },
        School = new SchoolDetails { Urn = "1", Name = "Test School" },
        Pupil = new PupilDetails
        {
            Id = "p", CypmdId = "c", Firstname = "A", Surname = "B",
            DateOfBirth = "01/01/2010", Sex = "F", Age = 14, Upn = "UPN",
        },
        Answers = answers.ToList(),
    };

    private static AnswerRecord Answer(string title, string value) =>
        new() { QuestionId = title, QuestionTitle = title, Type = "text", Value = value };

    private static AnswerRecord FileAnswer(string storedFileName, string originalFileName) => new()
    {
        QuestionId = "evidence",
        QuestionTitle = "Evidence",
        Type = "FileUpload",
        Files = new List<FileRecord>
        {
            new()
            {
                OriginalFileName = originalFileName,
                StoredFileName = storedFileName,
                PageCount = 1,
                FileSizeBytes = 3,
            }
        }
    };
}
