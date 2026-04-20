using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DfE.CheckPerformanceData.Persistence.Entities.CheckingWindowWorkflow;

// public class CheckingWindowDefinitionConfiguration : IEntityTypeConfiguration<CheckingWindowDefinition>
// {
//     public void Configure(EntityTypeBuilder<CheckingWindowDefinition> builder)
//     {
//         builder.HasKey(x => x.Id);
        
//         builder.Property(x => x.Title).IsRequired().HasMaxLength(200);
        
//         builder.Property(d => d.RequestType)
//             .HasConversion<string>()
//             .IsRequired();
        
//         builder.Property(d => d.WindowType)
//             .HasConversion<string>()
//             .IsRequired();

//         builder.HasMany(d => d.Steps)
//             .WithOne()
//             .HasForeignKey(s => s.CheckingWindowDefinitionId)
//             .OnDelete(DeleteBehavior.Cascade);
//     }
// }



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

// public class AmendmentRequestConfiguration
//     : IEntityTypeConfiguration<AmendmentRequest>
// {
//     public void Configure(EntityTypeBuilder<AmendmentRequest> builder)
//     {
//         builder.HasKey(r => r.Id);

//         builder.Property(r => r.LearnerId)
//             .IsRequired()
//             .HasMaxLength(13);

//         builder.Property(r => r.RequestType)
//             .HasConversion<string>()
//             .IsRequired();

//         builder.Property(r => r.Status)
//             .HasConversion<string>()
//             .IsRequired();

//         builder.HasMany(r => r.StepResponses)
//             .WithOne()
//             .HasForeignKey(s => s.AmendmentRequestId)
//             .OnDelete(DeleteBehavior.Cascade);
//     }
// }

// public class StepResponseConfiguration
//     : IEntityTypeConfiguration<StepResponse>
// {
//     public void Configure(EntityTypeBuilder<StepResponse> builder)
//     {
//         builder.HasKey(s => s.Id);

//         builder.Property(s => s.StepType)
//             .HasConversion<string>()
//             .IsRequired();

//         builder.Property(s => s.ResponseData)
//             .HasColumnType("jsonb")
//             .IsRequired();

//         builder.Property(s => s.CompletedAt)
//             .IsRequired();
//     }
// }
