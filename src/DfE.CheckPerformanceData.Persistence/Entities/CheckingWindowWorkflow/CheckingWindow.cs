using DfE.CheckPerformanceData.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DfE.CheckPerformanceData.Persistence.Entities.CheckingWindowWorkflow;

public sealed class CheckingWindow
{
    public Guid Id { get; init; }
    public DateOnly StartDate { get; init; }
    public DateOnly EndDate { get; init; }
    public KeyStages KeyStage { get; init; }
    public string Title { get; init; } = string.Empty;
    public ICollection<CheckingWindowStep> CheckingWindowRequestSteps {get;init;} = [];
}

public class CheckingWindowConfiguration : IEntityTypeConfiguration<CheckingWindow>
{
    public void Configure(EntityTypeBuilder<CheckingWindow> builder)
    {
        builder.HasKey(w => w.Id);

        builder.Property(w => w.Title)
            .IsRequired()
            .HasMaxLength(200);
    }
}