using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.WindowManagement;

public interface IWindowService
{
    Task<PageResult?> GetAllDataAsync(CancellationToken cancellationToken);
    Task<CheckingWindowDto> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task UpdateAsync(CheckingWindowDto window, CancellationToken cancellationToken);
    Task<CheckingWindowDto> CreateAsync(CheckingWindowDto window, CancellationToken cancellationToken);
}

public class PageResult
{
    public required List<CheckingWindowDto> Windows { get; set; }
}

public sealed class CheckingWindowDto
{
    public Guid Id { get; init; }
    public required string Title { get; set; }
    public required DateTime EndDate { get; set; }
    public required KeyStages KeyStage { get; set; }
    public required CheckingWindowType CheckingWindowType { get; set; }
    public bool HasPupilData { get; init; }
    public required DateTime StartDate { get; set; }
    public string IngressFile { get; set; } = string.Empty;
    public string IngressFileChecksum { get; set; } = string.Empty;
    public string SchemaFile { get; set; } = string.Empty;
    public string SchemaFileChecksum { get; set; } = string.Empty;
    public bool Validated { get; set; }
    public DateTime? ValidatedAt { get; set; }
}

public class CheckingWindowDraft
{
    public string PostUrl { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string TitleLink { get; init; } = string.Empty;
    public DateTime? StartDate { get; set; }
    public string StartDateLink { get; init; } = string.Empty;
    public DateTime? EndDate { get; set; }
    public string EndDateLink { get; init; } = string.Empty;
    public CheckingWindowType? CheckingWindowType { get; set; }
    public string CheckingWindowTypeLink { get; init; } = string.Empty;

    public bool IsValid
    {
        get
        {
            if (Title == null || !StartDate.HasValue || !EndDate.HasValue || !CheckingWindowType.HasValue)   
                return false;
            
            if (StartDate < DateTime.UtcNow || EndDate < StartDate)
                return false;

            return true;
        }
    }
}
