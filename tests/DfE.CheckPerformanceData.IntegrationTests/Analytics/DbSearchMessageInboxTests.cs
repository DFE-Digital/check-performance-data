using DfE.CheckPerformanceData.Application.Analytics;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using DfE.CheckPerformanceData.Persistence.Analytics;
using Npgsql;

namespace DfE.CheckPerformanceData.IntegrationTests.Analytics;

// Contract tests for the admin-inbox extensions on DbSearchMessageService — the read
// surface the messages inbox controller drives (unread badge count, paged list with
// sort + filter, per-message detail, global mark-read stamping, per-session purge, and
// the per-session list the drill-in view renders alongside the session's search history).
[Collection(nameof(PostgresCollection))]
public sealed class DbSearchMessageInboxTests
{
    private readonly PostgresFixture _fixture;

    public DbSearchMessageInboxTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    private DbSearchMessageService CreateSut() => new(_fixture.CreateContext());

    // --- Unread count is COUNT(*) WHERE is_read = false ---

    [Fact]
    public async Task GetUnreadCountAsync_CountsUnreadRowsOnly()
    {
        await SearchAnalyticsSeedHelpers.TruncateAllAsync(_fixture);
        var sut = CreateSut();

        var ids = new List<long>();
        for (var i = 0; i < 25; i++)
        {
            ids.Add(await sut.CreateAsync(
                sessionId: $"s-{i}",
                whatLookingFor: $"looking for thing {i}",
                whatGot: null,
                email: null,
                cancellationToken: CancellationToken.None));
        }

        Assert.Equal(25, await sut.GetUnreadCountAsync(CancellationToken.None));

        // Mark 3 rows as read via the service — mirrors the admin mark-read flow.
        await sut.MarkReadAsync(ids[0], "admin-a", DateTime.UtcNow, CancellationToken.None);
        await sut.MarkReadAsync(ids[1], "admin-a", DateTime.UtcNow, CancellationToken.None);
        await sut.MarkReadAsync(ids[2], "admin-b", DateTime.UtcNow, CancellationToken.None);

        Assert.Equal(22, await sut.GetUnreadCountAsync(CancellationToken.None));
    }

    // --- Paged inbox reader ---

    [Fact]
    public async Task GetInboxAsync_DefaultSort_ReturnsFirstPageDescBySubmittedAt()
    {
        await SearchAnalyticsSeedHelpers.TruncateAllAsync(_fixture);
        var sut = CreateSut();

        for (var i = 0; i < 25; i++)
        {
            await sut.CreateAsync(
                sessionId: $"s-{i}",
                whatLookingFor: $"looking for thing {i}",
                whatGot: null,
                email: null,
                cancellationToken: CancellationToken.None);
            // Small delay to guarantee distinct SubmittedAtUtc values.
            await Task.Delay(2);
        }

        var page = await sut.GetInboxAsync(
            sortField: SearchMessageSortField.SubmittedAt,
            sortDesc: true,
            filterText: null,
            page: 1,
            pageSize: 10,
            cancellationToken: CancellationToken.None);

        Assert.Equal(25, page.TotalCount);
        Assert.Equal(10, page.Rows.Count);
        // Newest first — the most-recently created row (i == 24) tops the list.
        Assert.Equal("s-24", page.Rows[0].SessionId);
        // Rows are ordered by SubmittedAtUtc DESC — each row is not older than its neighbour.
        for (var j = 1; j < page.Rows.Count; j++)
        {
            Assert.True(page.Rows[j - 1].SubmittedAtUtc >= page.Rows[j].SubmittedAtUtc);
        }
    }

    [Fact]
    public async Task GetInboxAsync_FilterText_MatchesOnlyRowsContainingSubstring()
    {
        await SearchAnalyticsSeedHelpers.TruncateAllAsync(_fixture);
        var sut = CreateSut();

        await sut.CreateAsync("s-a", "how do i find a widget", null, null, CancellationToken.None);
        await sut.CreateAsync("s-b", "how do i submit", null, null, CancellationToken.None);
        await sut.CreateAsync("s-c", "widget report broken", null, null, CancellationToken.None);
        await sut.CreateAsync("s-d", "cannot find anything", null, null, CancellationToken.None);

        var page = await sut.GetInboxAsync(
            sortField: SearchMessageSortField.SubmittedAt,
            sortDesc: true,
            filterText: "widget",
            page: 1,
            pageSize: 10,
            cancellationToken: CancellationToken.None);

        Assert.Equal(2, page.TotalCount);
        Assert.Equal(2, page.Rows.Count);
        Assert.All(page.Rows, r => Assert.Contains("widget", r.FirstLinePreview, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetInboxAsync_SortByIsReadAsc_ReturnsUnreadFirst()
    {
        await SearchAnalyticsSeedHelpers.TruncateAllAsync(_fixture);
        var sut = CreateSut();

        var read1 = await sut.CreateAsync("s-r1", "read message 1", null, null, CancellationToken.None);
        await sut.CreateAsync("s-u1", "unread message 1", null, null, CancellationToken.None);
        var read2 = await sut.CreateAsync("s-r2", "read message 2", null, null, CancellationToken.None);
        await sut.CreateAsync("s-u2", "unread message 2", null, null, CancellationToken.None);

        await sut.MarkReadAsync(read1, "admin", DateTime.UtcNow, CancellationToken.None);
        await sut.MarkReadAsync(read2, "admin", DateTime.UtcNow, CancellationToken.None);

        var page = await sut.GetInboxAsync(
            sortField: SearchMessageSortField.IsRead,
            sortDesc: false,
            filterText: null,
            page: 1,
            pageSize: 10,
            cancellationToken: CancellationToken.None);

        Assert.Equal(4, page.TotalCount);
        // Unread rows sort before read ones when direction is asc.
        Assert.False(page.Rows[0].IsRead);
        Assert.False(page.Rows[1].IsRead);
        Assert.True(page.Rows[2].IsRead);
        Assert.True(page.Rows[3].IsRead);
    }

    [Fact]
    public async Task GetInboxAsync_HasEmailFlagReflectsPersistedRow()
    {
        await SearchAnalyticsSeedHelpers.TruncateAllAsync(_fixture);
        var sut = CreateSut();

        await sut.CreateAsync("s-with", "message with email", null, "user@example.com", CancellationToken.None);
        await sut.CreateAsync("s-without", "message without email", null, null, CancellationToken.None);

        var page = await sut.GetInboxAsync(
            sortField: SearchMessageSortField.SubmittedAt,
            sortDesc: true,
            filterText: null,
            page: 1,
            pageSize: 10,
            cancellationToken: CancellationToken.None);

        var withEmail = Assert.Single(page.Rows, r => r.SessionId == "s-with");
        var withoutEmail = Assert.Single(page.Rows, r => r.SessionId == "s-without");
        Assert.True(withEmail.HasEmail);
        Assert.False(withoutEmail.HasEmail);
    }

    [Fact]
    public async Task GetInboxAsync_FirstLinePreview_TruncatedAndNewlinesFlattened()
    {
        await SearchAnalyticsSeedHelpers.TruncateAllAsync(_fixture);
        var sut = CreateSut();

        var body = "the first line goes here\nand then a second line follows\nand a third line as well and even more text here";
        await sut.CreateAsync("s-long", body, null, null, CancellationToken.None);

        var page = await sut.GetInboxAsync(
            sortField: SearchMessageSortField.SubmittedAt,
            sortDesc: true,
            filterText: null,
            page: 1,
            pageSize: 10,
            cancellationToken: CancellationToken.None);

        var row = Assert.Single(page.Rows);
        Assert.True(row.FirstLinePreview.Length <= 80,
            $"Preview must be ≤80 chars; got {row.FirstLinePreview.Length}.");
        Assert.DoesNotContain('\n', row.FirstLinePreview);
    }

    // --- GetByIdAsync ---

    [Fact]
    public async Task GetByIdAsync_ExistingId_ReturnsFullDetail()
    {
        await SearchAnalyticsSeedHelpers.TruncateAllAsync(_fixture);
        var sut = CreateSut();

        var id = await sut.CreateAsync(
            sessionId: "s-detail",
            whatLookingFor: "detail body",
            whatGot: "detail extra",
            email: "u@example.com",
            cancellationToken: CancellationToken.None);

        var detail = await sut.GetByIdAsync(id, CancellationToken.None);

        Assert.NotNull(detail);
        Assert.Equal(id, detail!.Id);
        Assert.Equal("s-detail", detail.SessionId);
        Assert.Equal("detail body", detail.WhatLookingFor);
        Assert.Equal("detail extra", detail.WhatGot);
        Assert.Equal("u@example.com", detail.Email);
        Assert.False(detail.IsRead);
        Assert.Null(detail.ReadByAdminSub);
        Assert.Null(detail.ReadAtUtc);
    }

    [Fact]
    public async Task GetByIdAsync_UnknownId_ReturnsNull()
    {
        await SearchAnalyticsSeedHelpers.TruncateAllAsync(_fixture);
        var sut = CreateSut();

        Assert.Null(await sut.GetByIdAsync(9_999_999L, CancellationToken.None));
    }

    // --- MarkReadAsync ---

    [Fact]
    public async Task MarkReadAsync_StampsIsReadReadByAdminSubAndReadAt()
    {
        await SearchAnalyticsSeedHelpers.TruncateAllAsync(_fixture);
        var sut = CreateSut();

        var id = await sut.CreateAsync("s-1", "body", null, null, CancellationToken.None);
        var readAt = DateTime.UtcNow;
        await sut.MarkReadAsync(id, "admin-a", readAt, CancellationToken.None);

        var detail = await sut.GetByIdAsync(id, CancellationToken.None);
        Assert.NotNull(detail);
        Assert.True(detail!.IsRead);
        Assert.Equal("admin-a", detail.ReadByAdminSub);
        Assert.NotNull(detail.ReadAtUtc);
        Assert.InRange(
            detail.ReadAtUtc!.Value,
            readAt.AddSeconds(-1),
            readAt.AddSeconds(1));
    }

    [Fact]
    public async Task MarkReadAsync_SecondCall_DoesNotOverwriteFirstAdminAttribution()
    {
        await SearchAnalyticsSeedHelpers.TruncateAllAsync(_fixture);
        var sut = CreateSut();

        var id = await sut.CreateAsync("s-1", "body", null, null, CancellationToken.None);
        var firstReadAt = DateTime.UtcNow.AddMinutes(-10);
        await sut.MarkReadAsync(id, "admin-first", firstReadAt, CancellationToken.None);

        // Second admin re-invokes mark-read on the same row — MUST be a no-op update
        // (idempotent global mark-read; the first admin's attribution stays).
        await sut.MarkReadAsync(id, "admin-second", DateTime.UtcNow, CancellationToken.None);

        var detail = await sut.GetByIdAsync(id, CancellationToken.None);
        Assert.NotNull(detail);
        Assert.Equal("admin-first", detail!.ReadByAdminSub);
    }

    // --- PurgeSessionAsync ---

    [Fact]
    public async Task PurgeSessionAsync_DeletesOnlyMessagesForSession_ReturnsCount()
    {
        await SearchAnalyticsSeedHelpers.TruncateAllAsync(_fixture);
        var sut = CreateSut();

        await sut.CreateAsync("session-xyz", "keep 1", null, null, CancellationToken.None);
        await sut.CreateAsync("session-xyz", "keep 2", null, null, CancellationToken.None);
        await sut.CreateAsync("session-xyz", "keep 3", null, null, CancellationToken.None);
        await sut.CreateAsync("other-session", "should survive", null, null, CancellationToken.None);

        var counts = await sut.PurgeSessionAsync("session-xyz", CancellationToken.None);

        Assert.Equal(3, counts.MessagesDeleted);

        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        await using var totalCmd = conn.CreateCommand();
        totalCmd.CommandText = "SELECT COUNT(*) FROM search_messages;";
        Assert.Equal(1L, (long)(await totalCmd.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task PurgeSessionAsync_UnknownSession_ReturnsZero()
    {
        await SearchAnalyticsSeedHelpers.TruncateAllAsync(_fixture);
        var sut = CreateSut();

        var counts = await sut.PurgeSessionAsync("session-does-not-exist", CancellationToken.None);
        Assert.Equal(0, counts.MessagesDeleted);
    }

    // --- GetForSessionAsync ---

    [Fact]
    public async Task GetForSessionAsync_ReturnsSessionMessagesNewestFirst()
    {
        await SearchAnalyticsSeedHelpers.TruncateAllAsync(_fixture);
        var sut = CreateSut();

        await sut.CreateAsync("session-a", "first message from a", null, null, CancellationToken.None);
        await Task.Delay(5);
        await sut.CreateAsync("session-a", "second message from a", null, null, CancellationToken.None);
        await Task.Delay(5);
        await sut.CreateAsync("session-b", "not in the result", null, null, CancellationToken.None);
        await Task.Delay(5);
        await sut.CreateAsync("session-a", "third message from a", null, null, CancellationToken.None);

        var messages = await sut.GetForSessionAsync("session-a", CancellationToken.None);

        Assert.Equal(3, messages.Count);
        Assert.All(messages, m => Assert.Equal("session-a", m.SessionId));
        // Newest first.
        Assert.Contains("third", messages[0].FirstLinePreview);
    }
}
