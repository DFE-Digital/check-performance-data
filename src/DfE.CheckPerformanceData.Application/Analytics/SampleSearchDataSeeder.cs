using DfE.CheckPerformanceData.Application.ContentBlocks;
using DfE.CheckPerformanceData.Application.PageTree;

namespace DfE.CheckPerformanceData.Application.Analytics;

// Development-only seeder that fabricates a plausible mix of search-event + feedback-message
// rows across a chosen time span so the search-analytics dashboard has something meaningful
// to demo. NOT for production — rows are indistinguishable from real user activity once
// written, and generating tens of thousands of events would poison a live retention window.
//
// Data-shape goals (drives the SeedAsync distribution choices):
//   * Weekday × hour weighting peaks in UK working hours (Mon-Fri, 09:00-17:00), tapers
//     overnight, drops further at weekends — matches the shape the weekday-hour heatmap
//     card is designed to visualise.
//   * ~15% zero-result queries (garbage strings + typos) so the funnel card and zero-result
//     top-N tile have something to show.
//   * Latency skew: log-normal-ish (median ~40 ms, p95 ~150 ms) with a rare multi-second
//     outlier per ~1000 events so the request-timings scatter has real dispersion.
//   * Session pool sized to give a mix of one-off searchers and power users (some sessions
//     run 20+ events) — mirrors real traffic and gives the session drill-in something to
//     drill into.
public sealed class SampleSearchDataSeeder(
    ISearchAnalyticsSink sink,
    ISampleSearchDataGateway messagesGateway,
    IPageNodeRepository? pageRepository = null,
    IContentBlockService? contentBlockService = null)
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

    // The hit pool used to populate search_event_results rows on non-zero events is
    // NO LONGER a hardcoded literal — it is loaded at seed-start from the real CMS state
    // via the injected repositories: every live PageNode contributes one ("page", path)
    // entry and every ContentBlock contributes one ("block", key) entry. That way the
    // Top pages + Top content blocks admin cards surface entities admins can actually
    // navigate to, instead of fictional keys the seeder invented. See BuildHitPoolAsync.

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

    // Fixed hit pool used when the CMS DB is empty AND no wired dependencies contribute
    // entries. Must be large enough that PickHits (dedup via seenKeys, up to 25 hits/event)
    // can honour the promised 3-25 hits per non-zero-result event; 20 pages + 10 blocks
    // covers the upper bound comfortably and gives the Top Pages / Top Content Blocks
    // tiles a realistic mix on a fresh empty DB.
    private static readonly (string Kind, string Key)[] SentinelHitPool =
    [
        ("page", "/help/getting-started"),
        ("page", "/help/submitting-amendments"),
        ("page", "/help/exam-entry-codes"),
        ("page", "/help/checking-performance-data"),
        ("page", "/help/notes-of-amendments"),
        ("page", "/help/absence"),
        ("page", "/help/ks4-guidance"),
        ("page", "/help/ks2-guidance"),
        ("page", "/help/post16-guidance"),
        ("page", "/help/attainment-8"),
        ("page", "/help/progress-8"),
        ("page", "/help/ebacc"),
        ("page", "/help/pupil-premium"),
        ("page", "/help/school-details"),
        ("page", "/help/downloading-data"),
        ("page", "/help/user-accounts"),
        ("page", "/help/permissions"),
        ("page", "/help/contact"),
        ("page", "/help/faq"),
        ("page", "/help/glossary"),
        ("block", "banner-notice"),
        ("block", "footer-contact"),
        ("block", "helpline-hours"),
        ("block", "email-support"),
        ("block", "how-to-navigate"),
        ("block", "faq-summary"),
        ("block", "quick-links"),
        ("block", "keyboard-shortcuts"),
        ("block", "accessibility-statement"),
        ("block", "cookie-notice"),
    ];

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
        IProgress<SampleSearchDataSeedProgressTick>? progress = null,
        Guid? jobId = null)
    {
        if (eventCount <= 0 && messageCount <= 0)
        {
            return new SampleSearchDataSeedResult(0, 0, 0);
        }

        // Load the hit pool from real CMS state before any RNG use so the pool is stable
        // across a single seed run (and a run whose RNG seed is identical produces the
        // same distribution over the same pool). When the DB has no pages AND no blocks
        // the pool falls back to a single-entry stub so a fresh empty DB still emits
        // valid (all-zero-result) events rather than crashing.
        var hitPool = await BuildHitPoolAsync(cancellationToken);

        var rng = new Random(seed);
        var fromUtc = nowUtc - span;
        // Guid-as-text ("N" — 32 hex chars, no dashes) marker written on every row so a
        // subsequent Cancel/rollback drops exactly this run's rows without touching real
        // events or other jobs. Null means "no per-run marker" (used by the existing
        // integration tests that predate the rollback surface).
        var jobIdText = jobId?.ToString("N");

        // Multi-scale variance profile — pre-computed at seed start from the same RNG so
        // repeat runs with the same seed reproduce identical timestamp / latency
        // distributions. Layers three independent sources of variance on top of the
        // static HourWeights × WeekendDampen weighting so a 90/365-day window doesn't
        // paint twelve near-identical weeks on the dashboard:
        //   * Weekly multiplier — one draw per ISO-ish week, [0.5, 1.8] with light
        //     autocorrelation so adjacent weeks trend together.
        //   * Daily anomalies — ~5% "spike days" (2×-3×), ~10% "quiet days"
        //     (0.2×-0.4×). Picked deterministically at seed start.
        //   * Outage bursts — 1-2 per quarter, contiguous 3-6 h windows where volume
        //     drops to ~30% of baseline AND latency inflates 4×-8×.
        var variance = VarianceProfile.Build(rng, fromUtc, nowUtc);

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

            var occurredAt = PickTimestamp(rng, fromUtc, nowUtc, variance);
            var sessionIdx = PickWeightedIndex(rng, sessionWeights, totalSessionWeight);
            var sessionId = sessions[sessionIdx];

            var isZeroResult = rng.NextDouble() < 0.15;
            var query = isZeroResult
                ? ZeroResultQueries[rng.Next(ZeroResultQueries.Length)]
                : LikelyQueries[rng.Next(LikelyQueries.Length)];

            var latency = PickLatency(rng, i, occurredAt, variance);

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
                (results, pages, blocks) = PickHits(rng, hitPool);
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
                Results: results,
                // Every row written by the seeder carries the marker so the delete-
                // seeded admin action can drop this row without touching real events.
                IsSeeded: true,
                // Per-run marker so the Cancel/rollback path can drop THIS job's
                // rows in one WHERE clause. Null when no jobId was supplied.
                JobId: jobIdText));
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

            var occurredAt = PickTimestamp(rng, fromUtc, nowUtc, variance);
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
                sessionId, occurredAt, whatLookingFor, whatGot, email, jobIdText));
        }
        await messagesGateway.WriteBackdatedMessagesAsync(messages, cancellationToken);
        var messagesCreated = messages.Count;

        // Final tick reflects the true totals so the UI can transition to the completed
        // state with definitive numbers rather than the last mid-run snapshot.
        progress?.Report(new SampleSearchDataSeedProgressTick(
            EventsWritten: eventsCreated,
            MessagesWritten: messagesCreated,
            CurrentCursorUtc: lastCursor));

        var outageSnapshot = variance.Outages
            .Select(o => new SampleSearchDataOutageWindow(o.StartUtc, o.EndUtc))
            .ToArray();
        return new SampleSearchDataSeedResult(
            eventsCreated, resultsCreated, messagesCreated, outageSnapshot);
    }

    // Picks a UTC timestamp inside [fromUtc, nowUtc] weighted by the weekday × hour table
    // multiplied through the multi-scale variance profile so the aggregate shape has
    // week-to-week variance, occasional spike / quiet days, and rare outage windows.
    // Simple rejection sampling: pick uniform, compute weight, keep with probability
    // weight / actualMaxWeight. actualMaxWeight is derived at profile build time (not
    // hardcoded) because the multipliers can push a peak above the base HourWeights max.
    // Bounded iterations so a degenerate seed can't loop forever — after 32 tries we
    // accept whatever candidate we had. Callers get a plausible-enough spread on the
    // seeded volumes (500-80000).
    private static DateTime PickTimestamp(
        Random rng, DateTime fromUtc, DateTime toUtc, VarianceProfile variance)
    {
        var totalMs = (toUtc - fromUtc).TotalMilliseconds;
        DateTime candidate = fromUtc;
        for (var attempt = 0; attempt < 32; attempt++)
        {
            var offsetMs = rng.NextDouble() * totalMs;
            candidate = fromUtc.AddMilliseconds(offsetMs);
            var effectiveWeight = variance.WeightAt(candidate);
            if (rng.NextDouble() < effectiveWeight / variance.MaxWeight)
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

    // Latency in ms — log-normal-ish via Box-Muller. Every ~1000 events we return a
    // deliberate multi-second outlier (2-5 s) so the request-timings scatter has a
    // visible tail. When the occurredAt falls inside an outage window the base draw
    // is multiplied by a 4×-8× factor pulled from the variance profile so the mean
    // latency for that hour is visibly elevated on the chart.
    private static int PickLatency(
        Random rng, int index, DateTime occurredAt, VarianceProfile variance)
    {
        if (index > 0 && index % 1000 == 0)
        {
            return 2000 + rng.Next(3000); // 2-5 second outlier
        }

        // Box-Muller for a normal deviate, then exp-shift so we get a log-normal tail.
        var u1 = 1.0 - rng.NextDouble();
        var u2 = 1.0 - rng.NextDouble();
        var z = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        // ln-median ~ ln(40) = 3.69; sigma 0.55 gives p95 ~ 150 ms — matches the shape the
        // request-timings scatter is expected to display.
        var latency = Math.Exp(3.69 + 0.55 * z);
        // Outage inflation lands here — cap at 6000 ms so a single elevated hour does
        // not blow the scatter's Y-axis.
        var outageMultiplier = variance.OutageLatencyMultiplierAt(occurredAt);
        if (outageMultiplier > 1.0) latency *= outageMultiplier;
        if (latency < 5) latency = 5;
        if (latency > 6000) latency = 6000;
        return (int)latency;
    }

    // Loads real pages + blocks from the CMS. Pages contribute one ("page", "/path") entry
    // each; blocks contribute one ("block", "key") entry each. The pool is capped at 500 to
    // keep the per-run memory + random-draw distribution reasonable. When either dependency
    // is null (older tests that construct the seeder without wiring the CMS), or when the
    // DB has no live pages or blocks, falls back to a single-entry sentinel so PickHits
    // terminates rather than throwing on an empty pool.
    private async Task<(string Kind, string Key)[]> BuildHitPoolAsync(CancellationToken cancellationToken)
    {
        const int PoolCap = 500;

        List<(string, string)> pages = new();
        List<(string, string)> blocks = new();

        if (pageRepository is not null)
        {
            var pageTree = await pageRepository.GetTreeAsync();
            pages = pageTree
                .Where(p => !string.IsNullOrWhiteSpace(p.Path))
                .Select(p => ("page", "/" + p.Path.TrimStart('/')))
                .ToList();
        }

        if (contentBlockService is not null)
        {
            var blockList = await contentBlockService.GetAllAsync();
            blocks = blockList
                .Where(b => !string.IsNullOrWhiteSpace(b.Key))
                .Select(b => ("block", b.Key))
                .ToList();
        }

        var combined = pages.Concat(blocks).ToArray();
        if (combined.Length == 0)
        {
            // Nothing to seed against — fires when the DB is empty OR when a caller
            // constructs the seeder with both CMS deps null. Return a fixed pool that
            // is BIG ENOUGH to honour the 3-25 hits invariant PickHits promises: with
            // dedup via seenKeys, a single-entry pool would collapse every event to
            // one hit and flatten the Top Pages / Top Content Blocks / long-tail tiles.
            return SentinelHitPool;
        }

        if (combined.Length > PoolCap)
        {
            combined = combined.Take(PoolCap).ToArray();
        }
        return combined;
    }

    // Picks 3-10 hits for a non-zero-result event. Roughly 5% of the time inflate to 20+
    // hits so the top-N pages tiles have a few high-hit rows. Rank decays from top down
    // so the sorted order looks realistic (best match first).
    private static (IReadOnlyList<SearchEventResultDto> Results, int Pages, int Blocks) PickHits(
        Random rng,
        (string Kind, string Key)[] hitPool)
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
            var (kind, key) = hitPool[rng.Next(hitPool.Length)];
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

    // Pre-computed variance profile for a single seed run. Owns three independent
    // multiplier layers driven by the seeder's deterministic RNG so the same seed
    // reproduces the same profile:
    //   * WeeklyMultiplier[weekIndex] — one draw per week in the seeded window, in the
    //     range [0.5, 1.8], with light autocorrelation so adjacent weeks trend together.
    //   * DailyAnomaly[dayIndex] — most days = 1.0; ~5% "spike days" in [2.0, 3.0]; ~10%
    //     "quiet days" in [0.2, 0.4].
    //   * OutageWindows — 1-2 contiguous 3-6 h intervals with a volume dampen (~0.3) and
    //     a latency multiplier (~4×-8×). Ordered by start-time so a binary lookup is not
    //     required — the list is at most two entries.
    // The Multiplier lookup for a timestamp composes all three layers; MaxWeight is
    // pre-computed by scanning every (day, hour) bucket so the rejection sampler in
    // PickTimestamp has a valid ceiling regardless of how the multipliers land.
    internal sealed class VarianceProfile
    {
        private readonly DateTime _fromUtc;
        private readonly double[] _weeklyMultiplier;   // per week from window start
        private readonly double[] _dailyAnomaly;       // per day from window start
        private readonly OutageWindow[] _outages;

        public double MaxWeight { get; }

        private VarianceProfile(
            DateTime fromUtc,
            double[] weekly,
            double[] daily,
            OutageWindow[] outages,
            double maxWeight)
        {
            _fromUtc = fromUtc;
            _weeklyMultiplier = weekly;
            _dailyAnomaly = daily;
            _outages = outages;
            MaxWeight = maxWeight;
        }

        // Build the profile deterministically from the RNG. Timestamps are truncated
        // to the day floor so multi-hour spans still land on stable indices.
        public static VarianceProfile Build(Random rng, DateTime fromUtc, DateTime toUtc)
        {
            var totalDays = Math.Max(1, (int)Math.Ceiling((toUtc.Date - fromUtc.Date).TotalDays) + 1);
            var totalWeeks = Math.Max(1, (int)Math.Ceiling(totalDays / 7.0));

            // Weekly multipliers with light autocorrelation.
            //   next = clamp(prev × (0.7 + 0.6 × rng), 0.5, 1.8)
            // starting from 1.0. Recipe mirrors the coordinator's guidance.
            var weekly = new double[totalWeeks];
            weekly[0] = 1.0;
            for (var w = 1; w < totalWeeks; w++)
            {
                var step = 0.7 + 0.6 * rng.NextDouble();
                var next = weekly[w - 1] * step;
                if (next < 0.5) next = 0.5;
                if (next > 1.8) next = 1.8;
                weekly[w] = next;
            }

            // Daily anomalies. Default 1.0; roll spike/quiet dice per day.
            var daily = new double[totalDays];
            for (var d = 0; d < totalDays; d++)
            {
                var roll = rng.NextDouble();
                if (roll < 0.05)
                {
                    // Spike day: 2.0-3.0×.
                    daily[d] = 2.0 + rng.NextDouble();
                }
                else if (roll < 0.15)
                {
                    // Quiet day: 0.2-0.4×.
                    daily[d] = 0.2 + rng.NextDouble() * 0.2;
                }
                else
                {
                    daily[d] = 1.0;
                }
            }

            // Outage windows. 1-2 per quarter (90-day scaling); on shorter windows we
            // still get at least one if the window is >= 30 days, zero otherwise so a
            // 24 h preset doesn't produce a spurious multi-hour outage.
            int outageCount;
            if (totalDays >= 60)
            {
                outageCount = 1 + (rng.NextDouble() < 0.5 ? 1 : 0);
            }
            else if (totalDays >= 30)
            {
                outageCount = rng.NextDouble() < 0.6 ? 1 : 0;
            }
            else
            {
                outageCount = 0;
            }

            var outages = new OutageWindow[outageCount];
            for (var o = 0; o < outageCount; o++)
            {
                var startDayIdx = rng.Next(totalDays);
                // Land the outage inside working hours so it shows up on the dashboard.
                var startHour = 9 + rng.Next(8); // 09-16
                var durationHours = 3 + rng.Next(4); // 3-6
                var latencyMultiplier = 4.0 + rng.NextDouble() * 4.0; // 4×-8×
                var start = fromUtc.Date.AddDays(startDayIdx).AddHours(startHour);
                var end = start.AddHours(durationHours);
                outages[o] = new OutageWindow(start, end, latencyMultiplier);
            }

            // Compute the true max weight across the multiplier grid so the rejection
            // sampler never exceeds probability 1.0. Static base = HourWeights peak (22)
            // × max weekly (1.8) × max daily (3.0). Weekend and outage dampens only ever
            // pull the value down, so we scan only the base × weekly × daily combos.
            var maxHourWeight = HourWeights.Max();
            var maxWeekly = weekly.Max();
            var maxDaily = daily.Max();
            var maxWeight = maxHourWeight * maxWeekly * maxDaily;

            return new VarianceProfile(fromUtc, weekly, daily, outages, maxWeight);
        }

        public double WeightAt(DateTime candidate)
        {
            var hourWeight = HourWeights[candidate.Hour];
            var dow = candidate.DayOfWeek;
            var isWeekend = dow == DayOfWeek.Saturday || dow == DayOfWeek.Sunday;
            var weekendFactor = isWeekend ? WeekendDampen : 1.0;

            var dayIdx = (int)(candidate.Date - _fromUtc.Date).TotalDays;
            if (dayIdx < 0) dayIdx = 0;
            if (dayIdx >= _dailyAnomaly.Length) dayIdx = _dailyAnomaly.Length - 1;
            var dailyFactor = _dailyAnomaly[dayIdx];

            var weekIdx = dayIdx / 7;
            if (weekIdx >= _weeklyMultiplier.Length) weekIdx = _weeklyMultiplier.Length - 1;
            var weeklyFactor = _weeklyMultiplier[weekIdx];

            var outageFactor = 1.0;
            for (var i = 0; i < _outages.Length; i++)
            {
                if (candidate >= _outages[i].StartUtc && candidate < _outages[i].EndUtc)
                {
                    outageFactor = 0.3; // volume dampen inside the outage
                    break;
                }
            }

            return hourWeight * weekendFactor * dailyFactor * weeklyFactor * outageFactor;
        }

        public double OutageLatencyMultiplierAt(DateTime candidate)
        {
            for (var i = 0; i < _outages.Length; i++)
            {
                if (candidate >= _outages[i].StartUtc && candidate < _outages[i].EndUtc)
                {
                    return _outages[i].LatencyMultiplier;
                }
            }
            return 1.0;
        }

        // Snapshot exposed to the audit payload so a reviewer can correlate an audit
        // row with the outage bar they see on the dashboard.
        public IReadOnlyList<(DateTime StartUtc, DateTime EndUtc)> Outages
            => _outages.Select(o => (o.StartUtc, o.EndUtc)).ToArray();

        private readonly struct OutageWindow
        {
            public OutageWindow(DateTime startUtc, DateTime endUtc, double latencyMultiplier)
            {
                StartUtc = startUtc;
                EndUtc = endUtc;
                LatencyMultiplier = latencyMultiplier;
            }
            public DateTime StartUtc { get; }
            public DateTime EndUtc { get; }
            public double LatencyMultiplier { get; }
        }
    }
}

// Per-run counts returned to the controller. Feeds both the success-banner TempData and
// the audit payload written on each seed. OutageWindowsUtc surfaces the multi-scale
// variance layer's outage bursts so a reviewer can correlate an audit row with the
// elevated-latency bar they see on the dashboard for that period.
public sealed record SampleSearchDataSeedResult(
    int EventsCreated,
    int ResultsCreated,
    int MessagesCreated,
    IReadOnlyList<SampleSearchDataOutageWindow>? OutageWindowsUtc = null);

// Read-only snapshot of one outage window the variance profile inserted into the run.
// Written into the audit payload so a reviewer sees why the dashboard has an elevated-
// latency band across a specific time range.
public sealed record SampleSearchDataOutageWindow(DateTime StartUtc, DateTime EndUtc);

// Progress tick fired by SampleSearchDataSeeder.SeedAsync between batches. Consumers are
// expected to be lightweight (in-memory job-store update, log line) — the seeder does not
// throttle emissions and a large seed may fire hundreds of ticks per second.
public sealed record SampleSearchDataSeedProgressTick(
    int EventsWritten,
    int MessagesWritten,
    DateTime CurrentCursorUtc);
