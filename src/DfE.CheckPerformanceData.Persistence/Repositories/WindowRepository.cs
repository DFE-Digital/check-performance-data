using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Persistence.Contexts;
using DfE.CheckPerformanceData.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using WindowValidated = DfE.CheckPerformanceData.Persistence.Entities.WindowValidated;

namespace DfE.CheckPerformanceData.Persistence.Repositories;

public sealed class WindowRepository(PortalDbContext dbContext) : IWindowRepository
{
    public async Task<List<CheckingWindowDto>> GetAllWindowsAsync(DateTime now, CancellationToken cancellationToken) =>
        await dbContext.CheckingWindows
            .AsNoTracking()
            .Where(w => w.StartDate <= now && w.EndDate >= now)
            .Select(w => new CheckingWindowDto
            {
                StartDate = w.StartDate,
                EndDate = w.EndDate,
                KeyStage = w.KeyStage,
                CheckingWindowType = w.CheckingWindowType,
                Title = w.Title,
                Id = w.Id,
                IngressFile = w.IngressFile,
                IngressFileChecksum = w.IngressFileChecksum,
                SchemaFile = w.SchemaFile,
                SchemaFileChecksum = w.SchemaFileChecksum,
                Validated = w.Validated != null,
                ValidatedAt = (w.Validated != null ? w.Validated.ValidatedAt : null)
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
                Id = w.Id,
                IngressFile = w.IngressFile,
                IngressFileChecksum = w.IngressFileChecksum,
                SchemaFile = w.SchemaFile,
                SchemaFileChecksum = w.SchemaFileChecksum,
                Validated = w.Validated != null,
                ValidatedAt = (w.Validated != null ? w.Validated.ValidatedAt : null)
            })
            .FirstOrDefaultAsync(cancellationToken);

    public Task UpdateAsync(CheckingWindowDto window, CancellationToken cancellationToken)
    {
        dbContext.CheckingWindows.Update(new CheckingWindow
        {
            Id = window.Id,
            StartDate = window.StartDate,
            EndDate = window.EndDate,
            KeyStage = window.KeyStage,
            CheckingWindowType = window.CheckingWindowType,
            Title = window.Title,
            IngressFile = window.IngressFile,
            IngressFileChecksum = window.IngressFileChecksum,
            SchemaFile = window.SchemaFile,
            SchemaFileChecksum = window.SchemaFileChecksum,
            Validated =new WindowValidated() { ValidatedAt = window.ValidatedAt ?? DateTime.UtcNow }
        });
        return dbContext.SaveChangesAsync(cancellationToken);
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
            IngressFile = window.IngressFile,
            IngressFileChecksum = window.IngressFileChecksum,
            SchemaFile = window.SchemaFile,
            SchemaFileChecksum = window.SchemaFileChecksum,
            Validated = new WindowValidated() { ValidatedAt = window.ValidatedAt ?? DateTime.UtcNow }
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
            IngressFile = entity.IngressFile,
            IngressFileChecksum = entity.IngressFileChecksum,
            SchemaFile = entity.SchemaFile,
            SchemaFileChecksum = entity.SchemaFileChecksum,
            Validated = entity.Validated != null,
            ValidatedAt = entity.Validated?.ValidatedAt
        };
    }
}