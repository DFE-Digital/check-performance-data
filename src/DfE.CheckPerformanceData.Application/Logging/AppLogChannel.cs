using System.Threading.Channels;

namespace DfE.CheckPerformanceData.Application.Logging;

// Producer/consumer channel between the (sync) DatabaseLogger and the background writer.
// Wrapped in a small class so the DI container can hand the same instance to both sides
// and so writers can be swapped in tests without touching the logger.
public sealed class AppLogChannel
{
    public AppLogChannel(AppLogSinkOptions options)
    {
        Channel = System.Threading.Channels.Channel.CreateBounded<AppLogDto>(
            new BoundedChannelOptions(options.ChannelCapacity)
            {
                // Never block a log call: drop the oldest queued row on overflow so the
                // application keeps running even if the DB is stalled.
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false
            });
    }

    public Channel<AppLogDto> Channel { get; }
}
