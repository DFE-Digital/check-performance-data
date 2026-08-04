using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DfE.CheckPerformanceData.Persistence.Entities;

/// <summary>
/// One successful DfE Sign-In sign-in by a school/organisation user, stored as organisation
/// data only (no user id — dropped on data-minimisation grounds). Appended by the
/// OnTokenValidated hook after claims enrichment succeeds; read only by the admin dashboard's
/// engagement metrics. Append-only — dedup happens at query time.
/// </summary>
public sealed class OrganisationLogin
{
    public Guid Id { get; init; }

    public required long OrganisationUrn { get; init; }

    /// <summary>
    /// Digits-only laestab ("933/4070" → "9334070") so it joins directly against the
    /// laestabs derived from pupil-blob names, which strip the slash the same way.
    /// </summary>
    public required string Laestab { get; init; }

    public required string OrganisationName { get; init; }

    public required DateTime LoggedInAtUtc { get; init; }
}

public sealed class OrganisationLoginConfiguration : IEntityTypeConfiguration<OrganisationLogin>
{
    public void Configure(EntityTypeBuilder<OrganisationLogin> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(x => x.Laestab).IsRequired().HasMaxLength(20);
        builder.Property(x => x.OrganisationName).IsRequired().HasMaxLength(500);

        // The dashboard queries by time range and cross-references by URN.
        builder.HasIndex(x => x.LoggedInAtUtc);
        builder.HasIndex(x => new { x.OrganisationUrn, x.LoggedInAtUtc });
    }
}
