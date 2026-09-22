using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.WindowManagement;

public interface IWindowRepository
{
    Task<List<CheckingWindowDto>> GetAllWindowsAsync(CancellationToken cancellationToken);
    Task<CheckingWindowDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task UpdateAsync(CheckingWindowDto window, CancellationToken cancellationToken);
    Task<CheckingWindowDto> CreateAsync(CheckingWindowDto window, CancellationToken cancellationToken);

    /// <summary>Whether any change request is stamped with this exercise (#466). WindowService
    /// refuses a remove when so — the FK is SetNull, so nothing in the database would — because a
    /// request must never lose the exercise it was made under.</summary>
    Task<bool> HasChangeRequestsAsync(Guid exerciseId, CancellationToken cancellationToken);
}

public class WindowDto
{
    public Guid Id { get; init; }
    public required string Title { get; init; }
    public required DateTime EndDate { get; init; }
    public required KeyStages KeyStage { get; init; }
    public required CheckingWindowType CheckingWindowType { get; init; }
    public bool HasPupilData { get; init; }
    public required DateTime StartDate { get; init; }
}