using System.Reflection;
using DfE.CheckPerformanceData.Web.Admin;
using DfE.CheckPerformanceData.Web.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace DfE.CheckPerformanceData.Application.UnitTests.Admin;

// Every controller that serves a URL under /admin must carry a role or section gate on the
// class. The global FallbackPolicy only demands a signed-in user, and every school user is
// signed in, so an ungated admin controller is open to every school in the country. The whole
// window-administration wizard shipped that way once: the landing page, summary, every wizard
// step, file upload, validate and close-exercise were reachable by any DfE Sign-in account.
public sealed class AdminRouteGateTests
{
    private static readonly Type[] AdminRoutedControllers = typeof(AdminController).Assembly
        .GetTypes()
        .Where(t => typeof(Controller).IsAssignableFrom(t) && !t.IsAbstract)
        .Where(HasAdminRoute)
        .OrderBy(t => t.FullName)
        .ToArray();

    [Fact]
    public void Every_Admin_Routed_Controller_Is_Gated_At_Class_Level()
    {
        Assert.NotEmpty(AdminRoutedControllers);

        var ungated = AdminRoutedControllers
            .Where(t => !IsGated(t))
            .Select(t => t.Name)
            .ToList();

        Assert.True(ungated.Count == 0,
            "Admin-routed controllers with no [RequireAdminSection] or [Authorize(Roles)] on the class: "
            + string.Join(", ", ungated));
    }

    // Class-level only: an action-level gate leaves every sibling action open, and the wizard
    // controllers have a GET and a POST per step.
    private static bool IsGated(Type controller)
    {
        if (controller.GetCustomAttribute<RequireAdminSectionAttribute>(inherit: true) is not null)
            return true;

        var authorize = controller.GetCustomAttribute<AuthorizeAttribute>(inherit: true);
        return authorize is not null && !string.IsNullOrWhiteSpace(authorize.Roles);
    }

    private static bool HasAdminRoute(Type controller)
    {
        var classTemplates = controller.GetCustomAttributes<RouteAttribute>(inherit: true)
            .Select(r => r.Template);
        var actionTemplates = controller
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .SelectMany(m => m.GetCustomAttributes(inherit: true))
            .Select(a => a switch
            {
                HttpMethodAttribute h => h.Template,
                RouteAttribute r => r.Template,
                _ => null,
            });

        return classTemplates.Concat(actionTemplates).Any(IsAdminTemplate);
    }

    private static bool IsAdminTemplate(string? template)
    {
        if (template is null) return false;
        var t = template.TrimStart('/');
        return t.Equals("admin", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("admin/", StringComparison.OrdinalIgnoreCase);
    }
}
