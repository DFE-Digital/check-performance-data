using System.Text.RegularExpressions;
using DfE.CheckPerformanceData.Application;
using DfE.CheckPerformanceData.Application.ContentBlocks;
using DfE.CheckPerformanceData.Domain.Entities;
using Ganss.Xss;
using Markdig;
using Microsoft.EntityFrameworkCore;

namespace DfE.CheckPerformanceData.Infrastructure.ContentBlocks;

public partial class ContentBlockService(IPortalDbContext context) : IContentBlockService
{
    private static readonly MarkdownPipeline MarkdownPipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    private static readonly HtmlSanitizer Sanitizer = new();

    public async Task<ContentBlockDto?> GetByKeyAsync(string key)
    {
        var block = await context.ContentBlocks
            .FirstOrDefaultAsync(b => b.Key == key);

        return block == null ? null : ToDto(block);
    }

    public async Task<ContentBlockDto> SaveAsync(SaveContentBlockDto dto)
    {
        var block = await context.ContentBlocks
            .FirstOrDefaultAsync(b => b.Key == dto.Key);

        if (block == null)
        {
            block = new ContentBlock
            {
                Key = dto.Key,
                BlockType = dto.BlockType,
                Value = dto.Value,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            context.ContentBlocks.Add(block);
            await context.SaveChangesAsync();

            context.ContentBlockVersions.Add(new ContentBlockVersion
            {
                ContentBlockId = block.Id,
                Value = dto.Value,
                VersionNumber = 1,
                CreatedAt = DateTime.UtcNow
            });
        }
        else
        {
            block.Value = dto.Value;
            block.UpdatedAt = DateTime.UtcNow;

            var maxVersion = await context.ContentBlockVersions
                .Where(v => v.ContentBlockId == block.Id)
                .MaxAsync(v => (int?)v.VersionNumber) ?? 0;

            context.ContentBlockVersions.Add(new ContentBlockVersion
            {
                ContentBlockId = block.Id,
                Value = dto.Value,
                VersionNumber = maxVersion + 1,
                CreatedAt = DateTime.UtcNow
            });
        }

        await context.SaveChangesAsync();
        return ToDto(block);
    }

    public async Task<List<ContentBlockVersionDto>> GetVersionsAsync(string key)
    {
        return await context.ContentBlockVersions
            .Where(v => v.ContentBlock.Key == key)
            .OrderByDescending(v => v.VersionNumber)
            .Select(v => new ContentBlockVersionDto
            {
                Id = v.Id,
                VersionNumber = v.VersionNumber,
                Value = v.Value,
                CreatedAt = v.CreatedAt,
                CreatedBy = v.CreatedBy
            })
            .ToListAsync();
    }

    public async Task<ContentBlockDto> RevertToVersionAsync(string key, int versionId)
    {
        var block = await context.ContentBlocks
            .FirstOrDefaultAsync(b => b.Key == key)
            ?? throw new InvalidOperationException($"Content block '{key}' not found.");

        var version = await context.ContentBlockVersions.FindAsync(versionId)
            ?? throw new InvalidOperationException($"Content block version {versionId} not found.");

        block.Value = version.Value;
        block.UpdatedAt = DateTime.UtcNow;

        var maxVersion = await context.ContentBlockVersions
            .Where(v => v.ContentBlockId == block.Id)
            .MaxAsync(v => v.VersionNumber);

        context.ContentBlockVersions.Add(new ContentBlockVersion
        {
            ContentBlockId = block.Id,
            Value = version.Value,
            VersionNumber = maxVersion + 1,
            CreatedAt = DateTime.UtcNow
        });

        await context.SaveChangesAsync();
        return ToDto(block);
    }

    private static ContentBlockDto ToDto(ContentBlock block) => new()
    {
        Id = block.Id,
        Key = block.Key,
        BlockType = block.BlockType,
        Value = block.Value,
        ValueHtml = block.BlockType == "Content" ? RenderHtml(block.Value) : null
    };

    private static string? RenderHtml(string? content)
    {
        if (string.IsNullOrEmpty(content)) return null;

        var html = ContainsHtmlTags().IsMatch(content)
            ? content
            : Markdown.ToHtml(content, MarkdownPipeline);

        return Sanitizer.Sanitize(html);
    }

    [GeneratedRegex(@"<\s*[a-z][a-z0-9]*[\s>]", RegexOptions.IgnoreCase)]
    private static partial Regex ContainsHtmlTags();
}
