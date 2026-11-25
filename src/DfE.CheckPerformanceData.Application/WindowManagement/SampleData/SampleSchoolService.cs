namespace DfE.CheckPerformanceData.Application.WindowManagement.SampleData;

/// <summary>
/// #937: reads and changes the list of the schools in a window that see sample data. The
/// caller's permission is the route's concern. This service checks the values again, so a
/// request that does not come from the form cannot store a bad value.
/// </summary>
public interface ISampleSchoolService
{
    Task<IReadOnlyList<SampleSchoolDto>> ListAsync(Guid windowId, CancellationToken cancellationToken);

    /// <summary>The school with that id in that window, or null.</summary>
    Task<SampleSchoolDto?> FindAsync(Guid windowId, Guid sampleSchoolId, CancellationToken cancellationToken);

    /// <summary>
    /// Checks the values and that the window does not list the school yet. Writes nothing. The
    /// add form calls this before it shows the confirmation page.
    /// </summary>
    Task<SampleSchoolAddResult> CheckAddAsync(Guid windowId, string? laestab, string? urn, CancellationToken cancellationToken);

    Task<SampleSchoolAddResult> AddAsync(Guid windowId, string? laestab, string? urn, string addedBy, CancellationToken cancellationToken);

    /// <summary>False when the window does not list that school.</summary>
    Task<bool> RemoveAsync(Guid windowId, Guid sampleSchoolId, CancellationToken cancellationToken);
}

public enum SampleSchoolAddStatus
{
    /// <summary>The values are valid and the window does not list the school (for an add, it now does).</summary>
    Ok,

    /// <summary>No such window.</summary>
    WindowNotFound,

    /// <summary>The LAESTAB or the URN is not valid. <see cref="SampleSchoolAddResult.Input"/> has the errors.</summary>
    Invalid,

    /// <summary>The window already lists the school.</summary>
    AlreadyListed
}

/// <summary>The status, and the checked input with its errors.</summary>
public sealed record SampleSchoolAddResult(SampleSchoolAddStatus Status, SampleSchoolInputResult Input);

public sealed class SampleSchoolService(ISampleSchoolRepository repository, TimeProvider clock) : ISampleSchoolService
{
    public Task<IReadOnlyList<SampleSchoolDto>> ListAsync(Guid windowId, CancellationToken cancellationToken) =>
        repository.ListAsync(windowId, cancellationToken);

    public async Task<SampleSchoolDto?> FindAsync(Guid windowId, Guid sampleSchoolId, CancellationToken cancellationToken) =>
        (await repository.ListAsync(windowId, cancellationToken)).SingleOrDefault(s => s.Id == sampleSchoolId);

    public async Task<SampleSchoolAddResult> CheckAddAsync(Guid windowId, string? laestab, string? urn,
        CancellationToken cancellationToken)
    {
        var input = SampleSchoolInput.Check(laestab, urn);
        if (!await repository.WindowExistsAsync(windowId, cancellationToken))
            return new SampleSchoolAddResult(SampleSchoolAddStatus.WindowNotFound, input);
        if (!input.IsValid)
            return new SampleSchoolAddResult(SampleSchoolAddStatus.Invalid, input);

        var listed = await repository.ListAsync(windowId, cancellationToken);
        return listed.Any(s => s.Laestab == input.Laestab)
            ? new SampleSchoolAddResult(SampleSchoolAddStatus.AlreadyListed, input)
            : new SampleSchoolAddResult(SampleSchoolAddStatus.Ok, input);
    }

    public async Task<SampleSchoolAddResult> AddAsync(Guid windowId, string? laestab, string? urn, string addedBy,
        CancellationToken cancellationToken)
    {
        var check = await CheckAddAsync(windowId, laestab, urn, cancellationToken);
        if (check.Status != SampleSchoolAddStatus.Ok)
            return check;

        var added = await repository.AddAsync(new NewSampleSchool(windowId, check.Input.Laestab!, check.Input.Urn!,
            clock.GetUtcNow().UtcDateTime, addedBy), cancellationToken);
        return added ? check : check with { Status = SampleSchoolAddStatus.AlreadyListed };
    }

    public Task<bool> RemoveAsync(Guid windowId, Guid sampleSchoolId, CancellationToken cancellationToken) =>
        repository.RemoveAsync(windowId, sampleSchoolId, cancellationToken);
}
