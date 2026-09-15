using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DfE.CheckPerformanceData.Persistence.Entities;

/// <summary>
/// A processed new-learner record — the spec-conformant version, never the raw Zendesk shape
/// (AB#292610 "Data storage in CYPMD"). Written only when the whole batch preprocessed cleanly;
/// the transfer reads the file rows from here, not from the pulled payload.
/// </summary>
public sealed class EgressNewLearner
{
    public Guid Id { get; set; }
    public Guid RunId { get; set; }
    public Guid ChangeRequestId { get; set; }
    public long? TicketId { get; set; }
    public string ReferenceNumber { get; set; } = string.Empty;
    public string CorrectionId { get; set; } = string.Empty;
    public string CorrectionType { get; set; } = string.Empty;
    public string KeyStage { get; set; } = string.Empty;
    public string LocalAuthority { get; set; } = string.Empty;
    public string EstablishmentNumber { get; set; } = string.Empty;
    public string Surname { get; set; } = string.Empty;
    public string MiddleName { get; set; } = string.Empty;
    public string Forename { get; set; } = string.Empty;
    public string Sex { get; set; } = string.Empty;
    public string DateOfBirth { get; set; } = string.Empty;
    public string AdmissionDate { get; set; } = string.Empty;
    public string Postcode { get; set; } = string.Empty;
    public string CycleYear { get; set; } = string.Empty;
    public string CycleMonth { get; set; } = string.Empty;
    public string SchoolUrn { get; set; } = string.Empty;
    public string Uln { get; set; } = string.Empty;
    public string Upn { get; set; } = string.Empty;
    public string LearnerId { get; set; } = string.Empty;
    public string YearGroup { get; set; } = string.Empty;
    public string SenStatus { get; set; } = string.Empty;
}

/// <summary>A processed remove-learner record; same rules as <see cref="EgressNewLearner"/>.</summary>
public sealed class EgressRemoveLearner
{
    public Guid Id { get; set; }
    public Guid RunId { get; set; }
    public Guid ChangeRequestId { get; set; }
    public long? TicketId { get; set; }
    public string ReferenceNumber { get; set; } = string.Empty;
    public string CorrectionId { get; set; } = string.Empty;
    public string CorrectionType { get; set; } = string.Empty;
    public string CorrectionReason { get; set; } = string.Empty;
    public string KeyStage { get; set; } = string.Empty;
    public string EstablishmentNumber { get; set; } = string.Empty;
    public string Surname { get; set; } = string.Empty;
    public string Forename { get; set; } = string.Empty;
    public string Sex { get; set; } = string.Empty;
    public string DateOfBirth { get; set; } = string.Empty;
    public string CycleYear { get; set; } = string.Empty;
    public string CycleMonth { get; set; } = string.Empty;
    public string LocalAuthority { get; set; } = string.Empty;
    public string LearnerId { get; set; } = string.Empty;
}

public sealed class EgressNewLearnerConfiguration : IEntityTypeConfiguration<EgressNewLearner>
{
    public void Configure(EntityTypeBuilder<EgressNewLearner> builder)
    {
        builder.ToTable("new_learners");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ReferenceNumber).IsRequired().HasMaxLength(50);
        foreach (var name in new[] { nameof(EgressNewLearner.CorrectionId), nameof(EgressNewLearner.CorrectionType), nameof(EgressNewLearner.KeyStage),
                     nameof(EgressNewLearner.LocalAuthority), nameof(EgressNewLearner.EstablishmentNumber), nameof(EgressNewLearner.Surname),
                     nameof(EgressNewLearner.MiddleName), nameof(EgressNewLearner.Forename), nameof(EgressNewLearner.Sex), nameof(EgressNewLearner.DateOfBirth),
                     nameof(EgressNewLearner.AdmissionDate), nameof(EgressNewLearner.Postcode), nameof(EgressNewLearner.CycleYear), nameof(EgressNewLearner.CycleMonth),
                     nameof(EgressNewLearner.SchoolUrn), nameof(EgressNewLearner.Uln), nameof(EgressNewLearner.Upn), nameof(EgressNewLearner.LearnerId),
                     nameof(EgressNewLearner.YearGroup), nameof(EgressNewLearner.SenStatus) })
        {
            builder.Property<string>(name).IsRequired().HasMaxLength(200);
        }
        builder.HasOne<EgressRun>().WithMany().HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => x.RunId);
    }
}

public sealed class EgressRemoveLearnerConfiguration : IEntityTypeConfiguration<EgressRemoveLearner>
{
    public void Configure(EntityTypeBuilder<EgressRemoveLearner> builder)
    {
        builder.ToTable("remove_learners");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ReferenceNumber).IsRequired().HasMaxLength(50);
        foreach (var name in new[] { nameof(EgressRemoveLearner.CorrectionId), nameof(EgressRemoveLearner.CorrectionType), nameof(EgressRemoveLearner.CorrectionReason),
                     nameof(EgressRemoveLearner.KeyStage), nameof(EgressRemoveLearner.EstablishmentNumber), nameof(EgressRemoveLearner.Surname),
                     nameof(EgressRemoveLearner.Forename), nameof(EgressRemoveLearner.Sex), nameof(EgressRemoveLearner.DateOfBirth), nameof(EgressRemoveLearner.CycleYear),
                     nameof(EgressRemoveLearner.CycleMonth), nameof(EgressRemoveLearner.LocalAuthority), nameof(EgressRemoveLearner.LearnerId) })
        {
            builder.Property<string>(name).IsRequired().HasMaxLength(200);
        }
        builder.HasOne<EgressRun>().WithMany().HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => x.RunId);
    }
}
