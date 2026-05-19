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
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(x => x.PupilFirstname)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(x => x.PupilSurname)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(x => x.Submitted)
            .IsRequired()
            .HasColumnType("timestamp without time zone");

        builder.Property(x => x.SubmittedById)
            .IsRequired();

        builder.Property(x => x.SubmittedByName)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(x => x.Status)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(x => x.ReferenceNumber)
            .IsRequired()
            .HasMaxLength(50);

        builder.HasOne<CheckingWindow>()
            .WithMany()
            .HasForeignKey(x => x.WindowId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.ReferenceNumber)
            .IsUnique();

        builder.HasIndex(x => new { x.WindowId, x.OrganisationUrn });

        builder.HasIndex(x => x.Status);
    }
}
