using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DfE.CheckPerformanceData.Persistence.Entities.CheckingWindowWorkflow;

public class AmendmentRequest
{
    public Guid Id { get; init; }
    public Guid CheckingWindowId { get; init; }
    public string OrganisationUrn { get; init; } = string.Empty;
    public Guid OrganisationId { get; init; }
    public int CurrentStepIndex  { get; set; }
    public AmendmentStatus Status { get; set; }
    public ICollection<AmendmentRequestStepResponse> StepResponses { get; init; } = new List<AmendmentRequestStepResponse>();
}

public class AmendmentRequestConfiguration : IEntityTypeConfiguration<AmendmentRequest>
{
    public void Configure(EntityTypeBuilder<AmendmentRequest> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrganisationUrn)
            .HasMaxLength(20)
            .IsRequired();
        
        builder.Property(x => x.OrganisationId)
            .IsRequired();

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.HasMany(x => x.StepResponses)
            .WithOne()
            .HasForeignKey(x => x.AmendmentRequestId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}