using DfE.CheckPerformanceData.Application.Media.Serving;

namespace DfE.CheckPerformanceData.Application.UnitTests.Media.Serving;

// Only the server makes resize and crop URLs, and it signs each one, so a visitor cannot make the
// service render sizes nobody asked for. A signature covers the image, its version and every
// setting; change any of them and it no longer matches.
public sealed class MediaUrlSignerTests
{
    private static readonly Guid Id = Guid.Parse("8a8e3a52-38a9-4a65-9e3a-5f8f2f0f7a11");
    private static readonly MediaRendition Width640 = new(640, null, null, null);

    private static MediaUrlSigner Signer(byte fill = 7) => new(new FixedSigningKey(Enumerable.Repeat(fill, 32).ToArray()));

    [Fact]
    public void ASignature_MatchesWhatItSigned()
    {
        var signer = Signer();
        var sig = signer.Sign(Id, 1, Width640);

        Assert.True(signer.IsValid(Id, 1, Width640, sig));
    }

    [Fact]
    public void TheSameRequest_AlwaysGetsTheSameSignature()
    {
        Assert.Equal(Signer().Sign(Id, 1, Width640), Signer().Sign(Id, 1, Width640));
    }

    [Fact]
    public void ASignature_IsUrlSafe()
    {
        var sig = Signer().Sign(Id, 1, new MediaRendition(1920, new MediaCrop(0.1m, 0.2m, 0.3m, 0.4m), new MediaAspectRatio(16, 9), new MediaFocalPoint(0.5m, 0.5m)));

        Assert.Matches("^[A-Za-z0-9_-]+$", sig);
    }

    [Fact]
    public void TamperedSettings_DoNotMatch()
    {
        var signer = Signer();
        var sig = signer.Sign(Id, 1, Width640);

        Assert.False(signer.IsValid(Id, 1, new MediaRendition(1920, null, null, null), sig));
        Assert.False(signer.IsValid(Id, 1, Width640 with { Crop = new MediaCrop(0, 0, 0.5m, 0.5m) }, sig));
        Assert.False(signer.IsValid(Id, 1, Width640 with { Aspect = new MediaAspectRatio(1, 1) }, sig));
    }

    [Fact]
    public void AnotherImageOrVersion_DoesNotMatch()
    {
        var signer = Signer();
        var sig = signer.Sign(Id, 1, Width640);

        Assert.False(signer.IsValid(Guid.NewGuid(), 1, Width640, sig));
        Assert.False(signer.IsValid(Id, 2, Width640, sig));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-signature")]
    [InlineData("%%%")]
    public void AMissingOrMalformedSignature_DoesNotMatch(string? sig)
    {
        Assert.False(Signer().IsValid(Id, 1, Width640, sig));
    }

    [Fact]
    public void ASignatureFromAnotherKey_DoesNotMatch()
    {
        var sig = Signer(fill: 1).Sign(Id, 1, Width640);

        Assert.False(Signer(fill: 2).IsValid(Id, 1, Width640, sig));
    }

    [Fact]
    public void ATruncatedSignature_DoesNotMatch()
    {
        var signer = Signer();
        var sig = signer.Sign(Id, 1, Width640);

        Assert.False(signer.IsValid(Id, 1, Width640, sig[..^1]));
    }
}

internal sealed class FixedSigningKey(byte[] key) : IMediaUrlSigningKey
{
    public byte[] GetKey() => key;
}
