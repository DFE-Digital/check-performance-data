using DfE.CheckPerformanceData.Application.Search;

namespace DfE.CheckPerformanceData.Application.UnitTests.Search;

public class ScopePageIdsTests
{
    private const string A = "00000000-cd94-4a01-8f01-000000000003";
    private const string B = "00000000-cd94-4a01-8f01-000000000004";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" , not-an-id ,guidance/post-16")]
    public void Parse_NoIds_IsEmpty(string? raw)
    {
        Assert.Empty(ScopePageIds.Parse(raw));
        Assert.Null(ScopePageIds.Normalise(raw));
        Assert.Null(ScopePageIds.ToPageTokens(raw));
    }

    [Fact]
    public void Normalise_KeepsDistinctIdsInOrder_InTheirStandardForm()
    {
        Assert.Equal($"{B},{A}", ScopePageIds.Normalise($" {{{B.ToUpperInvariant()}}} ,junk,{A},{B}"));
    }

    [Fact]
    public void ToPageTokens_GivesEachPagesToken_InOrder()
    {
        Assert.Equal(
            $"{PageToken.For(new Guid(B))},{PageToken.For(new Guid(A))}",
            ScopePageIds.ToPageTokens($"{B},{A}"));
    }
}
