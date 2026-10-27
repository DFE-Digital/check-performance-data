using System.Reflection;
using DfE.CheckPerformanceData.Web.Controllers;
using DfE.CheckPerformanceData.Web.Controllers.Egress;
using DfE.CheckPerformanceData.Web.Controllers.WindowAdmin;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web;

// A progress stream that starts a run must be a POST with an antiforgery token. The validate
// streams once ran ingest on a plain GET and made the new release live, so a crafted link opened by
// a signed-in admin published data. The auth cookie is SameSite=Lax, which a top-level GET carries.
public sealed class StreamRouteAntiforgeryTests
{
    // Read-only streams that may stay GET. Add a stream here only if it changes nothing.
    private static readonly HashSet<MethodInfo> ReadOnlyStreams =
    [
        typeof(ObservabilityController).GetMethod(nameof(ObservabilityController.Stream))!,
    ];

    public static TheoryData<string> RunStreams =>
    [
        nameof(ValidateWindowController.Stream),
        nameof(ValidateWindowController.StreamForExercise),
    ];

    [Theory]
    [MemberData(nameof(RunStreams))]
    public void Validate_stream_is_a_post_with_antiforgery(string action) =>
        AssertPostWithAntiforgery(typeof(ValidateWindowController).GetMethod(action)!);

    [Fact]
    public void Egress_preprocessing_stream_is_a_post_with_antiforgery() =>
        AssertPostWithAntiforgery(typeof(EgressController).GetMethod(nameof(EgressController.PreprocessingStream))!);

    [Fact]
    public void No_other_stream_route_is_a_get()
    {
        var getStreams = typeof(AdminController).Assembly
            .GetTypes()
            .Where(t => typeof(Controller).IsAssignableFrom(t) && !t.IsAbstract)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(m => !ReadOnlyStreams.Contains(m))
            .Where(m => m.GetCustomAttributes<HttpGetAttribute>()
                .Any(a => a.Template?.EndsWith("/stream", StringComparison.OrdinalIgnoreCase) == true))
            .Select(m => $"{m.DeclaringType!.Name}.{m.Name}")
            .ToList();

        Assert.True(getStreams.Count == 0,
            "GET stream routes not listed as read-only: " + string.Join(", ", getStreams));
    }

    private static void AssertPostWithAntiforgery(MethodInfo action)
    {
        Assert.Empty(action.GetCustomAttributes<HttpGetAttribute>());
        Assert.NotNull(action.GetCustomAttribute<HttpPostAttribute>());
        Assert.NotNull(action.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
    }
}
