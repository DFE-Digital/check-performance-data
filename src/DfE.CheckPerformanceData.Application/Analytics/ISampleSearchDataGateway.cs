namespace DfE.CheckPerformanceData.Application.Analytics;

// Write-only gateway used by SampleSearchDataSeeder for the seed-sample-search-data
// admin page. Separate from ISearchMessageService because that surface intentionally
// server-owns SubmittedAtUtc (user-submitted messages must be stamped at receipt) —
// the seeder needs to back-date rows across the chosen time span. Also carries the
// pool of session identifiers so tests can pin a deterministic session mix without
// generating GUIDs themselves.
public interface ISampleSearchDataGateway
{
    Task WriteBackdatedMessagesAsync(
        IReadOnlyList<BackdatedSearchMessage> messages,
        CancellationToken cancellationToken);
}

// Positional record used by the write-gateway. Kept alongside the interface so callers
// see the row shape at the same import.
public sealed record BackdatedSearchMessage(
    string SessionId,
    DateTime SubmittedAtUtc,
    string WhatLookingFor,
    string? WhatGot,
    string? Email);
