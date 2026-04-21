using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DfE.CheckPerformanceData.Persistence.Entities.CheckingWindowWorkflow;

public class AmendmentRequestStepResponse
{
    public Guid Id { get; init; }
    public Guid AmendmentRequestId { get; init; }
    public CheckingWindowStepType StepType { get; init; }
    public int StepIndex { get; init; }
    public JsonDocument ResponseData { get; init; } = JsonDocument.Parse("{}");
    public DateTime CompletedAt { get; set; }
}

public class AmendmentRequestStepResponseConfiguration : IEntityTypeConfiguration<AmendmentRequestStepResponse>
{
    public void Configure(EntityTypeBuilder<AmendmentRequestStepResponse> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.StepType)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.ResponseData)
            .HasColumnType("jsonb")
            .IsRequired()
            .HasConversion(
                v => v.RootElement.GetRawText(),
                v => JsonDocument.Parse(v));

        builder.HasIndex(x => x.AmendmentRequestId);
    }
}