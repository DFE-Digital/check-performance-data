using System.Security.Claims;
using Dfe.Analytics.AspNetCore;
using Dfe.Analytics.Events;
using DfE.CheckPerformanceData.Web.Analytics;
using Microsoft.AspNetCore.Http;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Analytics;

public sealed class OrganisationEventEnricherTests
{
    private static async Task<Event> EnrichAsync(params Claim[] claims)
    {
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "test"))
        };
        var @event = new Event { EventType = "web_request", Environment = "test" };

        await new OrganisationEventEnricher()
            .EnrichEventAsync(new EnrichWebRequestEventContext(@event, httpContext));

        return @event;
    }

    [Fact]
    public async Task Adds_organisation_urn_and_name_from_claims()
    {
        // user_id is added automatically by the middleware (UserIdClaimType =
        // NameIdentifier); this enricher adds the school's URN and name.
        var ev = await EnrichAsync(
            new Claim("organisation_urn", "123456"),
            new Claim("organisation_name", "Test School"));

        Assert.Equal(["123456"], ev.Data["organisation_urn"]);
        Assert.Equal(["Test School"], ev.Data["organisation_name"]);
    }

    [Fact]
    public async Task Omits_organisation_fields_when_claims_absent()
    {
        // Unauthenticated requests (e.g. sign-in pages) carry no org claims — don't
        // stamp empty values.
        var ev = await EnrichAsync();

        Assert.DoesNotContain("organisation_urn", ev.Data.Keys);
        Assert.DoesNotContain("organisation_name", ev.Data.Keys);
    }
}
