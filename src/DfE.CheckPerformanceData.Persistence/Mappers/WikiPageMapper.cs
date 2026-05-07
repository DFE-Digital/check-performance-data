using DfE.CheckPerformance.Persistence.Entities;
using DfE.CheckPerformanceData.Application.Wiki;
using DfE.CheckPerformanceData.Persistence.Entities;
using Riok.Mapperly.Abstractions;

namespace DfE.CheckPerformanceData.Persistence.Mappers;

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.None)]
internal static partial class WikiPageMapper
{
    // IQueryable projections — translated to SQL by EF Core
    // Unmapped source/target members are automatically ignored for queryable projections

    [MapperRequiredMapping(RequiredMappingStrategy.None)]
    public static partial IQueryable<WikiPageDto> ProjectToDto(this IQueryable<WikiPage> query);

    [MapperRequiredMapping(RequiredMappingStrategy.None)]
    public static partial IQueryable<WikiPageVersionDto> ProjectToVersionDto(this IQueryable<WikiPageVersion> query);

    // In-memory mapping — for entities already loaded (e.g. after Add/SaveChanges)

    [MapperIgnoreSource(nameof(WikiPage.Parent))]
    [MapperIgnoreSource(nameof(WikiPage.Children))]
    [MapperIgnoreSource(nameof(WikiPage.Versions))]
    [MapperIgnoreSource(nameof(WikiPage.IsDeleted))]
    [MapperIgnoreSource(nameof(WikiPage.DeletedAt))]
    [MapperIgnoreSource(nameof(WikiPage.DeletedBy))]
    [MapperIgnoreSource(nameof(WikiPage.CreatedAt))]
    [MapperIgnoreSource(nameof(WikiPage.CreatedBy))]
    [MapperIgnoreSource(nameof(WikiPage.UpdatedAt))]
    [MapperIgnoreSource(nameof(WikiPage.UpdatedBy))]
    [MapperIgnoreSource(nameof(WikiPage.BodyPlainText))]
    [MapperIgnoreSource(nameof(WikiPage.SearchVector))]
    [MapperIgnoreTarget(nameof(WikiPageDto.SlugPath))]
    [MapperIgnoreTarget(nameof(WikiPageDto.ContentHtml))]
    public static partial WikiPageDto ToDto(WikiPage entity);
}
