using DfE.CheckPerformanceData.Application.Media;
using Microsoft.Extensions.Options;

namespace DfE.CheckPerformanceData.Application.UnitTests.Media;

// Decoding a large image holds hundreds of megabytes, so only a few may run at once. The rest
// wait their turn, and a waiter that gives up leaves without taking a place.
public sealed class MediaProcessingGateTests
{
    private static MediaProcessingGate Gate(int limit) =>
        new(Options.Create(new MediaProcessingOptions { MaxConcurrentProcessing = limit }));

    [Fact]
    public void DefaultsToTwoAtOnce()
    {
        Assert.Equal(2, new MediaProcessingOptions().MaxConcurrentProcessing);
    }

    [Fact]
    public async Task LetsInUpToTheLimit_ThenMakesTheNextWait()
    {
        var gate = Gate(2);
        using var first = await gate.EnterAsync();
        using var second = await gate.EnterAsync();

        var third = gate.EnterAsync();
        await Task.Delay(50);
        Assert.False(third.IsCompleted);

        first.Dispose();
        using var entered = await third.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task AWaiterThatIsCancelled_StopsWaiting_AndTakesNoPlace()
    {
        var gate = Gate(1);
        var holder = await gate.EnterAsync();
        using var cts = new CancellationTokenSource();

        var waiting = gate.EnterAsync(cts.Token);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        holder.Dispose();
        using var next = await gate.EnterAsync().WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ReleasingTwice_FreesOnlyOnePlace()
    {
        var gate = Gate(1);
        var holder = await gate.EnterAsync();
        holder.Dispose();
        holder.Dispose();

        using var a = await gate.EnterAsync();
        var b = gate.EnterAsync();
        await Task.Delay(50);
        Assert.False(b.IsCompleted);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ALimitBelowOne_IsRefused(int limit)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Gate(limit));
    }
}
