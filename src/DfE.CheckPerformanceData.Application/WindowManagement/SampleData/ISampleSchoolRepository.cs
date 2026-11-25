namespace DfE.CheckPerformanceData.Application.WindowManagement.SampleData;

/// <summary>#937: the stored list of the schools in a window that see sample data.</summary>
public interface ISampleSchoolRepository
{
    /// <summary>The window's sample schools, in LAESTAB order. Empty for an unknown window.</summary>
    Task<IReadOnlyList<SampleSchoolDto>> ListAsync(Guid windowId, CancellationToken cancellationToken);

    Task<bool> WindowExistsAsync(Guid windowId, CancellationToken cancellationToken);

    /// <summary>
    /// Adds the school. False when the window already lists it: the unique index decides, so two
    /// admins who add the same school at the same time cannot both write.
    /// </summary>
    Task<bool> AddAsync(NewSampleSchool school, CancellationToken cancellationToken);

    /// <summary>Removes the school from that window. False when the window does not list it.</summary>
    Task<bool> RemoveAsync(Guid windowId, Guid sampleSchoolId, CancellationToken cancellationToken);
}

/// <summary>One sample school of a window. <see cref="Laestab"/> is digits only.</summary>
public sealed record SampleSchoolDto(Guid Id, Guid WindowId, string Laestab, string Urn, DateTime AddedAt, string AddedBy);

/// <summary>A school to add. The values are already checked and in their stored form.</summary>
public sealed record NewSampleSchool(Guid WindowId, string Laestab, string Urn, DateTime AddedAt, string AddedBy);
