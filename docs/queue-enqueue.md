# Enqueuing onto the Rules Engine and Zendesk queues

This document explains how to put a message onto the **rules-engine** queue and the
**zendesk** queue: the one service that does it, the queue names, the payload the
consumers expect, and copy-paste examples for production code, the dev harness, and
HTTP.

---

## Overview

Both queues are rows in a single Postgres table (`queue_messages`), drained with
`FOR UPDATE SKIP LOCKED`. There is no message broker — the queue is a thin service
over the database.

```
                 EnqueueAsync("rules-engine", doc)          EnqueueAsync("zendesk", doc)
 Web app  ─────────────────────────────────────►  ┌──────────────┐
 (RequestService)                                  │ queue_messages│
                                                   │  table        │
 RulesConsumer  ◄──── DequeueAsync("rules-engine") └──────────────┘
   evaluates, writes decision to ChangeRequests
                                                   ZendeskConsumer ◄──── DequeueAsync("zendesk")
                                                     creates the Zendesk ticket
```

Everything goes through one interface — `IQueueService`
(`src/DfE.CheckPerformanceData.Application/Queue/IQueueService.cs`), implemented by
`PostgresQueueService`
(`src/DfE.CheckPerformanceData.Infrastructure/Queue/PostgresQueueService.cs`). You
never write to the table directly.

The two queue names are constants on `QueueOptions`
(`src/DfE.CheckPerformanceData.Application/Queue/QueueOptions.cs`) — always use the
constants, never the literal strings:

| Constant                      | Value          | Drained by       |
| ----------------------------- | -------------- | ---------------- |
| `QueueOptions.RulesEngineQueue` | `"rules-engine"` | `RulesConsumer`  |
| `QueueOptions.ZendeskQueue`     | `"zendesk"`      | `ZendeskConsumer`|

---

## The enqueue call

```csharp
Task<Guid> EnqueueAsync<T>(string queueName, T message, CancellationToken cancellationToken = default);
```

Things to know:

- **Payload serialisation.** A `string` is stored **verbatim**; anything else is
  `JsonSerializer.Serialize`d. So you can pass either a pre-built JSON string or an
  object — both end up as a JSON string in the `payload` column.
  (`PostgresQueueService.EnqueueAsync`, line 39.)
- **Return value.** The new row's `Guid` id, so you can address that exact message
  later (e.g. `DeadLetterAsync(id, reason)`) without dequeuing.
- **Transaction participation.** The insert goes through the caller's
  `IPortalDbContext`, so it joins any ambient `ExecuteInTransactionAsync` — if the
  surrounding transaction rolls back, the message is never enqueued. Enqueue and the
  DB write that justifies it commit or fail together.
- **Visible immediately.** `VisibleAfterUtc` is set to now, so a consumer can claim
  the message on its next poll.

---

## The payload contract

Both consumers parse the body with `RequestDocumentParser.Parse`
(`src/DfE.CheckPerformanceData.Application/RequestSubmission/RequestDocumentParser.cs`)
into a `RequestDocument`. Parsing is **case-insensitive** and accepts enum names as
strings. A body that is not valid JSON, or is missing a `required` field, parses to
`null` and the worker treats it as an unparseable (poison) message.

So whatever you enqueue onto **either** queue must be a `RequestDocument`-shaped JSON
(`src/DfE.CheckPerformanceData.Application/RequestSubmission/RequestDocument.cs`).
Required fields: `ChangeRequestId`, `ReferenceNumber`, `SubmittedBy`,
`CheckingWindowType`, `RequestTypeCode`, `School`, `Pupil`, `Answers`.

Minimal valid body:

```json
{
  "ChangeRequestId": "11111111-2222-3333-4444-555555555555",
  "ReferenceNumber": "REF-EXAMPLE-001",
  "SubmittedAt": "2026-06-24T09:00:00Z",
  "SubmittedBy": { "UserId": "u1", "DisplayName": "Test User" },
  "CheckingWindowId": "11111111-1111-1111-1111-111111111111",
  "CheckingWindowType": "KS4June",
  "RequestTypeCode": "not-on-roll",
  "School": { "Urn": "100000", "Name": "Test School" },
  "Pupil": {
    "Id": "p1", "CypmdId": "c1", "Firstname": "Ann", "Surname": "Bell",
    "DateOfBirth": "01/01/2015", "Sex": "F", "Age": 9, "Upn": "X123", "Pincl": 0
  },
  "Answers": []
}
```

The `Answers` array is what drives rule evaluation; the `RuleContextMapper` reads each
answer's `RawValue` in preference to `Value`. `ChangeRequestId` must point at a real
`ChangeRequests` row — the `RulesConsumer` writes its decision back to that row by
reference number, and the `ZendeskConsumer` reads that row to build the ticket.

---

## Putting an item on the **rules-engine** queue

This is the normal entry point to the pipeline. A message here is picked up by
`RulesConsumer`, evaluated against the current rule set, and its decision
(`Outcome`, `OutcomeKey`, `MatchedRuleId`, `RulesVersion`) written back to the
`ChangeRequests` row.

### Production — on submit

`RequestService.ConfirmRequestAsync` upserts the `ChangeRequests` row, builds the
document and enqueues it
(`src/DfE.CheckPerformanceData.Application/RequestSubmission/RequestService.cs:52`):

```csharp
var changeRequestId = await requestRepository.UpsertAsync(
    BuildChangeRequestData(windowId, journey, RequestStatus.SubmittedUnCommitted, config));
var document = BuildRequestDocument(context, config, changeRequestId);

// Enqueue onto the Postgres rules-engine queue; RulesConsumer picks it up,
// evaluates it, and writes the decision back to the row.
await queueService.EnqueueAsync(QueueOptions.RulesEngineQueue, document);
```

### Your own code

Inject `IQueueService` and call it. Pass an object (it gets JSON-serialised) or a
pre-built JSON string:

```csharp
public sealed class MyProducer(IQueueService queueService)
{
    public async Task EnqueueAsync(RequestDocument document, CancellationToken ct)
    {
        Guid messageId = await queueService.EnqueueAsync(
            QueueOptions.RulesEngineQueue, document, ct);
        // messageId addresses this exact row if you ever need to dead-letter it.
    }
}
```

### Dev harness (code)

`DevPipelineRunner.SubmitAsync` is the shared synthetic-request driver. It creates a
`ChangeRequests` row for a preset outcome, then enqueues the document
(`src/DfE.CheckPerformanceData.Web/Controllers/DevPipelineRunner.cs:75`):

```csharp
var messageBody = BuildMessageJson(reference, preset, changeRequestId); // a JSON string
var messageId = await _queueService.EnqueueAsync(
    QueueOptions.RulesEngineQueue, messageBody, cancellationToken);
```

### Dev harness (HTTP)

These endpoints are gated to non-production with `Dev:ToolsEnabled` and become admin
the easy way via `GET /dev/impersonate/admin`. The Pipeline Dashboard's **Demo** panel
(`/admin/observability`) calls them:

- **Drive a real outcome through the pipeline** — `POST /dev/uat/drive` with
  `outcome=approved|rejected|scrutiny|failure|random` (and an optional `batch` count).
  Runs through `DevPipelineRunner` and enqueues onto the rules-engine queue.
- **Inject a failure** — `POST /dev/queues/inject-failure`: enqueues a synthetic
  message and dead-letters it, so it lands in the DLQ.

---

## Putting an item on the **zendesk** queue

The mechanism is identical — only the queue name changes:

```csharp
await queueService.EnqueueAsync(QueueOptions.ZendeskQueue, document, cancellationToken);
```

A message here is drained by `ZendeskConsumer`
(`src/DfE.CheckPerformanceData.RulesEngineWorker/Consumers/ZendeskConsumer.cs`), which:

1. Loads the `ChangeRequests` row by `ReferenceNumber` and reads the decision the
   rules engine already persisted (`Outcome` / `OutcomeKey` / `MatchedRuleId`); a
   missing decision falls back to **Scrutiny** so a fault never auto-approves.
2. Is **idempotent** — it checks the row's `CrmId` and atomically claims the
   "ticket created" transition before calling Zendesk, so a redelivery or redrive
   never opens a second ticket.
3. Creates the ticket, uploads any evidence attachments, and writes the `CrmId` and
   `Status = ZendeskTicketCreated` back to the row.

Because the consumer reads the decision off the `ChangeRequests` row, a message you
put on the zendesk queue should be the **same `RequestDocument`** for a request the
rules engine has **already decided** (i.e. its row has an `Outcome`). Enqueuing a
document whose row has no decision yet results in a Scrutiny-fallback ticket.

> ⚠️ **There is currently no production code that forwards a message from the
> rules-engine queue onto the zendesk queue.** `RulesConsumer` writes the decision
> back to the `ChangeRequests` row but does **not** re-enqueue onto the zendesk queue
> (its XML doc-comment still says "enqueues a downstream ticket message", but
> `ProcessAsync` only does the decision write-back — the comment is stale). Today the
> zendesk queue is fed only by tests and any code you write. If you are wiring the
> end-to-end pipeline, the forwarding `EnqueueAsync(QueueOptions.ZendeskQueue, …)`
> call is the missing link and belongs at the end of `RulesConsumer.ProcessAsync`
> (inside the same transaction as the decision write-back, so the ticket message and
> the decision commit atomically).

### Example — enqueue + let the consumer create the ticket

```csharp
// After the rules engine has decided this request (its ChangeRequests row has an Outcome):
await queueService.EnqueueAsync(QueueOptions.ZendeskQueue, document, ct);
// ZendeskConsumer claims it on its next poll and creates the ticket.
```

### Example — drive the consumer directly (tests)

The consumer can also be exercised without the queue at all, by handing a body
straight to `ProcessMessageBodyAsync`
(`tests/DfE.CheckPerformanceData.IntegrationTests/Queue/ZendeskConsumerIdempotencyTests.cs`):

```csharp
var consumer = new ZendeskConsumer(queueService, zendeskService, dbContext);
await consumer.ProcessMessageBodyAsync(messageBodyJson, CancellationToken.None);
```

---

## What happens after you enqueue (consumer loop)

Both consumers share `ConsumerBase`
(`src/DfE.CheckPerformanceData.RulesEngineWorker/Consumers/ConsumerBase.cs`):

- Each poll claims **one** visible message for its queue with
  `DequeueAsync(QueueName)`. The claim pushes `VisibleAfterUtc` forward by the
  **visibility timeout** (`QueueOptions.VisibilityTimeout`, default 30s) and
  increments `Attempts`, so a crashed worker's message re-surfaces on its own.
- On success the message is **acked** (deleted) and a metric is recorded.
- On a thrown exception the message is retried; once `Attempts` reaches
  `QueueOptions.MaxAttempts` (default 5) it is **dead-lettered** — copied to the
  `dead_letters` table with a sanitised reason (exception type only, never the
  exception text, which could carry a pupil field) and removed from the queue.
- `Attempts` counts **claims**, not failures — size `VisibilityTimeout` above the
  worst-case processing time so a slow-but-successful message is not re-claimed and
  burned toward the cap.

Dead-letters are visible on the **Dead-letter queue** admin page and can be
**redriven** (re-enqueued onto their source queue, by original id, so a double-redrive
can't duplicate) or **purged**.

---

## Quick reference

| Task | Call |
| ---- | ---- |
| Enqueue to rules engine | `EnqueueAsync(QueueOptions.RulesEngineQueue, doc, ct)` |
| Enqueue to Zendesk | `EnqueueAsync(QueueOptions.ZendeskQueue, doc, ct)` |
| Payload | `RequestDocument`-shaped JSON (string passed verbatim; object auto-serialised) |
| Dead-letter a specific message | `DeadLetterAsync(messageId, reason, ct)` |
| Drive a dev request (HTTP) | `POST /dev/uat/drive?outcome=approved&batch=1` |
| Seed a dead-letter (HTTP) | `GET /dev/queues/seed-dlq` |
| Inject a failure (HTTP) | `POST /dev/queues/inject-failure` |

**Source map**

- Contract: `Application/Queue/IQueueService.cs`
- Implementation: `Infrastructure/Queue/PostgresQueueService.cs`
- Queue names + tuning: `Application/Queue/QueueOptions.cs`
- Payload: `Application/RequestSubmission/RequestDocument.cs` (+ `RequestDocumentParser.cs`)
- Producers: `Application/RequestSubmission/RequestService.cs`, `Web/Controllers/DevPipelineRunner.cs`
- Consumers: `RulesEngineWorker/Consumers/RulesConsumer.cs`, `ZendeskConsumer.cs`, `ConsumerBase.cs`
