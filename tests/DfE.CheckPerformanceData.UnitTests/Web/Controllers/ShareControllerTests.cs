using System.Reflection;
using DfE.CheckPerformanceData.Application.Observability;
using DfE.CheckPerformanceData.Web.Controllers;
using DfE.CheckPerformanceData.Web.Models.Observability;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Controllers;

// The share link and the wallboard are the "show without admin" surfaces: reachable without the
// cypmd_admin cookie (so [AllowAnonymous] at the class level) but gated by an opaque revocable
// token in the URL path. A valid token returns the aggregate-only view (200); a missing, invalid
// or revoked token returns 404 — never 401, never a redirect, never an OIDC challenge, so a
// viewer is never bounced into the sign-in flow and the surface's existence and the auth topology
// stay hidden. The view model is the aggregate-only AggregateShareViewModel, never the
// authenticated DashboardViewModel, and the surface carries no admin/mutating actions.
public sealed class ShareControllerTests
{
    private static AggregateShareSnapshot EmptyAggregate() =>
        new(
            Depths: Array.Empty<QueueDepthSnapshot>(),
            DecisionMix: Array.Empty<DecisionMixEntry>(),
            Throughput: Array.Empty<ThroughputBucket>(),
            Dwell: Array.Empty<StageDwell>(),
            ProcessedToday: 0,
            TypicalEndToEnd: TimeSpan.Zero,
            OverallHealth: new HealthState(HealthLevel.Flowing, "#00703c", "Flowing", "circle"),
            CapturedAtUtc: DateTime.UtcNow);

    private static IMetricsQueryService QueryReturningEmptyAggregate()
    {
        var query = Substitute.For<IMetricsQueryService>();
        query.GetAggregateShareAsync(Arg.Any<CancellationToken>()).Returns(EmptyAggregate());
        return query;
    }

    private static IShareTokenService TokenService(bool valid)
    {
        var tokens = Substitute.For<IShareTokenService>();
        tokens.ValidateAsync(Arg.Any<string>(), Arg.Any<ShareSurface>(), Arg.Any<CancellationToken>())
            .Returns(valid);
        return tokens;
    }

    // === Controllers under test, paired with their expected surface ===

    public static IEnumerable<object[]> Controllers =>
        new[]
        {
            new object[] { typeof(ShareController) },
            new object[] { typeof(WallboardController) },
        };

    // --- Both controllers are [AllowAnonymous] at the class level (the show-without-admin surface) ---

    [Theory]
    [MemberData(nameof(Controllers))]
    public void Controller_IsAllowAnonymous_AtClassLevel(Type controllerType)
    {
        var allowAnonymous = controllerType.GetCustomAttribute<AllowAnonymousAttribute>();
        Assert.NotNull(allowAnonymous);
    }

    // --- Every public GET action takes a {token} route value (no token-less anonymous route) ---

    [Theory]
    [MemberData(nameof(Controllers))]
    public void EveryPublicAction_TakesATokenParameter(Type controllerType)
    {
        var actions = controllerType
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName && typeof(Task<IActionResult>).IsAssignableFrom(m.ReturnType));

        Assert.NotEmpty(actions);
        foreach (var action in actions)
        {
            Assert.Contains(action.GetParameters(), p =>
                string.Equals(p.Name, "token", StringComparison.OrdinalIgnoreCase));
        }
    }

    // --- Read-only: no action carries a mutating HTTP verb (POST/PUT/DELETE/PATCH) ---

    [Theory]
    [MemberData(nameof(Controllers))]
    public void NoAction_CarriesAMutatingVerb(Type controllerType)
    {
        var mutating = new[]
        {
            typeof(Microsoft.AspNetCore.Mvc.HttpPostAttribute),
            typeof(Microsoft.AspNetCore.Mvc.HttpPutAttribute),
            typeof(Microsoft.AspNetCore.Mvc.HttpDeleteAttribute),
            typeof(Microsoft.AspNetCore.Mvc.HttpPatchAttribute),
        };

        foreach (var action in controllerType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            foreach (var verb in mutating)
            {
                Assert.Null(action.GetCustomAttribute(verb));
            }
        }
    }

    // --- A valid token yields a 200 view carrying the aggregate-only view model ---

    [Fact]
    public async Task Share_ValidToken_Returns200_WithAggregateShareViewModel()
    {
        var controller = new ShareController(TokenService(valid: true), QueryReturningEmptyAggregate());

        var result = await controller.Index("a-valid-token");

        var view = Assert.IsType<ViewResult>(result);
        Assert.IsType<AggregateShareViewModel>(view.Model);
    }

    [Fact]
    public async Task Wallboard_ValidToken_Returns200_WithAggregateShareViewModel()
    {
        var controller = new WallboardController(TokenService(valid: true), QueryReturningEmptyAggregate());

        var result = await controller.Index("a-valid-token");

        var view = Assert.IsType<ViewResult>(result);
        Assert.IsType<AggregateShareViewModel>(view.Model);
    }

    // --- A missing / invalid / revoked token yields 404 — never 401, redirect or challenge ---

    [Theory]
    [InlineData("")]
    [InlineData("invalid-or-revoked-token")]
    public async Task Share_MissingOrInvalidOrRevokedToken_Returns404_NotChallenge(string token)
    {
        var controller = new ShareController(TokenService(valid: false), QueryReturningEmptyAggregate());

        var result = await controller.Index(token);

        AssertIsNotFoundAndNotAuthChallenge(result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid-or-revoked-token")]
    public async Task Wallboard_MissingOrInvalidOrRevokedToken_Returns404_NotChallenge(string token)
    {
        var controller = new WallboardController(TokenService(valid: false), QueryReturningEmptyAggregate());

        var result = await controller.Index(token);

        AssertIsNotFoundAndNotAuthChallenge(result);
    }

    // --- A valid token never reaches a pupil-bearing query path: only the aggregate query is hit ---

    [Fact]
    public async Task Share_ValidToken_OnlyCallsTheAggregateQuery()
    {
        var query = QueryReturningEmptyAggregate();
        var controller = new ShareController(TokenService(valid: true), query);

        await controller.Index("a-valid-token");

        await query.Received(1).GetAggregateShareAsync(Arg.Any<CancellationToken>());
        // The journey / per-message detail reads carry pupil references and must never be touched.
        await query.DidNotReceive().GetJourneyAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Share_InvalidToken_NeverQueriesAggregate()
    {
        var query = QueryReturningEmptyAggregate();
        var controller = new ShareController(TokenService(valid: false), query);

        await controller.Index("nope");

        await query.DidNotReceive().GetAggregateShareAsync(Arg.Any<CancellationToken>());
    }

    private static void AssertIsNotFoundAndNotAuthChallenge(IActionResult result)
    {
        Assert.IsType<NotFoundResult>(result);
        Assert.IsNotType<UnauthorizedResult>(result);
        Assert.IsNotType<ChallengeResult>(result);
        Assert.IsNotType<RedirectResult>(result);
        Assert.IsNotType<RedirectToActionResult>(result);
        Assert.IsNotType<ForbidResult>(result);
    }
}
