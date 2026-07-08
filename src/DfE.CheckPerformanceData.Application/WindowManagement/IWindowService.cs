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
    public string TitleLink = "/admin/windows/title";
    public DateTime? StartDate { get; set; }
    public string StartDateLink  = "/admin/windows/start-date";
    public DateTime? EndDate { get; set; }
    public string EndDateLink  = "/admin/windows/end-date";
    public CheckingWindowType? CheckingWindowType { get; set; }
    public string CheckingWindowTypeLink  = "/admin/windows/window-type";
    public KeyStages? KeyStage { get; set; }
    public string KeyStageLink  = "/admin/windows/key-stage";

    public bool IsValid
    {
        get
        {
            // if (IsEmpty || StartDate < DateTime.UtcNow || EndDate < StartDate)
            //     return false;

            return true;
        }
    }
    
    public bool IsEmpty => 
        Title == null && !StartDate.HasValue && !EndDate.HasValue && !CheckingWindowType.HasValue && !KeyStage.HasValue;

    public string NextController() => (
                Title is null,
                !StartDate.HasValue, 
                !EndDate.HasValue, 
                !CheckingWindowType.HasValue, 
                !KeyStage.HasValue   
            ) switch
        {
            (true, _, _, _, _) => "Title",
            (false, true, _, _, _) => "StartDate",
            (false, _, true, _, _) => "EndDate",
            (false, _, _, true, _) => "WindowType",
            (false, _, _, _, true) => "KeyStage",
            _ => "CreateCheckingWindow"
        };
}
