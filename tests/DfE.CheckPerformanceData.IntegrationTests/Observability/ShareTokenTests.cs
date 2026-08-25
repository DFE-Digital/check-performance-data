using DfE.CheckPerformanceData.Application.Observability;
using DfE.CheckPerformanceData.Persistence.Observability;
using DfE.CheckPerformanceData.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.IntegrationTests.Observability;

// The share/wallboard token gate, exercised against real Postgres. A token is a cryptographically
// random opaque string returned once on generation; only its SHA-256 hash is stored, so a DB read
// can never reveal a live token. Validation hashes the incoming token and compares the hash to a
// non-revoked row with a constant-time compare; revocation makes the same token validate false.
[Collection(nameof(PostgresCollection))]
public sealed class ShareTokenTests
{
    private readonly PostgresFixture _fixture;

    public ShareTokenTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    // --- GenerateAsync stores only a hash; the plaintext token is never persisted ---

    [Fact]
    public async Task GenerateAsync_StoresOnlyAHash_NotThePlaintextToken()
    {
        await ResetTokensAsync();
        var service = CreateService();

        var token = await service.GenerateAsync("Stakeholder demo", ShareSurface.Share, "admin-1");

        Assert.False(string.IsNullOrWhiteSpace(token));

        await using var context = _fixture.CreateContext();
        var row = await context.ShareTokens.SingleAsync();

        // The plaintext is not stored anywhere on the row.
        Assert.NotEqual(token, row.TokenHash);
        Assert.DoesNotContain(token, row.TokenHash);
        Assert.Equal("Stakeholder demo", row.Label);
        Assert.Equal("admin-1", row.CreatedBy);
        Assert.Null(row.RevokedAtUtc);
        // The stored hash is a SHA-256 hex/base64 digest, not the raw token.
        Assert.True(row.TokenHash.Length >= 32);
    }

    // --- A freshly generated token validates true, then false after revocation ---

    [Fact]
    public async Task ValidateAsync_IsTrueForFreshToken_AndFalseAfterRevoke()
    {
        await ResetTokensAsync();
        var service = CreateService();

        var token = await service.GenerateAsync("Wallboard", ShareSurface.Wallboard, "admin-2");

        Assert.True(await service.ValidateAsync(token, ShareSurface.Wallboard));

        Guid id;
        await using (var context = _fixture.CreateContext())
        {
            id = (await context.ShareTokens.SingleAsync()).Id;
        }

        await service.RevokeAsync(id);

        Assert.False(await service.ValidateAsync(token, ShareSurface.Wallboard));
    }

    // --- An unknown / wrong token validates false ---

    [Fact]
    public async Task ValidateAsync_IsFalseForUnknownToken()
    {
        await ResetTokensAsync();
        var service = CreateService();

        await service.GenerateAsync("Share", ShareSurface.Share, "admin-3");

        Assert.False(await service.ValidateAsync("not-the-real-token", ShareSurface.Share));
    }

    // --- A token issued for one surface does not validate for the other ---

    [Fact]
    public async Task ValidateAsync_IsScopedToTheSurfaceItWasIssuedFor()
    {
        await ResetTokensAsync();
        var service = CreateService();

        var shareToken = await service.GenerateAsync("Share only", ShareSurface.Share, "admin-4");

        Assert.True(await service.ValidateAsync(shareToken, ShareSurface.Share));
        Assert.False(await service.ValidateAsync(shareToken, ShareSurface.Wallboard));
    }

    private IShareTokenService CreateService() =>
        new ShareTokenService(_fixture.CreateContext());

    private async Task ResetTokensAsync()
    {
        await using var context = _fixture.CreateContext();
        await context.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE share_tokens RESTART IDENTITY CASCADE;");
    }
}
