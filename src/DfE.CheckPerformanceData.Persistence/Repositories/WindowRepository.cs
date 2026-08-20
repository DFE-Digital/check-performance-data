using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using WindowValidated = DfE.CheckPerformanceData.Persistence.Entities.WindowValidated;

namespace DfE.CheckPerformanceData.Persistence.Repositories;

public sealed class WindowRepository(PortalDbContext dbContext) : IWindowRepository
{
    public async Task<List<CheckingWindowDto>> GetAllWindowsAsync(CancellationToken cancellationToken) =>
        await dbContext.CheckingWindows
            .AsNoTracking()
            .Select(w => new CheckingWindowDto
            {
                StartDate = w.StartDate,
                EndDate = w.EndDate,
                KeyStage = w.KeyStage,
                CheckingWindowType = w.CheckingWindowType,
                Title = w.Title,
                TurnaroundCommitment = w.TurnaroundCommitment,
                Id = w.Id,
                IngressFile = w.IngressFile,
                IngressFileChecksum = w.IngressFileChecksum,
                SchemaFile = w.SchemaFile,
                SchemaFileChecksum = w.SchemaFileChecksum,
                Validated = w.Validated != null,
                ValidatedAt = (w.Validated != null ? w.Validated.ValidatedAt : null),
                Datasets = w.Datasets
                    .OrderBy(d => d.SortOrder)
                    .Select(d => new CheckingWindowDatasetDto
                    {
                        Id = d.Id,
                        Name = d.Name,
                        IngressFile = d.IngressFile,
                        IngressFileChecksum = d.IngressFileChecksum,
                        SchemaFile = d.SchemaFile,
                        SchemaFileChecksum = d.SchemaFileChecksum,
                        Included = d.Included,
                        SortOrder = d.SortOrder
                    })
                    .ToList()
            })
            .ToListAsync(cancellationToken);
    
    public async Task<CheckingWindowDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        await dbContext.CheckingWindows
            .AsNoTracking()
            .Where(w => w.Id == id)
            .Select(w => new CheckingWindowDto
            {
                StartDate = w.StartDate,
                EndDate = w.EndDate,
                KeyStage = w.KeyStage,
                CheckingWindowType = w.CheckingWindowType,
                Title = w.Title,
                TurnaroundCommitment = w.TurnaroundCommitment,
                Id = w.Id,
                IngressFile = w.IngressFile,
                IngressFileChecksum = w.IngressFileChecksum,
                SchemaFile = w.SchemaFile,
                SchemaFileChecksum = w.SchemaFileChecksum,
                Validated = w.Validated != null,
                ValidatedAt = (w.Validated != null ? w.Validated.ValidatedAt : null),
                Datasets = w.Datasets
                    .OrderBy(d => d.SortOrder)
                    .Select(d => new CheckingWindowDatasetDto
                    {
                        Id = d.Id,
                        Name = d.Name,
                        IngressFile = d.IngressFile,
                        IngressFileChecksum = d.IngressFileChecksum,
                        SchemaFile = d.SchemaFile,
                        SchemaFileChecksum = d.SchemaFileChecksum,
                        Included = d.Included,
                        SortOrder = d.SortOrder
                    })
                    .ToList()
            })
            .FirstOrDefaultAsync(cancellationToken);

    public async Task UpdateAsync(CheckingWindowDto window, CancellationToken cancellationToken)
    {
        // Loaded and mutated rather than Update(new CheckingWindow{...}) — a detached overwrite
        // would leave the window's dataset rows untracked and strand them.
        CheckingWindow entity = await dbContext.CheckingWindows
            .Include(w => w.Datasets)
            .SingleAsync(w => w.Id == window.Id, cancellationToken);

        dbContext.Entry(entity).CurrentValues.SetValues(new
        {
            window.StartDate,
            window.EndDate,
            window.KeyStage,
            window.CheckingWindowType,
            window.Title,
            window.TurnaroundCommitment,
            window.IngressFile,
            window.IngressFileChecksum,
            window.SchemaFile,
            window.SchemaFileChecksum
        });

        entity.Validated = new WindowValidated { ValidatedAt = window.ValidatedAt ?? DateTime.UtcNow };

        SyncDatasets(entity, window.Datasets);

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    // Datasets are keyed by Name within a window: existing rows are updated in place so their
    // Ids (and any files already uploaded against them) survive, new ones are added, and rows
    // no longer wanted (e.g. after a window type change) are removed.
    private static void SyncDatasets(CheckingWindow entity, List<CheckingWindowDatasetDto> wanted)
    {
        if (wanted.Count == 0)
        {
            return;
        }

        foreach (CheckingWindowDatasetDto dto in wanted)
        {
            CheckingWindowDataset? existing = entity.Datasets.SingleOrDefault(d => d.Name == dto.Name);

            if (existing is null)
            {
                entity.Datasets.Add(new CheckingWindowDataset
                {
                    CheckingWindowId = entity.Id,
                    Name = dto.Name,
                    IngressFile = dto.IngressFile,
                    IngressFileChecksum = dto.IngressFileChecksum,
                    SchemaFile = dto.SchemaFile,
                    SchemaFileChecksum = dto.SchemaFileChecksum,
                    Included = dto.Included,
                    SortOrder = dto.SortOrder
                });
                continue;
            }

            existing.IngressFile = dto.IngressFile;
            existing.IngressFileChecksum = dto.IngressFileChecksum;
            existing.SchemaFile = dto.SchemaFile;
            existing.SchemaFileChecksum = dto.SchemaFileChecksum;
        }

        foreach (CheckingWindowDataset stale in entity.Datasets.Where(d => wanted.All(x => x.Name != d.Name)).ToList())
        {
            entity.Datasets.Remove(stale);
        }
    }

    public async Task<CheckingWindowDto> CreateAsync(CheckingWindowDto window, CancellationToken cancellationToken)
    {
        var entity = new CheckingWindow
        {
            StartDate = window.StartDate,
            EndDate = window.EndDate,
            KeyStage = window.KeyStage,
            CheckingWindowType = window.CheckingWindowType,
            Title = window.Title,
            TurnaroundCommitment = window.TurnaroundCommitment,
            IngressFile = window.IngressFile,
            IngressFileChecksum = window.IngressFileChecksum,
            SchemaFile = window.SchemaFile,
            SchemaFileChecksum = window.SchemaFileChecksum,
            Validated = new WindowValidated() { ValidatedAt = window.ValidatedAt ?? DateTime.UtcNow },
            // A window is born with the dataset slots its type requires.
            Datasets = (window.Datasets.Count > 0
                    ? window.Datasets
                    : WindowDatasets.DefaultsFor(window.CheckingWindowType).ToList())
                .Select(d => new CheckingWindowDataset
                {
                    Name = d.Name,
                    IngressFile = d.IngressFile,
                    IngressFileChecksum = d.IngressFileChecksum,
                    SchemaFile = d.SchemaFile,
                    SchemaFileChecksum = d.SchemaFileChecksum,
                    Included = d.Included,
                    SortOrder = d.SortOrder
                })
                .ToList()
        };

        await dbContext.CheckingWindows.AddAsync(entity, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new CheckingWindowDto
        {
            Id = entity.Id,
            StartDate = entity.StartDate,
            EndDate = entity.EndDate,
            KeyStage = entity.KeyStage,
            CheckingWindowType = entity.CheckingWindowType,
            Title = entity.Title,
            TurnaroundCommitment = entity.TurnaroundCommitment,
            IngressFile = entity.IngressFile,
            IngressFileChecksum = entity.IngressFileChecksum,
            SchemaFile = entity.SchemaFile,
            SchemaFileChecksum = entity.SchemaFileChecksum,
            Validated = entity.Validated != null,
            ValidatedAt = entity.Validated?.ValidatedAt
        };
    }
}