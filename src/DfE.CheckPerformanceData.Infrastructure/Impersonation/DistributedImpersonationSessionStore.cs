using System.Text.Json;
using DfE.CheckPerformanceData.Application.Impersonation;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Distributed;

namespace DfE.CheckPerformanceData.Infrastructure.Impersonation;

public sealed class DistributedImpersonationSessionStore(IDistributedCache cache, IDataProtectionProvider protection) : IImpersonationSessionStore
{
    private readonly IDataProtector _protector = protection.CreateProtector("CPD.Impersonation.State.v1");

    public async Task<ImpersonationSessionState?> GetAsync(string binding, CancellationToken ct)
    {
        var bytes = await cache.GetAsync("impersonation:" + binding, ct);
        // A corrupt protected record must fail closed, never become the original context.
        return bytes is null ? null : JsonSerializer.Deserialize<ImpersonationSessionState>(_protector.Unprotect(bytes))
            ?? throw new InvalidOperationException("Impersonation state could not be read.");
    }

    public Task SetAsync(string binding, ImpersonationSessionState state, CancellationToken ct) =>
        cache.SetAsync("impersonation:" + binding, _protector.Protect(JsonSerializer.SerializeToUtf8Bytes(state)),
            new DistributedCacheEntryOptions { SlidingExpiration = TimeSpan.FromDays(7) }, ct);
}
