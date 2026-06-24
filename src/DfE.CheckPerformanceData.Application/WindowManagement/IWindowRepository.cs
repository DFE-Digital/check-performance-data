using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.WindowManagement;

public interface IWindowRepository
{
    Task<List<CheckingWindowDto>> GetAllWindowsAsync(DateTime now, CancellationToken cancellationToken);
    Task<CheckingWindowDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task UpdateAsync(CheckingWindowDto window, CancellationToken cancellationToken);
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