using DfE.CheckPerformanceData.Application.ContentBlocks;
using DfE.CheckPerformanceData.Persistence.Entities;
using Riok.Mapperly.Abstractions;

namespace DfE.CheckPerformanceData.Persistence.Mappers;

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.None)]
internal static partial class ContentBlockMapper
{
    // IQueryable projections — translated to SQL by EF Core

    [MapperRequiredMapping(RequiredMappingStrategy.None)]
    public static partial IQueryable<ContentBlockDto> ProjectToDto(this IQueryable<ContentBlock> query);

    [MapperRequiredMapping(RequiredMappingStrategy.None)]
    public static partial IQueryable<ContentBlockVersionDto> ProjectToVersionDto(this IQueryable<ContentBlockVersion> query);

    // In-memory mapping — for entities already loaded (e.g. after Add/SaveChanges)

    [MapperIgnoreSource(nameof(ContentBlock.CreatedAt))]
    [MapperIgnoreSource(nameof(ContentBlock.CreatedBy))]
    [MapperIgnoreSource(nameof(ContentBlock.UpdatedAt))]
    [MapperIgnoreSource(nameof(ContentBlock.UpdatedBy))]
    [MapperIgnoreSource(nameof(ContentBlock.Versions))]
    [MapperIgnoreTarget(nameof(ContentBlockDto.ValueHtml))]
    public static partial ContentBlockDto ToDto(ContentBlock entity);
}
