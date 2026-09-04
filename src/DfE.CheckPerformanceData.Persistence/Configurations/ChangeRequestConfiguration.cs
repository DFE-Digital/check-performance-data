using DfE.CheckPerformanceData.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DfE.CheckPerformanceData.Persistence.Configurations;

internal sealed class ChangeRequestConfiguration : IEntityTypeConfiguration<ChangeRequest>
{
    public void Configure(EntityTypeBuilder<ChangeRequest> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.WindowId)
            .IsRequired();

        builder.Property(x => x.OrganisationUrn)
            .IsRequired();

        builder.Property(x => x.PupilUpn)
            .HasMaxLength(50);

        builder.Property(x => x.PupilFirstname)
            .HasMaxLength(100);

        builder.Property(x => x.PupilSurname)
            .HasMaxLength(100);

        builder.Property(x => x.Submitted)
            .IsRequired()
            .HasColumnType("timestamp without time zone");

        builder.Property(x => x.SubmittedById)
            .IsRequired();

        builder.Property(x => x.SubmittedByName)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(x => x.SubmittedByEmail)
            .HasMaxLength(256);

        builder.Property(x => x.Status)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(x => x.ReferenceNumber)
            .IsRequired()
            .HasMaxLength(50);
        
        builder.Property(x => x.CrmId)
            .HasMaxLength(100);

        builder.Property(x => x.RequestType)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(x => x.RequestTypeDescription)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(x => x.AmendmentType)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(x => x.Outcome)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(x => x.OutcomeKey)
            .HasMaxLength(100);

        builder.Property(x => x.MatchedRuleId)
            .HasMaxLength(100);

        builder.Property(x => x.RulesVersion)
            .HasMaxLength(100);

        // Deliberately unbounded, unlike its neighbours. The engine caps the trace at
        // MaxTraceLines lines but not at any line width - a free-text answer is rendered
        // verbatim into a leaf line - so a length cap here would throw on write. The bound
        // is applied at the write site instead (RulesConsumer.TruncateTrace).
        builder.Property(x => x.DecisionTrace);

        builder.HasOne<CheckingWindow>()
            .WithMany()
            .HasForeignKey(x => x.WindowId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.ReferenceNumber)
            .IsUnique();

        // The durable idempotency guard for the "ticket created" transition: a CRM id can
        // only ever be recorded once, so two concurrent or redelivered messages for the same
        // request cannot both write a (different) Zendesk ticket id. Partial so the many rows
        // that have not yet been ticketed (CrmId null) are not forced unique.
        builder.HasIndex(x => x.CrmId)
            .IsUnique()
            .HasFilter("\"CrmId\" IS NOT NULL");

        // SetNull rather than Restrict: the admin wizard deletes the CheckingExercises rows an
        // admin unticks (WindowRepository.SyncExercises), and Restrict would turn that into an
        // unhandled DbUpdateException mid-wizard. Nothing irrecoverable is lost - AmendmentType
        // still derives the exercise type - and an exercise that no longer exists is not one an
        // admin can filter by or ask the open/closed question about.
        builder.HasOne<CheckingExercise>()
            .WithMany()
            .HasForeignKey(x => x.CheckingExerciseId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(x => new { x.WindowId, x.OrganisationUrn });

        // Grouping and filtering the admin request lists by exercise is the reason the column
        // exists, so the lookup it serves gets its own index.
        builder.HasIndex(x => x.CheckingExerciseId);

        builder.HasIndex(x => x.Status);
        
        builder.Property(x => x.WorkerStatus)
            .HasConversion<string>();
    }
}
