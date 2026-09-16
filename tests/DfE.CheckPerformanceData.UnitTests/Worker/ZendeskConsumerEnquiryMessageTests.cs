using System.Linq.Expressions;
using System.Text.Json;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.Queue;
using DfE.CheckPerformanceData.Application.RequestSubmission;
using DfE.CheckPerformanceData.Application.ZendeskClient;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Infrastructure.ZendeskClient;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Entities;
using DfE.CheckPerformanceData.RulesEngineWorker.Consumers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Worker;

/// <summary>
/// Verifies that <see cref="ZendeskConsumer"/> can claim and process a results-enquiry
/// message immediately on submission (AB#301974), and that the claim predicate only widens
/// for enquiry rows — amendment semantics are unchanged (SC-005).
/// </summary>
public sealed class ZendeskConsumerEnquiryMessageTests
{
    private const string Reference = "CYPMD_16to19_RE_4F9C2A1";

    // ── Tests ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Enquiry_row_with_null_WorkerStatus_is_claimed()
    {
        var harness = new ConsumerHarness([NewEnquiryRow(Reference)]);
        harness.StubTicketCreation();

        await harness.Consumer.ProcessMessageBodyAsync(
            Serialize(NewEnquiryMessage(Reference)), CancellationToken.None);

        await harness.Zendesk.Received(1).CreateTicketAsync(Arg.Any<CreateTicketRequestDto>());
    }

    [Fact]
    public async Task Enquiry_ticket_has_Scrutiny_status_and_names_the_QAN()
    {
        var harness = new ConsumerHarness([NewEnquiryRow(Reference)]);
        harness.StubTicketCreation();

        await harness.Consumer.ProcessMessageBodyAsync(
            Serialize(NewEnquiryMessage(Reference)), CancellationToken.None);

        var ticket = harness.CreatedTicket!.Ticket;
        Assert.Equal("new", ticket.Status);
        Assert.Equal("high", ticket.Priority);
        Assert.Equal("question", ticket.Type);
        Assert.Contains("CPMD Requires Scrutiny", ticket.Subject);
        Assert.Contains("Results enquiry - Incorrect grade", ticket.Subject);

        // DeriveDecision uses null Outcome → Scrutiny fallback, and null OutcomeKey → falls
        // back to the message's RequestTypeCode, so the description must carry the message
        // outcome and the synthetic challenged-result answer block (FR-003).
        Assert.Contains("Results enquiry - Incorrect grade", ticket.Description);
        Assert.Contains("Challenged result", ticket.Description);
        Assert.Contains("60180882", ticket.Description); // QAN from the seeded result
    }

    [Fact]
    public async Task Non_enquiry_row_with_null_WorkerStatus_is_not_claimed()
    {
        // SC-005: the widened claim must not accidentally claim an amendment with null status.
        var harness = new ConsumerHarness([NewAmendmentRow(Reference)]);
        harness.StubTicketCreation();

        await harness.Consumer.ProcessMessageBodyAsync(
            Serialize(NewEnquiryMessage(Reference)), CancellationToken.None);

        await harness.Zendesk.DidNotReceive().CreateTicketAsync(Arg.Any<CreateTicketRequestDto>());
    }

    [Fact]
    public async Task Redelivery_with_CrmId_already_set_is_noop()
    {
        // The check-before-create guard must fire regardless of WorkerStatus.
        var harness = new ConsumerHarness([NewEnquiryRow(Reference, crmId: "12345")]);
        harness.StubTicketCreation();

        await harness.Consumer.ProcessMessageBodyAsync(
            Serialize(NewEnquiryMessage(Reference)), CancellationToken.None);

        await harness.Zendesk.DidNotReceive().CreateTicketAsync(Arg.Any<CreateTicketRequestDto>());
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static string Serialize(RequestDocument doc) => JsonSerializer.Serialize(doc);

    private static RequestDocument NewEnquiryMessage(string referenceNumber) => new()
    {
        ChangeRequestId = Guid.NewGuid(),
        ReferenceNumber = referenceNumber,
        SubmittedAt = DateTime.UtcNow,
        SubmittedBy = new UserDetails { UserId = "u", DisplayName = "Ada Editor" },
        CheckingWindowId = Guid.NewGuid(),
        CheckingWindowType = "Post16",
        RequestTypeCode = "Results enquiry - Incorrect grade",
        School = new SchoolDetails { Urn = "142313", Name = "Test School", Laestab = "860/4070" },
        Pupil = new PupilDetails
        {
            Id = "p1", CypmdId = "1596410810", Firstname = "Billy", Surname = "B",
            DateOfBirth = "12/03/2007", Sex = "M", Age = 19, Upn = "UPN1"
        },
        Answers =
        [
            new AnswerRecord
            {
                QuestionId = "challenged-result",
                QuestionTitle = "Challenged result",
                Type = "ResultsEnquiry",
                Value = "GCSE (9-1) Art&Des : Fine Art — QAN 60180882, syllabus 1AD0, session S2024, held grade 9"
            }
        ]
    };

    private static ChangeRequest NewEnquiryRow(string referenceNumber, WorkerStatus? workerStatus = null, string? crmId = null) => new()
    {
        Id = Guid.NewGuid(),
        WindowId = Guid.NewGuid(),
        OrganisationUrn = 142313,
        Submitted = DateTime.UtcNow,
        SubmittedById = Guid.NewGuid(),
        SubmittedByName = "Ada Editor",
        Status = RequestStatus.SubmittedUnCommitted,
        ReferenceNumber = referenceNumber,
        RequestType = RequestType.ResultsEnquiry,
        RequestTypeDescription = "Results enquiry - Incorrect grade",
        AmendmentType = WhatToChange.IncorrectGrade,
        WorkerStatus = workerStatus,
        CrmId = crmId
    };

    private static ChangeRequest NewAmendmentRow(string referenceNumber) => new()
    {
        Id = Guid.NewGuid(),
        WindowId = Guid.NewGuid(),
        OrganisationUrn = 142313,
        Submitted = DateTime.UtcNow,
        SubmittedById = Guid.NewGuid(),
        SubmittedByName = "Ada Editor",
        Status = RequestStatus.SubmittedUnCommitted,
        ReferenceNumber = referenceNumber,
        RequestType = RequestType.Amendment,
        RequestTypeDescription = "Remove - pupil-died",
        AmendmentType = WhatToChange.Remove,
        WorkerStatus = null,
        CrmId = null
    };

    // ── Harness ────────────────────────────────────────────────────────────

    /// <summary>
    /// Wires a fake <see cref="IAsyncQueryProvider"/> over the seeded rows so that:
    ///   • <c>FirstOrDefaultAsync</c> evaluates the real predicate against the in-memory list.
    ///   • <c>ExecuteUpdateAsync</c> returns the count of rows matching the real WHERE clause.
    /// This lets the pre-T007 claim predicate (<c>WorkerStatus == RulesProcessed</c>) return 0
    /// for an enquiry row (red), and post-T007 return 1 (green).
    /// </summary>
    private sealed class ConsumerHarness
    {
        public IQueueService Queue { get; }
        public IZendeskService Zendesk { get; }
        public IPortalDbContext DbContext { get; }
        public ZendeskConsumer Consumer { get; }
        public CreateTicketRequestDto? CreatedTicket { get; private set; }

        public ConsumerHarness(List<ChangeRequest> rows)
        {
            Queue = Substitute.For<IQueueService>();
            Zendesk = Substitute.For<IZendeskService>();
            DbContext = Substitute.For<IPortalDbContext>();

            var dbSet = Substitute.For<DbSet<ChangeRequest>, IQueryable<ChangeRequest>, IAsyncEnumerable<ChangeRequest>>();
            ((IQueryable<ChangeRequest>)dbSet).Provider.Returns(new AsyncQueryProvider(rows));
            ((IQueryable<ChangeRequest>)dbSet).Expression.Returns(rows.AsQueryable().Expression);
            ((IQueryable<ChangeRequest>)dbSet).ElementType.Returns(typeof(ChangeRequest));
            ((IQueryable<ChangeRequest>)dbSet).GetEnumerator().Returns(rows.GetEnumerator());

            DbContext.ChangeRequests.Returns(dbSet);

            // Run the callback synchronously so the claim/CrmId writes reach the fake
            // ExecuteUpdateAsync without needing a real transaction.
            DbContext.ExecuteInTransactionAsync(Arg.Any<Func<Task>>(), Arg.Any<CancellationToken>())
                .Returns(ci => ci.Arg<Func<Task>>().Invoke());

            Consumer = new ZendeskConsumer(
                Queue, Zendesk, DbContext,
                Substitute.For<IZendeskTicketFieldService>(),
                new SchoolCheckingExerciseSettings
                {
                    TargetViewTitle = "School Checking Exercise",
                    BrandId = 1234,
                    GroupId = 5678
                });
        }

        public void StubTicketCreation()
        {
            Zendesk.CreateTicketAsync(Arg.Any<CreateTicketRequestDto>())
                .Returns(ci =>
                {
                    CreatedTicket = ci.Arg<CreateTicketRequestDto>();
                    return Task.FromResult(
                        new CreateTicketResponseDto { Ticket = new TicketDto { Id = 42 } });
                });
        }
    }

    // ── Fake async query provider ──────────────────────────────────────────

    /// <summary>
    /// Implements <see cref="IAsyncQueryProvider"/> over an in-memory list so that:
    ///   • <c>FirstOrDefaultAsync(predicate)</c> runs the predicate against the list.
    ///   • <c>Where(...).ExecuteUpdateAsync(...)</c> counts real-matching rows.
    /// No real EF translation occurs — the tests assert on the fake provider's output,
    /// confirming the claim predicate's branch logic (RulesProcessed vs null-ResultsEnquiry)
    /// without requiring a database.
    /// </summary>
    private sealed class AsyncQueryProvider : IAsyncQueryProvider
    {
        private readonly List<ChangeRequest> _rows;

        public AsyncQueryProvider(List<ChangeRequest> rows) => _rows = rows;

        public IQueryable CreateQuery(Expression expression) =>
            new TestAsyncEnumerable<ChangeRequest>(expression, this);

        public IQueryable<TElement> CreateQuery<TElement>(Expression expression) =>
            new TestAsyncEnumerable<TElement>(expression, this);

        public object? Execute(Expression expression) =>
            throw new NotSupportedException($"Synchronous execution is not supported in this test harness: {expression}");

        public TResult Execute<TResult>(Expression expression) =>
            throw new NotSupportedException($"Synchronous execution is not supported in this test harness: {expression}");

        public TResult ExecuteAsync<TResult>(Expression expression, CancellationToken cancellationToken = default)
        {
            // ExecuteUpdateAsync: returns the count of rows the real WHERE predicate matches.
            if (TryExecuteUpdate(expression, out var affected))
                return (TResult)(object)Task.FromResult(affected);

            // FirstOrDefaultAsync: returns the first match or null from the in-memory list.
            if (expression is MethodCallExpression { Method.Name: "FirstOrDefaultAsync" } call
                && call.Arguments.Count > 1
                && TryGetPredicate(call.Arguments[1], out var firstPredicate))
            {
                var match = _rows.FirstOrDefault(firstPredicate);
                return (TResult)(object)Task.FromResult(match);
            }

            throw new NotSupportedException($"Unhandled async query expression in test harness: {expression}");
        }

        private bool TryExecuteUpdate(Expression expression, out int affected)
        {
            // EF's ExecuteUpdateAsync wraps: Expression.Call(ExecuteUpdate, [sourceExpression, setPropertyCalls]).
            // sourceExpression is the Where(...) call whose predicate we evaluate over the real rows.
            if (expression is MethodCallExpression { Method.Name: "ExecuteUpdate" } call
                && call.Arguments[0] is MethodCallExpression { Method.Name: "Where" } whereCall
                && whereCall.Arguments.Count > 1
                && TryGetPredicate(whereCall.Arguments[1], out var predicate))
            {
                affected = _rows.Count(r => predicate(r));
                return true;
            }

            affected = 0;
            return false;
        }

        private static bool TryGetPredicate(Expression argument, out Func<ChangeRequest, bool> predicate)
        {
            var lambda = argument switch
            {
                UnaryExpression { NodeType: ExpressionType.Quote, Operand: LambdaExpression l } => l,
                LambdaExpression l => l,
                _ => null
            };

            if (lambda is not null)
            {
                predicate = (Func<ChangeRequest, bool>)lambda.Compile();
                return true;
            }

            predicate = null!;
            return false;
        }
    }

    private sealed class TestAsyncEnumerable<T> : IOrderedQueryable<T>, IAsyncEnumerable<T>
    {
        public TestAsyncEnumerable(Expression expression, IQueryProvider provider)
        {
            Expression = expression;
            Provider = provider;
        }

        public Type ElementType => typeof(T);
        public Expression Expression { get; }
        public IQueryProvider Provider { get; }

        public IEnumerator<T> GetEnumerator() =>
            throw new NotSupportedException("Enumeration is not supported in this test harness.");

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Async enumeration is not supported in this test harness.");
    }
}
