using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using DfE.CheckPerformanceData.Application.Impersonation;
using DfE.CheckPerformanceData.Persistence.Contexts;

namespace DfE.CheckPerformanceData.Persistence.Locking;

public sealed class PostgresImpersonationSessionLock(IPortalDbContext db) : IImpersonationSessionLock
{
    public async Task<IAsyncDisposable> AcquireAsync(string binding, CancellationToken ct)
    {
        var key = BinaryPrimitives.ReadInt64BigEndian(SHA256.HashData(Encoding.UTF8.GetBytes("CPD.Impersonation.Lock:" + binding)));
        var gate = new PostgresAdvisoryLock(db, key);
        while (!await gate.TryAcquireAsync(ct))
            await Task.Delay(25, ct);
        return new Lease(gate);
    }

    private sealed class Lease(PostgresAdvisoryLock gate) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => new(gate.ReleaseAsync(CancellationToken.None));
    }
}
