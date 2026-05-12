using System;
using System.Collections.Generic;
using Xunit;
using DfE.CheckPerformanceData.Application.ZendeskClient;

namespace DfE.CheckPerformanceData.UnitTests.ZendeskClient;

/// <summary>
/// Unit tests for AttachmentDto class.
/// </summary>
public class AttachmentDtoTests
{
    [Fact]
    public void AttachmentDto_DefaultConstructor_ShouldInitializeProperties()
    {
        // Arrange & Act
        var dto = new AttachmentDto();

        // Assert
        Assert.Equal(string.Empty, dto.Url);
        Assert.Equal(0L, dto.Id);
        Assert.Equal(string.Empty, dto.FileName);
        Assert.Null(dto.ContentUrl);
        Assert.Null(dto.MappedContentUrl);
        Assert.Null(dto.ContentType);
        Assert.Equal(0L, dto.Size);
        Assert.Null(dto.Width);
        Assert.Null(dto.Height);
        Assert.False(dto.Inline);
        Assert.False(dto.Deleted);
        Assert.False(dto.MalwareAccessOverride);
        Assert.Null(dto.MalwareScanResult);
        Assert.Null(dto.Thumbnails);
    }

    [Fact]
    public void AttachmentDto_WithAllProperties_ShouldSetAllProperties()
    {
        // Arrange & Act
        var dto = new AttachmentDto
        {
            Url = "https://example.com/file.pdf",
            Id = 123L,
            FileName = "test.pdf",
            ContentUrl = "https://example.com/content/file.pdf",
            MappedContentUrl = "https://example.com/mapped/file.pdf",
            ContentType = "application/pdf",
            Size = 1024L,
            Width = 800,
            Height = 600,
            Inline = true,
            Deleted = false,
            MalwareAccessOverride = true,
            MalwareScanResult = "clean",
            Thumbnails = new List<AttachmentThumbnailDto>
            {
                new() { Id = 1L, FileName = "thumbnail1.jpg" }
            }
        };

        // Assert
        Assert.Equal("https://example.com/file.pdf", dto.Url);
        Assert.Equal(123L, dto.Id);
        Assert.Equal("test.pdf", dto.FileName);
        Assert.Equal("https://example.com/content/file.pdf", dto.ContentUrl);
        Assert.Equal("https://example.com/mapped/file.pdf", dto.MappedContentUrl);
        Assert.Equal("application/pdf", dto.ContentType);
        Assert.Equal(1024L, dto.Size);
        Assert.Equal(800, dto.Width);
        Assert.Equal(600, dto.Height);
        Assert.True(dto.Inline);
        Assert.False(dto.Deleted);
        Assert.True(dto.MalwareAccessOverride);
        Assert.Equal("clean", dto.MalwareScanResult);
        Assert.Single(dto.Thumbnails);
    }

    [Fact]
    public void AttachmentDto_WithZeroId_ShouldAllowZero()
    {
        // Arrange & Act
        var dto = new AttachmentDto { Id = 0L };

        // Assert
        Assert.Equal(0L, dto.Id);
    }

    [Fact]
    public void AttachmentDto_WithNegativeId_ShouldAllowNegative()
    {
        // Arrange & Act
        var dto = new AttachmentDto { Id = -1L };

        // Assert
        Assert.Equal(-1L, dto.Id);
    }

    [Fact]
    public void AttachmentDto_WithMaxId_ShouldAllowMaxValue()
    {
        // Arrange & Act
        var dto = new AttachmentDto { Id = long.MaxValue };

        // Assert
        Assert.Equal(long.MaxValue, dto.Id);
    }

    [Fact]
    public void AttachmentDto_WithZeroSize_ShouldAllowZero()
    {
        // Arrange & Act
        var dto = new AttachmentDto { Size = 0L };

        // Assert
        Assert.Equal(0L, dto.Size);
    }

    [Fact]
    public void AttachmentDto_WithNegativeSize_ShouldAllowNegative()
    {
        // Arrange & Act
        var dto = new AttachmentDto { Size = -1L };

        // Assert
        Assert.Equal(-1L, dto.Size);
    }

    [Fact]
    public void AttachmentDto_WithMaxSize_ShouldAllowMaxValue()
    {
        // Arrange & Act
        var dto = new AttachmentDto { Size = long.MaxValue };

        // Assert
        Assert.Equal(long.MaxValue, dto.Size);
    }

    [Fact]
    public void AttachmentDto_WithZeroWidth_ShouldAllowZero()
    {
        // Arrange & Act
        var dto = new AttachmentDto { Width = 0 };

        // Assert
        Assert.Equal(0, dto.Width);
    }

    [Fact]
    public void AttachmentDto_WithNegativeWidth_ShouldAllowNegative()
    {
        // Arrange & Act
        var dto = new AttachmentDto { Width = -1 };

        // Assert
        Assert.Equal(-1, dto.Width);
    }

    [Fact]
    public void AttachmentDto_WithMaxWidth_ShouldAllowMaxValue()
    {
        // Arrange & Act
        var dto = new AttachmentDto { Width = int.MaxValue };

        // Assert
        Assert.Equal(int.MaxValue, dto.Width);
    }

    [Fact]
    public void AttachmentDto_WithZeroHeight_ShouldAllowZero()
    {
        // Arrange & Act
        var dto = new AttachmentDto { Height = 0 };

        // Assert
        Assert.Equal(0, dto.Height);
    }

    [Fact]
    public void AttachmentDto_WithNegativeHeight_ShouldAllowNegative()
    {
        // Arrange & Act
        var dto = new AttachmentDto { Height = -1 };

        // Assert
        Assert.Equal(-1, dto.Height);
    }

    [Fact]
    public void AttachmentDto_WithMaxHeight_ShouldAllowMaxValue()
    {
        // Arrange & Act
        var dto = new AttachmentDto { Height = int.MaxValue };

        // Assert
        Assert.Equal(int.MaxValue, dto.Height);
    }

    [Fact]
    public void AttachmentDto_WithEmptyUrl_ShouldSetEmptyString()
    {
        // Arrange & Act
        var dto = new AttachmentDto { Url = string.Empty };

        // Assert
        Assert.Equal(string.Empty, dto.Url);
    }

    [Fact]
    public void AttachmentDto_WithNullUrl_ShouldAllowNull()
    {
        // Arrange & Act
        var dto = new AttachmentDto { Url = null };

        // Assert
        Assert.Null(dto.Url);
    }

    [Fact]
    public void AttachmentDto_WithEmptyFileName_ShouldSetEmptyString()
    {
        // Arrange & Act
        var dto = new AttachmentDto { FileName = string.Empty };

        // Assert
        Assert.Equal(string.Empty, dto.FileName);
    }

    [Fact]
    public void AttachmentDto_WithNullFileName_ShouldAllowNull()
    {
        // Arrange & Act
        var dto = new AttachmentDto { FileName = null };

        // Assert
        Assert.Null(dto.FileName);
    }

    [Fact]
    public void AttachmentDto_WithEmptyContentType_ShouldSetEmptyString()
    {
        // Arrange & Act
        var dto = new AttachmentDto { ContentType = string.Empty };

        // Assert
        Assert.Equal(string.Empty, dto.ContentType);
    }

    [Fact]
    public void AttachmentDto_WithNullContentType_ShouldAllowNull()
    {
        // Arrange & Act
        var dto = new AttachmentDto { ContentType = null };

        // Assert
        Assert.Null(dto.ContentType);
    }

    [Fact]
    public void AttachmentDto_WithEmptyMalwareScanResult_ShouldSetEmptyString()
    {
        // Arrange & Act
        var dto = new AttachmentDto { MalwareScanResult = string.Empty };

        // Assert
        Assert.Equal(string.Empty, dto.MalwareScanResult);
    }

    [Fact]
    public void AttachmentDto_WithNullMalwareScanResult_ShouldAllowNull()
    {
        // Arrange & Act
        var dto = new AttachmentDto { MalwareScanResult = null };

        // Assert
        Assert.Null(dto.MalwareScanResult);
    }

    [Fact]
    public void AttachmentDto_WithEmptyContentUrl_ShouldSetEmptyString()
    {
        // Arrange & Act
        var dto = new AttachmentDto { ContentUrl = string.Empty };

        // Assert
        Assert.Equal(string.Empty, dto.ContentUrl);
    }

    [Fact]
    public void AttachmentDto_WithNullContentUrl_ShouldAllowNull()
    {
        // Arrange & Act
        var dto = new AttachmentDto { ContentUrl = null };

        // Assert
        Assert.Null(dto.ContentUrl);
    }

    [Fact]
    public void AttachmentDto_WithEmptyMappedContentUrl_ShouldSetEmptyString()
    {
        // Arrange & Act
        var dto = new AttachmentDto { MappedContentUrl = string.Empty };

        // Assert
        Assert.Equal(string.Empty, dto.MappedContentUrl);
    }

    [Fact]
    public void AttachmentDto_WithNullMappedContentUrl_ShouldAllowNull()
    {
        // Arrange & Act
        var dto = new AttachmentDto { MappedContentUrl = null };

        // Assert
        Assert.Null(dto.MappedContentUrl);
    }

    [Fact]
    public void AttachmentDto_WithNullAttachments_ShouldAllowNull()
    {
        // Arrange & Act
        var dto = new AttachmentDto { Thumbnails = null };

        // Assert
        Assert.Null(dto.Thumbnails);
    }

    [Fact]
    public void AttachmentDto_WithEmptyAttachments_ShouldInitializeEmptyList()
    {
        // Arrange & Act
        var dto = new AttachmentDto { Thumbnails = new List<AttachmentThumbnailDto>() };

        // Assert
        Assert.Empty(dto.Thumbnails);
    }

    [Fact]
    public void AttachmentDto_WithMultiplePropertyAssignments_ShouldUpdateValues()
    {
        // Arrange
        var dto = new AttachmentDto
        {
            Url = "https://example.com/file1.pdf",
            Id = 1L,
            FileName = "file1.pdf"
        };

        // Assert initial state
        Assert.Equal("https://example.com/file1.pdf", dto.Url);
        Assert.Equal(1L, dto.Id);
        Assert.Equal("file1.pdf", dto.FileName);

        // Update with object initializer
        dto = new AttachmentDto
        {
            Url = "https://example.com/file2.pdf",
            Id = 2L,
            FileName = "file2.pdf",
            ContentType = "application/pdf"
        };

        // Assert updated values
        Assert.Equal("https://example.com/file2.pdf", dto.Url);
        Assert.Equal(2L, dto.Id);
        Assert.Equal("file2.pdf", dto.FileName);
        Assert.Equal("application/pdf", dto.ContentType);
    }
}