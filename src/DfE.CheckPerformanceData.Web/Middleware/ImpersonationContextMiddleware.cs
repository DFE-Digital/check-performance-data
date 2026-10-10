using DfE.CheckPerformanceData.Application.Audit;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.Impersonation;
using DfE.CheckPerformanceData.Web.Impersonation;
using Microsoft.AspNetCore.Mvc.Controllers;

namespace DfE.CheckPerformanceData.Web.Middleware;

public sealed class ImpersonationContextMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, ImpersonationSessionService session,
        IImpersonationSessionLock gate, IImpersonationAuditWriter audit, ICurrentUserService original)
    {
        var binding = session.ResolveBinding(context);
        if (binding is null) { await next(context); return; }
        // Hold through MVC session commit. Writes and transitions on another replica cannot race.
        await using var lease = await gate.AcquireAsync(binding, context.RequestAborted);
        await session.InitialiseAsync(binding, context.RequestAborted);
        if (session.IsImpersonating) context.Items["CPD.Impersonation.ReadOnly"] = true;
        if (session.IsImpersonating && !ImpersonationAccessPolicy.CanAccess(context.User))
        {
            await audit.RecordAsync(new(original.UserId, session.OrganisationLaestab,
                session.OrganisationUrn, "Exit", DateTime.UtcNow), context.RequestAborted);
            await session.SetSelectionAsync(null, context.RequestAborted);
            await context.Session.CommitAsync(context.RequestAborted);
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        var action = context.GetEndpoint()?.Metadata.GetMetadata<ControllerActionDescriptor>();
        var safeMethod = HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method);
        var exit = action?.ControllerName == "Impersonation" && action.ActionName == "Exit";
        var signOut = action?.ControllerName == "DfeSignOut" && action.ActionName == "Index";
        var asset = action is null && safeMethod && (context.Request.Path.StartsWithSegments("/css")
            || context.Request.Path.StartsWithSegments("/js") || context.Request.Path.StartsWithSegments("/lib")
            || context.Request.Path.StartsWithSegments("/images") || context.Request.Path.StartsWithSegments("/assets")
            || context.Request.Path.StartsWithSegments("/fonts") || context.Request.Path == "/favicon.ico"
            || context.Request.Path == "/govuk-frontend.min.css" || context.Request.Path == "/govuk-frontend.min.js");
        if (session.IsImpersonating && signOut)
        {
            await RecordAsync("Exit");
            await session.SetSelectionAsync(null, context.RequestAborted);
        }

        if (session.IsImpersonating && !exit && !signOut && !asset && (action is null || !ImpersonationEndpointPolicy.CanView(action, context.Request.Method)))
        {
            // Unknown GETs can mutate too; record denied non-read actions without logging input.
            await RecordAsync("BlockedChange");
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            // Render a controlled page using the ordinary layout, never an admin layout.
            await new Microsoft.AspNetCore.Mvc.ViewResult { ViewName = "~/Views/Impersonation/ReadOnly.cshtml" }
                .ExecuteResultAsync(new Microsoft.AspNetCore.Mvc.ActionContext(context,
                    context.GetRouteData(), action ?? new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor()));
            return;
        }

        if (session.IsImpersonating && context.GetRouteData().Values.TryGetValue("windowId", out var routeWindow)
            && Guid.TryParse(routeWindow?.ToString(), out var windowId))
        {
            var repository = context.RequestServices.GetRequiredService<DfE.CheckPerformanceData.Application.LandingPage.ILandingPageRepository>();
            var clock = context.RequestServices.GetRequiredService<TimeProvider>();
            var windows = await repository.GetOpenWindowsAsync(clock.GetLocalNow().DateTime, session.OrganisationLaestab, context.RequestAborted);
            var stages = DfE.CheckPerformanceData.Application.DfESignInApiClient.OrganisationKeyStages.All
                .Where(k => session.LowestAge < k.HighAge && session.HighestAge > k.LowAge).Select(k => k.KeyStage);
            if (!windows.Any(w => w.Id == windowId && w.HasPupilData && stages.Contains(w.KeyStage)))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                await new Microsoft.AspNetCore.Mvc.ViewResult { ViewName = "~/Views/Impersonation/Unavailable.cshtml" }
                    .ExecuteResultAsync(new Microsoft.AspNetCore.Mvc.ActionContext(context, context.GetRouteData(),
                        action ?? new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor()));
                return;
            }
        }

        if (!safeMethod && !exit && !signOut)
        {
            string? stamp = context.Request.Headers["X-Establishment-Context"].FirstOrDefault();
            if (stamp is null && context.Request.HasFormContentType)
                stamp = (await context.Request.ReadFormAsync(context.RequestAborted))["EstablishmentContext"].FirstOrDefault();
            if (!session.ValidStamp(stamp))
            {
                context.Response.StatusCode = StatusCodes.Status409Conflict;
                await context.Response.WriteAsync("The establishment context has changed. Reload the page before making changes.");
                return;
            }
            context.Items[ImpersonationSessionService.WriteAuthorisedKey] = true;
        }

        try { await next(context); }
        catch (ImpersonationWriteDeniedException) when (!context.Response.HasStarted)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsync("The request does not belong to the original authenticated establishment, or the context has changed. Reload the page before making changes.");
        }
        // MVC's session middleware is outside this scope; commit before releasing the lock.
        await context.Session.CommitAsync(context.RequestAborted);

        Task RecordAsync(string operation) => audit.RecordAsync(new(original.UserId,
            session.OrganisationLaestab, session.OrganisationUrn, operation, DateTime.UtcNow), context.RequestAborted);
    }
}
