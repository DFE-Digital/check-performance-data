using Microsoft.Extensions.Logging;

namespace DfE.CheckPerformanceData.Application.Logging;

// Runtime knobs for the database log sink. Bound from configuration (section
// "AppLogSink") or set in code; all fields have sensible defaults.
public sealed class AppLogSinkOptions
{
    public const string SectionName = "AppLogSink";

    // Minimum log level that reaches the sink. Below this is dropped in-place, so a very
    // chatty framework category can't fill the table.
    public LogLevel MinLevel { get; set; } = LogLevel.Information;

    // Categories to skip regardless of level. Prefix match. Kept small on purpose — the
    // idea is to filter *out* framework noise, not build a routing table.
    public IReadOnlyList<string> SkipCategories { get; set; } =
    [
        "Microsoft.EntityFrameworkCore.Database.Command",
        "Microsoft.EntityFrameworkCore.Infrastructure",
        "Microsoft.AspNetCore.Hosting.Diagnostics",
        "Microsoft.AspNetCore.Routing",
        "Microsoft.AspNetCore.Server",
        "Microsoft.AspNetCore.StaticFiles",
        "Microsoft.AspNetCore.Mvc.Infrastructure",
        // The sink writes through EF Core; without this we would log our own writes.
        "DfE.CheckPerformanceData.Application.Logging",
        "DfE.CheckPerformanceData.Persistence.Repositories.AppLogRepository"
    ];

    // Batch size hint for the background writer. Rows are written as soon as this many
    // buffer up or FlushInterval elapses, whichever comes first.
    public int BatchSize { get; set; } = 50;

    // How often the background writer wakes and flushes anything in the buffer, even if the
    // batch is smaller than BatchSize.
    public TimeSpan FlushInterval { get; set; } = TimeSpan.FromSeconds(1);

    // Bound on the in-memory channel. If bursts exceed this, the oldest queued log is dropped
    // — the sink is best-effort and must never block application code or exhaust memory.
    public int ChannelCapacity { get; set; } = 10_000;
}
