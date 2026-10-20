using DfE.CheckPerformanceData.Web.Middleware;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web;

public sealed class CspNonceTests
{
    // 128 bits of OS randomness, base64url-encoded without padding: URL- and attribute-safe,
    // and never so short that two requests could collide.
    [Fact]
    public void Create_ReturnsA22CharacterBase64UrlValue()
    {
        var nonce = CspNonce.Create();

        Assert.Equal(22, nonce.Length);
        Assert.DoesNotContain('+', nonce);
        Assert.DoesNotContain('/', nonce);
        Assert.DoesNotContain('=', nonce);
    }

    // A per-request nonce is meaningless if it repeats.
    [Fact]
    public void Create_ReturnsDifferentValues()
    {
        var seen = new HashSet<string>();
        for (var i = 0; i < 100; i++)
        {
            Assert.True(seen.Add(CspNonce.Create()), "CspNonce.Create returned a duplicate value");
        }
    }
}
