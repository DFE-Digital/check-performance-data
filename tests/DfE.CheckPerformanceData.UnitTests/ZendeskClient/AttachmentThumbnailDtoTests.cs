using System;
using System.Collections.Generic;
using Xunit;
using DfE.CheckPerformanceData.Application.ZendeskClient;

namespace DfE.CheckPerformanceData.UnitTests.ZendeskClient;

/// <summary>
/// Unit tests for AttachmentThumbnailDto class.
/// </summary>
public class AttachmentThumbnailDtoTests
{
    [Fact]
    public void AttachmentThumbnailDto_DefaultConstructor_ShouldInitializeProperties()
    {
        // Arrange & Act
        var dto = new AttachmentThumbnailDto();

        // Assert
        Assert.Equal(string.Empty, dto.Url);
        Assert.Equal(0L, dto.Id);
        Assert.Equal(string.Empty, dto.FileName);
        Assert.Null(dto.ContentUrl);
        Assert.Null(dto.MappedContentUrl);
        Assert.Equal(string.Empty, dto.ContentType);
        Assert.Equal(0L, dto.Size);
        Assert.Null(dto.Width);
        Assert.Null(dto.Height);
        Assert.False(dto.Inline);
        Assert.False(dto.Deleted);
    }

    [Fact]
    public void AttachmentThumbnailDto_WithAllProperties_ShouldSetAllProperties()
    {
        // Arrange & Act
        var dto = new AttachmentThumbnailDto
        {
            Url = "https://example.com/thumbnail.jpg",
            Id = 456L,
            FileName = "thumbnail.jpg",
            ContentUrl = "https://example.com/content/thumbnail.jpg",
            MappedContentUrl = "https://example.com/mapped/thumbnail.jpg",
            ContentType = "image/jpeg",
            Size = 512L,
            Width = 200,
            Height = 150,
            Inline = false,
            Deleted = false
        };

        // Assert
        Assert.Equal("https://example.com/thumbnail.jpg", dto.Url);
        Assert.Equal(456L, dto.Id);
        Assert.Equal("thumbnail.jpg", dto.FileName);
        Assert.Equal("https://example.com/content/thumbnail.jpg", dto.ContentUrl);
        Assert.Equal("https://example.com/mapped/thumbnail.jpg", dto.MappedContentUrl);
        Assert.Equal("image/jpeg", dto.ContentType);
        Assert.Equal(512L, dto.Size);
        Assert.Equal(200, dto.Width);
        Assert.Equal(150, dto.Height);
        Assert.False(dto.Inline);
        Assert.False(dto.Deleted);
    }

    [Fact]
    public void AttachmentThumbnailDto_WithZeroId_ShouldAllowZero()
    {
        // Arrange & Act
        var dto = new AttachmentThumbnailDto { Id = 0L };

        // Assert
        Assert.Equal(0L, dto.Id);
    }

    [Fact]
    public void AttachmentThumbnailDto_WithNegativeId_ShouldAllowNegative()
    {
        // Arrange & Act
        var dto = new AttachmentThumbnailDto { Id = -1L };

        // Assert
        Assert.Equal(-1L, dto.Id);
    }

    [Fact]
    public void AttachmentThumbnailDto_WithMaxId_ShouldAllowMaxValue()
    {
        // Arrange & Act
        var dto = new AttachmentThumbnailDto { Id = long.MaxValue };

        // Assert
        Assert.Equal(long.MaxValue, dto.Id);
    }

    [Fact]
    public void AttachmentThumbnailDto_WithZeroSize_ShouldAllowZero()
    {
        // Arrange & Act
        var dto = new AttachmentThumbnailDto { Size = 0L };

        // Assert
        Assert.Equal(0L, dto.Size);
    }

    [Fact]
    public void AttachmentThumbnailDto_WithNegativeSize_ShouldAllowNegative()
    {
        // Arrange & Act
        var dto = new AttachmentThumbnailDto { Size = -1L };

        // Assert
        Assert.Equal(-1L, dto.Size);
    }

    [Fact]
    public void AttachmentThumbnailDto_WithMaxSize_ShouldAllowMaxValue()
    {
        // Arrange & Act
        var dto = new AttachmentThumbnailDto { Size = long.MaxValue };

        // Assert
        Assert.Equal(long.MaxValue, dto.Size);
    }

    [Fact]
    public void AttachmentThumbnailDto_WithZeroWidth_ShouldAllowZero()
    {
        // Arrange & Act
        var dto = new AttachmentThumbnailDto { Width = 0 };

        // Assert
        Assert.Equal(0, dto.Width);
    }

    [Fact]
    public void AttachmentThumbnailDto_WithNegativeWidth_ShouldAllowNegative()
    {
        // Arrange & Act
        var dto = new AttachmentThumbnailDto { Width = -1 };

        // Assert
        Assert.Equal(-1, dto.Width);
    }

    [Fact]
    public void AttachmentThumbnailDto_WithMaxWidth_ShouldAllowMaxValue()
    {
        // Arrange & Act
        var dto = new AttachmentThumbnailDto { Width = int.MaxValue };

        // Assert
        Assert.Equal(int.MaxValue, dto.Width);
    }

    [Fact]
    public void AttachmentThumbnailDto_WithZeroHeight_ShouldAllowZero()
    {
        // Arrange & Act
        var dto = new AttachmentThumbnailDto { Height = 0 };

        // Assert
        Assert.Equal(0, dto.Height);
    }

    [Fact]
    public void AttachmentThumbnailDto_WithNegativeHeight_ShouldAllowNegative()
    {
        // Arrange & Act
        var dto = new AttachmentThumbnailDto { Height = -1 };

        // Assert
        Assert.Equal(-1, dto.Height);
    }

    [Fact]
    public void AttachmentThumbnailDto_WithMaxHeight_ShouldAllowMaxValue()
    {
        // Arrange & Act
        var dto = new AttachmentThumbnailDto { Height = int.MaxValue };

        // Assert
        Assert.Equal(int.MaxValue, dto.Height);
    }

    [Fact]
    public void AttachmentThumbnailDto_WithEmptyUrl_ShouldSetEmptyString()
    {
        // Arrange & Act
        var dto = new AttachmentThumbnailDto { Url = string.Empty };

        // Assert
        Assert.Equal(string.Empty, dto.Url);
    }

    [Fact]
    public void AttachmentThumbnailDto_WithNullUrl_ShouldAllowNull()
    {
        // Arrange & Act
        var dto = new AttachmentThumbnailDto { Url = null };

        // Assert
        Assert.Null(dto.Url);
    }

    [Fact]
    public void AttachmentThumbnailDto_WithEmptyFileName_ShouldSetEmptyString()
    {
        // Arrange & Act
        var dto = new AttachmentThumbnailDto { FileName = string.Empty };

        // Assert
        Assert.Equal(string.Empty, dto.FileName);
    }

    [Fact]
    public void AttachmentThumbnailDto_WithNullFileName_ShouldAllowNull()
    {
        // Arrange & Act
        var dto = new AttachmentThumbnailDto { FileName = null };

        // Assert
        Assert.Null(dto.FileName);
    }

    [Fact]
    public void AttachmentThumbnailDto_WithEmptyContentType_ShouldSetEmptyString()
    {
        // Arrange & Act
        var dto = new AttachmentThumbnailDto { ContentType = string.Empty };

        // Assert
        Assert.Equal(string.Empty, dto.ContentType);
    }

    [Fact]
    public void AttachmentThumbnailDto_WithNullContentType_ShouldAllowNull()
    {
        // Arrange & Act
        var dto = new AttachmentThumbnailDto { ContentType = null };

        // Assert
        Assert.Null(dto.ContentType);
    }

    [Fact]
    public void AttachmentThumbnailDto_WithEmptyContentUrl_ShouldSetEmptyString()
    {
        // Arrange & Act
        var dto = new AttachmentThumbnailDto { ContentUrl = string.Empty };

        // Assert
        Assert.Equal(string.Empty, dto.ContentUrl);
    }

    [Fact]
    public void AttachmentThumbnailDto_WithNullContentUrl_ShouldAllowNull()
    {
        // Arrange & Act
        var dto = new AttachmentThumbnailDto { ContentUrl = null };

        // Assert
        Assert.Null(dto.ContentUrl);
    }

    [Fact]
    public void AttachmentThumbnailDto_WithEmptyMappedContentUrl_ShouldSetEmptyString()
    {
        // Arrange & Act
        var dto = new AttachmentThumbnailDto { MappedContentUrl = string.Empty };

        // Assert
        Assert.Equal(string.Empty, dto.MappedContentUrl);
    }

    [Fact]
    public void AttachmentThumbnailDto_WithNullMappedContentUrl_ShouldAllowNull()
    {
        // Arrange & Act
        var dto = new AttachmentThumbnailDto { MappedContentUrl = null };

        // Assert
        Assert.Null(dto.MappedContentUrl);
    }

    [Fact]
    public void AttachmentThumbnailDto_WithMultiplePropertyAssignments_ShouldUpdateValues()
    {
        // Arrange
        var dto = new AttachmentThumbnailDto
        {
            Url = "https://example.com/thumb1.jpg",
            Id = 1L,
            FileName = "thumb1.jpg"
        };

        // Assert initial state
        Assert.Equal("https://example.com/thumb1.jpg", dto.Url);
        Assert.Equal(1L, dto.Id);
        Assert.Equal("thumb1.jpg", dto.FileName);

        // Update with object initializer
        dto = new AttachmentThumbnailDto
        {
            Url = "https://example.com/thumb2.jpg",
            Id = 2L,
            FileName = "thumb2.jpg",
            ContentType = "image/png"
        };

        // Assert updated values
        Assert.Equal("https://example.com/thumb2.jpg", dto.Url);
        Assert.Equal(2L, dto.Id);
        Assert.Equal("thumb2.jpg", dto.FileName);
        Assert.Equal("image/png", dto.ContentType);
    }
}