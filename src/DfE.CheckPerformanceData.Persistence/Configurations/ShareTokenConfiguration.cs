using DfE.CheckPerformance.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DfE.CheckPerformanceData.Persistence.Configurations;

internal sealed class ShareTokenConfiguration : IEntityTypeConfiguration<ShareToken>
{
    public void Configure(EntityTypeBuilder<ShareToken> builder)
    {
        builder.ToTable("share_tokens");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id");

        builder.Property(x => x.TokenHash)
            .HasColumnName("token_hash")
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(x => x.Label)
            .HasColumnName("label")
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(x => x.Surface)
            .HasColumnName("surface")
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(x => x.CreatedBy)
            .HasColumnName("created_by")
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(x => x.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .HasColumnType("timestamp with time zone");

        builder.Property(x => x.RevokedAtUtc)
            .HasColumnName("revoked_at_utc")
            .HasColumnType("timestamp with time zone");

        // The hash is the lookup key on every validation; a named index keeps the constant-time
        // candidate fetch a single index seek rather than a scan.
        builder.HasIndex(x => x.TokenHash)
            .HasDatabaseName("ix_share_tokens_token_hash");
    }
}
