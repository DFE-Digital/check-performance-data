using System.Security.Cryptography;
using System.Text;
using DfE.CheckPerformanceData.Application.CurrentUser;
using DfE.CheckPerformanceData.Application.Impersonation;
using DfE.CheckPerformanceData.Infrastructure.Authentication;
using DfE.CheckPerformanceData.Web.Session;
using Microsoft.AspNetCore.DataProtection;

namespace DfE.CheckPerformanceData.Web.Impersonation;

public sealed class ImpersonationSessionService(
    IHttpContextAccessor accessor, ICurrentUserService original,
    IImpersonationSessionStore store, IDataProtectionProvider protection,
    DfE.CheckPerformanceData.Application.CheckYourPupilData.ICheckYourPupilDataRepository pupils) : IEstablishmentViewContext, IImpersonationWriteGuard
{
    private readonly IDataProtector _stamps = protection.CreateProtector("CPD.Impersonation.FormContext.v1");
    public ImpersonationSessionState? State { get; private set; }
    public string? Binding { get; private set; }
    public bool IsImpersonating => State?.Selection is not null;
    public string OrganisationLaestab => State?.Selection?.Laestab ?? original.OrganisationLaestab;
    public string OrganisationUrn => State?.Selection?.Urn ?? original.OrganisationUrn;
    public int? LowestAge => State?.Selection?.LowestAge;
    public int? HighestAge => State?.Selection?.HighestAge;
    public bool IsInitialised => Binding is not null;

    public string? ResolveBinding(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated != true || context.Items[AuthenticatedSessionBinding.ItemKey] is not string authenticated
            || context.Features.Get<Microsoft.AspNetCore.Http.Features.ISessionFeature>()?.Session is null)
            return null;
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(authenticated + ":" + original.UserId + ":" + CpdSessionIdentity.Ensure(context.Session))));
    }

    public async Task InitialiseAsync(string binding, CancellationToken ct)
    {
        Binding = binding;
        State = await store.GetAsync(binding, ct) ?? new(Guid.NewGuid().ToString("N"), null);
        await store.SetAsync(binding, State, ct);
    }

    public async Task SetSelectionAsync(EstablishmentSelection? selection, CancellationToken ct)
    {
        if (Binding is null) throw new ImpersonationWriteDeniedException();
        State = new(Guid.NewGuid().ToString("N"), selection);
        await store.SetAsync(Binding, State, ct);
        var session = accessor.HttpContext!.Session;
        foreach (var key in session.Keys.Where(k => k.StartsWith("request_", StringComparison.Ordinal)
            || k.StartsWith("bulk_", StringComparison.Ordinal) || k.StartsWith("single_", StringComparison.Ordinal)
            || k.StartsWith("SelectedWindow", StringComparison.Ordinal)).ToArray()) session.Remove(key);
    }

    public string? CreateStamp() => Binding is null || State is null ? null
        : _stamps.Protect(string.Join("\n", Binding, State.Generation, original.OrganisationLaestab, original.OrganisationUrn));

    public bool ValidStamp(string? stamp)
    {
        if (Binding is null || State is null || stamp is null) return false;
        try
        {
            return _stamps.Unprotect(stamp) == string.Join("\n", Binding, State.Generation, original.OrganisationLaestab, original.OrganisationUrn);
        }
        catch (CryptographicException) { return false; }
    }

    public async Task EnsureCanWriteAsync(Guid windowId, DfE.CheckPerformanceData.Application.Journey.RequestState? journey = null)
    {
        // Background workers and existing service-only tests have no HTTP impersonation context.
        if (accessor.HttpContext is null) return;
        if (IsImpersonating || (IsInitialised && !accessor.HttpContext.Items.ContainsKey(WriteAuthorisedKey)))
            throw new ImpersonationWriteDeniedException();
        if (journey is null) return;
        if (journey.CheckingWindow?.Id != windowId) throw new ImpersonationWriteDeniedException();
        if (journey.SelectedPupil is { } pupil && journey.SelectedWhatToChange != DfE.CheckPerformanceData.Application.CheckYourPupilData.WhatToChange.Add)
        {
            var owned = await FindOwnedPupilAsync(pupil.Id);
            if (owned is null || owned.Id != pupil.Id) throw new ImpersonationWriteDeniedException();
        }
        if (journey.MatchedPupil is { } matched)
        {
            var owned = await FindOwnedPupilAsync(matched.Id);
            if (owned is null || owned.Id != matched.Id) throw new ImpersonationWriteDeniedException();
        }

        async Task<DfE.CheckPerformanceData.Application.CheckYourPupilData.PupilDto?> FindOwnedPupilAsync(Guid id)
        {
            try { return await pupils.GetPupilAsync(windowId, original.OrganisationLaestab, id); }
            catch (KeyNotFoundException) { return null; }
        }
    }

    public static readonly object WriteAuthorisedKey = new();
}
