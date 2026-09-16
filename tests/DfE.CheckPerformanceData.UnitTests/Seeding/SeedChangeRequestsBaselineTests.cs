using DfE.CheckPerformanceData.Web.Seeding;

namespace DfE.CheckPerformanceData.Application.UnitTests.Seeding;

// The seeded reference numbers are the baseline the dev reset protects, so the seeder and the
// reset have to agree on what they are. They agree by construction — one list, used both to
// write the rows and to decide what survives — and these pin the properties that construction
// relies on, because a drift between the two is exactly the class of bug the reset exists to
// fix: silent, and only visible on the second run against an environment.
public class SeedChangeRequestsBaselineTests
{
    [Fact]
    public void Baseline_CoversEverySeededRequest()
    {
        Assert.Equal(SeedChangeRequests.SeededRequestCount, SeedChangeRequests.SeededReferenceNumbers.Count);
    }

    // The references are compared as whole strings, so their exact text matters. Pinning the ends
    // of the range catches a renumbering or a format change that would otherwise leave the reset
    // quietly deleting the fixtures it is supposed to keep.
    [Theory]
    [InlineData("CYPMD_KS4June_SEED001")]
    [InlineData("CYPMD_KS4June_SEED011")]
    public void Baseline_CarriesTheReferencesTheSeederWrites(string reference)
    {
        Assert.Contains(reference, SeedChangeRequests.SeededReferenceNumbers);
    }

    // Case matters: the comparison is ordinal, so a reference differing only in case is a
    // different request and must not be mistaken for a fixture.
    [Fact]
    public void Baseline_MatchesOrdinally()
    {
        Assert.DoesNotContain("cypmd_ks4june_seed001", SeedChangeRequests.SeededReferenceNumbers);
    }

    [Fact]
    public void Baseline_HasNoDuplicates()
    {
        Assert.Equal(
            SeedChangeRequests.SeededReferenceNumbers.Count,
            SeedChangeRequests.SeededReferenceNumbers.Distinct().Count());
    }
}
