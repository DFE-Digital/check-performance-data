using DfE.CheckPerformanceData.Application.Admin;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace DfE.CheckPerformanceData.Web.Authorization;

// Route-level gate: the current user must hold a role that has been granted
// <see cref="SectionKey"/> in the role-settings grid. Meant to be applied AFTER a plain
// [Authorize(...)] so the user is at least authenticated before we ask the policy.
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class RequireAdminSectionAttribute(string sectionKey) : Attribute, IAsyncAuthorizationFilter
{
    public string SectionKey { get; } = sectionKey;

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var policy = context.HttpContext.RequestServices.GetRequiredService<IAdminAccessPolicy>();
        var allowed = await policy.CanAccessAsync(context.HttpContext.User, SectionKey);
        if (!allowed)
        {
            context.Result = new ForbidResult();
        }
    }
}
