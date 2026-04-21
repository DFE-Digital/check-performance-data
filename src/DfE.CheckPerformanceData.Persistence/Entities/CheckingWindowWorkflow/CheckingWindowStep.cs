using DfE.CheckPerformanceData.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DfE.CheckPerformanceData.Persistence.Entities.CheckingWindowWorkflow;

public class CheckingWindowStep
{
    public Guid Id { get; init; }
    public Guid CheckingWindowId { get; init; }
    public RequestTypes RequestType {get; init;} // Add, Include, Remove
    public CheckingWindowStepType StepType {get; init;} // Date, Upload, More Details etc
    public int Order { get; init; } 
    public bool IsRequired {get; init;}
    public string Title { get; init; } = string.Empty;
    public string Explanation { get; init; } = string.Empty;
}

public class CheckingWindowStepConfiguration
    : IEntityTypeConfiguration<CheckingWindowStep>
{
    public void Configure(EntityTypeBuilder<CheckingWindowStep> builder)
    {
        builder.HasKey(s => s.Id);

        builder.Property(s => s.StepType)
            .HasConversion<string>()
            .IsRequired();

        builder.Property(s => s.RequestType)
            .HasConversion<string>()
            .IsRequired();

        builder.HasIndex(s => new { s.CheckingWindowId, s.RequestType, s.Order })
            .IsUnique();
    }
}