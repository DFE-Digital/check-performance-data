using System.Reflection;
using DfE.CheckPerformanceData.Web.Controllers;
using DfE.CheckPerformanceData.Web.Controllers.CheckYourPupilData;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace DfE.CheckPerformanceData.IntegrationTests.PageTree;

// Reflection-based guard. No web host needed — inspects the compiled controller metadata directly.
//
// Verifies three contracts:
//   1. PageController.Show is the ONLY catch-all (wildcard) GET endpoint in the Web assembly.
//   2. That catch-all carries Order = int.MaxValue so the MVC routing engine always evaluates
//      it last, after every other endpoint has had the opportunity to match.
//   3. Known routes (/admin, /help/search, /CheckYourPupilData/{windowId}) are served by
//      their own controllers, confirming that those controllers define explicit route templates
//      that will win over the catch-all in normal operation.
//
// Pattern matches AzureQueueCutoverGuardTests — assembly-level reflection requires no running
// host and no database, making it fast and unconditional.
public sealed class RoutePrecedenceTests
{
    private static readonly Type[] AllControllers = typeof(PageController).Assembly
        .GetTypes()
        .Where(t => typeof(Controller).IsAssignableFrom(t) && !t.IsAbstract)
        .ToArray();

    private static IEnumerable<(MethodInfo Method, HttpGetAttribute Attr)> AllHttpGetActions() =>
        AllControllers
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            .SelectMany(m => m.GetCustomAttributes<HttpGetAttribute>()
                .Select(a => (Method: m, Attr: a)));

    [Fact]
    public void PageController_Show_IsTheOnlyCatchAllGetEndpoint()
    {
        // A wildcard catch-all has an asterisk in its template (e.g. "/{*path}").
        var catchAlls = AllHttpGetActions()
            .Where(x => x.Attr.Template?.Contains('*') == true)
            .ToList();

        Assert.True(catchAlls.Count == 1,
            $"Expected exactly one catch-all GET endpoint but found {catchAlls.Count}: " +
            string.Join(", ", catchAlls.Select(c => $"{c.Method.DeclaringType!.Name}.{c.Method.Name}")));

        Assert.Equal(nameof(PageController), catchAlls[0].Method.DeclaringType!.Name);
        Assert.Equal(nameof(PageController.Show), catchAlls[0].Method.Name);
    }

    [Fact]
    public void PageController_CatchAll_HasMaxIntOrder_SoItNeverShadowsRealRoutes()
    {
        var show = typeof(PageController).GetMethod(nameof(PageController.Show))!;
        var attr = show.GetCustomAttribute<HttpGetAttribute>()!;
        Assert.NotNull(attr);
        Assert.Equal(int.MaxValue, attr.Order);
    }

    // Verifies that each named controller defines at least one route template that starts with
    // the expected path prefix, confirming it owns those URLs explicitly — not via the catch-all.
    [Theory]
    [InlineData(typeof(AdminController),                "admin")]
    [InlineData(typeof(HelpController),                 "help/search")]
    [InlineData(typeof(CheckYourPupilDataController),   "CheckYourPupilData/")]
    public void KnownControllers_HaveExplicitRouteTemplates_UnderTheirOwnPrefix(
        Type controllerType, string expectedPrefix)
    {
        // IRouteTemplateProvider covers [HttpGet], [HttpPost], [Route], etc.
        var templates = controllerType
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .SelectMany(m => m.GetCustomAttributes<Attribute>()
                .OfType<IRouteTemplateProvider>()
                .Select(a => a.Template ?? string.Empty))
            .Concat(
                // Also check class-level route attributes.
                controllerType.GetCustomAttributes<Attribute>()
                    .OfType<IRouteTemplateProvider>()
                    .Select(a => a.Template ?? string.Empty))
            .ToList();

        Assert.True(
            templates.Any(t => t.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase)),
            $"{controllerType.Name} has no route template starting with '{expectedPrefix}'. " +
            $"Found: [{string.Join(", ", templates)}]");
    }
}
