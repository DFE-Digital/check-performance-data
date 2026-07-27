using System.Threading.Channels;

namespace DfE.CheckPerformanceData.Application.Analytics;

// Producer/consumer channel between the SinkAndLogSearchTelemetry decorator (fan-in from
// every /search request) and the SearchEventWriter background service (single reader
// draining to the sink in batches). Wrapped in a small class so the DI container hands
// the same instance to both sides and so tests can swap in a differently-configured
// channel without touching the decorator or the writer.
//
// FullMode is Wait — the newest arrival is dropped caller-side (the decorator uses the
// non-blocking TryWrite and discards the DTO on a false return) so drops are observable
// via the counter. AppLogChannel uses DropOldest instead because its writer is a logger
// that must never receive false; the analytics decorator specifically wants the false
// return so it can bump the drop counter and log a warn line.
//
// Functionally this delivers the plan's intent — "prefer the older recorded stream over
// a burst of new writes when the sink is stalled" — because when the buffer is full the
// TryWrite is refused and the newest event is the one that never lands. The trade-off is
// that under sustained overload the newest events shed first; the drop counter counts
// them so an operator can see the shed.
public sealed class SearchAnalyticsChannel
{
    // Fixed capacity. The bound is large enough to absorb a burst of concurrent requests
    // (see SinkLoadTests — 500 concurrent /search requests must not shed) but small
    // enough that a stalled sink cannot hold multiple megabytes of DTOs in memory.
    private const int Capacity = 1000;

    public SearchAnalyticsChannel()
    {
        Channel = System.Threading.Channels.Channel.CreateBounded<SearchEventDto>(
            new BoundedChannelOptions(Capacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false
            });
    }

    public Channel<SearchEventDto> Channel { get; }
}
