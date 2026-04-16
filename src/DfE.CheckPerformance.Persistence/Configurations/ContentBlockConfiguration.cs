using DfE.CheckPerformance.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DfE.CheckPerformance.Persistence.Configurations;

internal sealed class ContentBlockConfiguration : IEntityTypeConfiguration<ContentBlock>
{
    public void Configure(EntityTypeBuilder<ContentBlock> builder)
    {
        builder
            .HasIndex(b => b.Key)
            .IsUnique();
    }
}
