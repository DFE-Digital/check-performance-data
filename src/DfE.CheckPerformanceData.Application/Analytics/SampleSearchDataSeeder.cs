namespace DfE.CheckPerformanceData.Application.Analytics;

// Development-only seeder that fabricates a plausible mix of search-event + feedback-message
// rows across a chosen time span so the search-analytics dashboard has something meaningful
// to demo. NOT for production — rows are indistinguishable from real user activity once
// written, and generating tens of thousands of events would poison a live retention window.
//
// Data-shape goals (drives the SeedAsync distribution choices):
//   * Weekday × hour weighting peaks in UK working hours (Mon-Fri, 09:00-17:00), tapers
//     overnight, drops further at weekends — matches the shape the round-7 heatmap card
//     is designed to visualise.
//   * ~15% zero-result queries (garbage strings + typos) so the funnel card and zero-result
//     top-N tile have something to show.
//   * Latency skew: log-normal-ish (median ~40 ms, p95 ~150 ms) with a rare multi-second
//     outlier per ~1000 events so the round-7 request-timings scatter has real dispersion.
//   * Session pool sized to give a mix of one-off searchers and power users (some sessions
//     run 20+ events) — mirrors real traffic and gives the session drill-in something to
//     drill into.
public sealed class SampleSearchDataSeeder(
    ISearchAnalyticsSink sink,
    ISampleSearchDataGateway messagesGateway)
{
    // Realistic-sounding queries the CMS surface would see. Mix of admin-facing help lookups
    // (submitting amendments, exam-entry codes, absence guidance) so the top-queries tile
    // renders recognisable text during a demo.
    private static readonly string[] LikelyQueries =
    [
        "pupil premium", "KS4 exam entry", "absence code guidance", "school census",
        "widget setup", "how to edit a page", "admin nav", "checking window",
        "post-16", "sixth form", "provisional results", "amendment request",
        "DSI role", "content block", "dead letter queue", "rules engine",
        "wiki page", "editor role", "publish schedule", "role settings",
    ];

    // Zero-result queries: garbage strings a user might type, typos, non-English fragments.
    private static readonly string[] ZeroResultQueries =
    [
        "asdfasdf", "XXXX", "qwerty", "test123", "zzzz", "aaaaaa",
        "pupel premim", "amendmnet", "wnidow", "abscence",
    ];

    // Message-body pool for the feedback-messages seed. Realistic-sounding sentences an
    // admin might read in the inbox — enough variety that the messages table + detail page
    // + first-line preview column all look real during a demo.
    private static readonly string[] MessageBodies =
    [
        "I can't find the guidance about the amendment process for KS4 results.",
        "The search returned nothing for pupil premium — is there a page about it?",
        "I was expecting to see the sixth-form checking window guidance here.",
        "Where do I find the DSI role list? The search suggests it but nothing loads.",
        "The absence-code page is missing from the results — used to be under Help.",
        "Search for 'widget' returned zero results — thought there was a setup guide.",
        "I need the exam-entry guidance for KS4 — search is not finding it.",
        "Looking for the publish-schedule page — the search just returns unrelated wiki pages.",
        "Cannot find the school-census due-dates page from search.",
        "The rules-engine documentation is not in search — is it published?",
        "I searched for 'admin nav' and got two content-block hits but no wiki page.",
        "Looking for the amendment window closing date — search returned old wiki pages only.",
        "Post-16 guidance page is missing from search results.",
        "Zero results for 'DSI editor role' — is there a page describing what editors can do?",
        "Search for 'content staging' returns unrelated hits — no page about the import/export.",
        "I can't find where to change the CMS page length setting from search.",
    ];

    // Hit URL pool used to populate search_event_results rows on non-zero events. Mix of
    // /help, /wiki, /guidance and content-block keys so the top-pages and top-blocks tiles
    // both have something to show. Rank values (0.05-0.99) are unitless relevance scores
    // that mimic what SearchEventMapper.From derives from a real SearchTelemetryEvent.
    private static readonly (string Kind, string Key)[] HitPool =
    [
        ("page",  "/help/getting-started"), ("page", "/help/submit-amendment"),
        ("page",  "/help/faq"), ("page", "/wiki/dsi-roles"),
        ("page",  "/wiki/rules-engine"), ("page", "/wiki/data-pipeline"),
        ("page",  "/wiki/wiki-sandbox"), ("page", "/support/contact-helpline"),
        ("page",  "/support/common-issues"), ("page", "/support/security-advice"),
        ("page",  "/guidance/ks2-checking"), ("page", "/guidance/ks4-checking"),
        ("page",  "/guidance/post-16"), ("block", "banner"), ("block", "footer"),
        ("block", "helpline-contact"), ("block", "amendment-window-notice"),
        ("block", "ks4-exam-entry"), ("block", "school-census-dates"),
    ];

    // Per-hour weight table (24 buckets, Mon..Fri). Peaks 09:00-11:00 and 14:00-16:00,
    // tapers overnight. Weekend traffic is a flat 10% of weekday hours (applied at the
    // day-level via WeekendDampen).
    private static readonly int[] HourWeights =
    [
        1, 1, 1, 1, 1, 2,    // 00-05
        4, 8, 12, 20, 22, 20, // 06-11 (working hours ramp)
        18, 16, 20, 22, 18, 12, // 12-17
        8, 6, 4, 3, 2, 2,    // 18-23
    ];

    private const double WeekendDampen = 0.10;

    // Runs one seed pass. Returns per-table counts so the caller can put them in a
    // success banner + audit payload. NowUtc + Seed are parameters so tests can pin
    // deterministic behaviour — the controller passes DateTime.UtcNow + a session-derived
    // seed so repeat clicks with the same preset add variety rather than duplicating.
    // The `progress` overload is opt-in: existing callers pass null and see the identical
    // behaviour they saw before. Progress ticks fire once per completed event batch (batch
    // size = 200) and once when the messages flush finishes, giving the modal poller a live
    // signal to render against. Cancellation is checked between events and between batches
    // so a Cancel click from the modal halts the seed within a few hundred milliseconds
    // rather than only after the whole run completes.
    public async Task<SampleSearchDataSeedResult> SeedAsync(
        TimeSpan span,
        int eventCount,
        int messageCount,
        DateTime nowUtc,
        int seed,
        CancellationToken cancellationToken,
        IProgress<SampleSearchDataSeedProgressTick>? progress = null)
    {
        if (eventCount <= 0 && messageCount <= 0)
        {
            return new SampleSearchDataSeedResult(0, 0, 0);
        }

        var rng = new Random(seed);
        var fromUtc = nowUtc - span;

        // Session pool: ~200 synthetic sessions is enough to give a mix of one-off + power
        // users while keeping the top-users tile populated with real data. Reduce to eventCount/3
        // if a tiny seed run would exhaust the pool.
        var sessionPoolSize = Math.Max(8, Math.Min(200, Math.Max(1, eventCount / 3)));
        var sessions = Enumerable.Range(0, sessionPoolSize)
            .Select(_ => Guid.NewGuid().ToString("N"))
            .ToArray();

        // Weight-biased session picker: give a handful of sessions much higher chance of
        // selection so a few power users emerge in the top-sessions view. Ten "power" indices
        // get 5x weight; everyone else weight 1.
        var sessionWeights = new double[sessionPoolSize];
        for (var i = 0; i < sessionPoolSize; i++) sessionWeights[i] = 1.0;
        var powerCount = Math.Min(10, sessionPoolSize);
        for (var i = 0; i < powerCount; i++)
        {
            var idx = rng.Next(sessionPoolSize);
            sessionWeights[idx] += 4.0;
        }
        var totalSessionWeight = sessionWeights.Sum();

        var eventsCreated = 0;
        var resultsCreated = 0;
        var lastCursor = fromUtc;

        // Emit events in batches so the sink's back-fill for FK ids runs on modest sizes.
        // Batch size of 200 keeps the two SaveChangesAsync round-trips-per-batch cost bounded
        // and gives cancellation a chance to interrupt mid-run for large presets. Also drives
        // the progress-tick cadence — every batch flush emits one tick with the running count
        // + the cursor timestamp of the most recent event.
        const int BatchSize = 200;
        var buffer = new List<SearchEventDto>(BatchSize);

        for (var i = 0; i < eventCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var occurredAt = PickTimestamp(rng, fromUtc, nowUtc);
            var sessionIdx = PickWeightedIndex(rng, sessionWeights, totalSessionWeight);
            var sessionId = sessions[sessionIdx];

            var isZeroResult = rng.NextDouble() < 0.15;
            var query = isZeroResult
                ? ZeroResultQueries[rng.Next(ZeroResultQueries.Length)]
                : LikelyQueries[rng.Next(LikelyQueries.Length)];

            var latency = PickLatency(rng, i);

            IReadOnlyList<SearchEventResultDto> results;
            int pages, blocks;
            if (isZeroResult)
            {
                results = Array.Empty<SearchEventResultDto>();
                pages = 0;
                blocks = 0;
            }
            else
            {
                (results, pages, blocks) = PickHits(rng);
            }
            resultsCreated += results.Count;

            buffer.Add(new SearchEventDto(
                OccurredAtUtc: occurredAt,
                SessionId: sessionId,
                QueryRaw: query,
                QueryNormalised: query.ToLowerInvariant(),
                Scope: "site",
                ResultsPages: pages,
                ResultsBlocks: blocks,
                LatencyMs: latency,
                Results: results));
            lastCursor = occurredAt;

            if (buffer.Count >= BatchSize)
            {
                await sink.RecordBatchAsync(buffer, cancellationToken);
                eventsCreated += buffer.Count;
                buffer.Clear();
                progress?.Report(new SampleSearchDataSeedProgressTick(
                    EventsWritten: eventsCreated,
                    MessagesWritten: 0,
                    CurrentCursorUtc: lastCursor));
            }
        }
        if (buffer.Count > 0)
        {
            await sink.RecordBatchAsync(buffer, cancellationToken);
            eventsCreated += buffer.Count;
            buffer.Clear();
            progress?.Report(new SampleSearchDataSeedProgressTick(
                EventsWritten: eventsCreated,
                MessagesWritten: 0,
                CurrentCursorUtc: lastCursor));
        }

        // Messages: scatter M messages across the same window. Emit via the gateway so the
        // Application layer stays free of a Persistence reference — the gateway's Db-backed
        // implementation writes the rows in one flush.
        var messages = new List<BackdatedSearchMessage>(messageCount);
        for (var i = 0; i < messageCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var occurredAt = PickTimestamp(rng, fromUtc, nowUtc);
            var sessionIdx = PickWeightedIndex(rng, sessionWeights, totalSessionWeight);
            var sessionId = sessions[sessionIdx];
            var whatLookingFor = MessageBodies[rng.Next(MessageBodies.Length)];
            var whatGot = rng.NextDouble() < 0.6
                ? "It just gave me unrelated hits."
                : (string?)null;
            var email = rng.NextDouble() < 0.5
                ? $"demo-user-{sessionIdx:D3}@example.com"
                : null; // "hide my email" was ticked

            messages.Add(new BackdatedSearchMessage(
                sessionId, occurredAt, whatLookingFor, whatGot, email));
        }
        await messagesGateway.WriteBackdatedMessagesAsync(messages, cancellationToken);
        var messagesCreated = messages.Count;

        // Final tick reflects the true totals so the UI can transition to the completed
        // state with definitive numbers rather than the last mid-run snapshot.
        progress?.Report(new SampleSearchDataSeedProgressTick(
            EventsWritten: eventsCreated,
            MessagesWritten: messagesCreated,
            CurrentCursorUtc: lastCursor));

        return new SampleSearchDataSeedResult(eventsCreated, resultsCreated, messagesCreated);
    }

    // Picks a UTC timestamp inside [fromUtc, nowUtc] weighted by the weekday × hour table
    // so the output distribution matches real UK-working-hours traffic. Simple rejection
    // sampling: pick uniform, compute weight for that hour, keep with probability weight/max.
    // Bounded iterations so a degenerate seed can't loop forever — after 8 tries we accept
    // whatever candidate we had. Callers get a plausible-enough spread on the seeded volumes
    // (500-80000) — the shape is what matters, not exact fidelity to the weight table.
    private static DateTime PickTimestamp(Random rng, DateTime fromUtc, DateTime toUtc)
    {
        var totalMs = (toUtc - fromUtc).TotalMilliseconds;
        DateTime candidate = fromUtc;
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var offsetMs = rng.NextDouble() * totalMs;
            candidate = fromUtc.AddMilliseconds(offsetMs);
            var hourWeight = HourWeights[candidate.Hour];
            var dow = candidate.DayOfWeek;
            var isWeekend = dow == DayOfWeek.Saturday || dow == DayOfWeek.Sunday;
            var effectiveWeight = isWeekend ? hourWeight * WeekendDampen : hourWeight;

            // Max possible weight across the table (peak at hour 15 = 22).
            const double MaxWeight = 22.0;
            if (rng.NextDouble() < effectiveWeight / MaxWeight)
            {
                return candidate;
            }
        }
        return candidate;
    }

    // Weighted index picker: sum weights, pick a target inside [0, total), walk until we
    // exceed it. O(N) per call which is fine for the 200-session pool.
    private static int PickWeightedIndex(Random rng, double[] weights, double total)
    {
        var target = rng.NextDouble() * total;
        var accum = 0.0;
        for (var i = 0; i < weights.Length; i++)
        {
            accum += weights[i];
            if (accum >= target) return i;
        }
        return weights.Length - 1;
    }

    // Latency in ms — log-normal-ish via Box-Muller. Every ~1000 events we return a deliberate
    // multi-second outlier (2-5 s) so the request-timings scatter has a visible tail.
    private static int PickLatency(Random rng, int index)
    {
        if (index > 0 && index % 1000 == 0)
        {
            return 2000 + rng.Next(3000); // 2-5 second outlier
        }

        // Box-Muller for a normal deviate, then exp-shift so we get a log-normal tail.
        var u1 = 1.0 - rng.NextDouble();
        var u2 = 1.0 - rng.NextDouble();
        var z = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        // ln-median ~ ln(40) = 3.69; sigma 0.55 gives p95 ~ 150 ms which matches the shape
        // the round-7 acceptance calls out.
        var latency = Math.Exp(3.69 + 0.55 * z);
        if (latency < 5) latency = 5;
        if (latency > 800) latency = 800;
        return (int)latency;
    }

    // Picks 3-10 hits for a non-zero-result event. Roughly 5% of the time inflate to 20+
    // hits so the top-N pages tiles have a few high-hit rows. Rank decays from top down
    // so the sorted order looks realistic (best match first).
    private static (IReadOnlyList<SearchEventResultDto> Results, int Pages, int Blocks) PickHits(Random rng)
    {
        var roll = rng.NextDouble();
        int hitCount;
        if (roll < 0.05) hitCount = 20 + rng.Next(6);        // 20-25 for the "long-tail" ~5%
        else if (roll < 0.35) hitCount = 1 + rng.Next(4);    // 1-4 for the "single-digit" ~30%
        else hitCount = 3 + rng.Next(8);                     // 3-10 for the rest

        var results = new List<SearchEventResultDto>(hitCount);
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);
        var pages = 0;
        var blocks = 0;
        var position = 1;
        var maxAttempts = hitCount * 3;
        for (var attempt = 0; attempt < maxAttempts && results.Count < hitCount; attempt++)
        {
            var (kind, key) = HitPool[rng.Next(HitPool.Length)];
            if (!seenKeys.Add(kind + "::" + key)) continue;
            var rank = (float)(1.0 - (position - 1) * 0.03 - rng.NextDouble() * 0.05);
            if (rank < 0.05f) rank = 0.05f;
            results.Add(new SearchEventResultDto(position, kind, key, rank));
            if (kind == "block") blocks++;
            else pages++;
            position++;
        }
        return (results, pages, blocks);
    }
}

// Per-run counts returned to the controller. Feeds both the success-banner TempData and
// the audit payload written on each seed.
public sealed record SampleSearchDataSeedResult(int EventsCreated, int ResultsCreated, int MessagesCreated);

// Progress tick fired by SampleSearchDataSeeder.SeedAsync between batches. Consumers are
// expected to be lightweight (in-memory job-store update, log line) — the seeder does not
// throttle emissions and a large seed may fire hundreds of ticks per second.
public sealed record SampleSearchDataSeedProgressTick(
    int EventsWritten,
    int MessagesWritten,
    DateTime CurrentCursorUtc);
