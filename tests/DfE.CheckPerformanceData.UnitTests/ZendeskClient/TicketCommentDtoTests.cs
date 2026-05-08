using System;
using System.Collections.Generic;
using Xunit;
using DfE.CheckPerformanceData.Application.ZendeskClient;

namespace DfE.CheckPerformanceData.UnitTests.ZendeskClient;

/// <summary>
/// Unit tests for TicketCommentDto class.
/// </summary>
public class TicketCommentDtoTests
{
    [Fact]
    public void TicketCommentDto_DefaultConstructor_ShouldInitializeProperties()
    {
        // Arrange & Act
        var dto = new TicketCommentDto();

        // Assert
        Assert.Equal(0L, dto.Id);
        Assert.Null(dto.Type);
        Assert.Equal(0L, dto.AuthorId);
        Assert.Equal(string.Empty, dto.Body);
        Assert.Null(dto.HtmlBody);
        Assert.Null(dto.PlainBody);
        Assert.False(dto.Public);
        Assert.Null(dto.Attachments);
        Assert.Equal(0L, dto.AuditId);
        Assert.Null(dto.Via);
        Assert.Equal(default, dto.CreatedAt);
        Assert.Null(dto.Metadata);
    }

    [Fact]
    public void TicketCommentDto_WithAllProperties_ShouldSetAllProperties()
    {
        // Arrange & Act
        var dto = new TicketCommentDto
        {
            Id = 123L,
            Type = "comment",
            AuthorId = 456L,
            Body = "Test comment body",
            HtmlBody = "<p>Test comment body</p>",
            PlainBody = "Test comment body",
            Public = true,
            Attachments = new List<AttachmentDto>
            {
                new() { Id = 1L, FileName = "file1.pdf" }
            },
            AuditId = 789L,
            Via = new ViaDto { Channel = "email" },
            CreatedAt = new DateTime(2024, 1, 1),
            Metadata = new CommentMetadataDto { System = new MetadataSystemDto { Client = "test" } }
        };

        // Assert
        Assert.Equal(123L, dto.Id);
        Assert.Equal("comment", dto.Type);
        Assert.Equal(456L, dto.AuthorId);
        Assert.Equal("Test comment body", dto.Body);
        Assert.Equal("<p>Test comment body</p>", dto.HtmlBody);
        Assert.Equal("Test comment body", dto.PlainBody);
        Assert.True(dto.Public);
        Assert.Single(dto.Attachments);
        Assert.Equal(789L, dto.AuditId);
        Assert.Equal("email", dto.Via.Channel);
        Assert.Equal(new DateTime(2024, 1, 1), dto.CreatedAt);
        Assert.NotNull(dto.Metadata);
        Assert.NotNull(dto.Metadata.System);
        Assert.Equal("test", dto.Metadata.System.Client);
    }

    [Fact]
    public void TicketCommentDto_WithZeroId_ShouldAllowZero()
    {
        // Arrange & Act
        var dto = new TicketCommentDto { Id = 0L };

        // Assert
        Assert.Equal(0L, dto.Id);
    }

    [Fact]
    public void TicketCommentDto_WithNegativeId_ShouldAllowNegative()
    {
        // Arrange & Act
        var dto = new TicketCommentDto { Id = -1L };

        // Assert
        Assert.Equal(-1L, dto.Id);
    }

    [Fact]
    public void TicketCommentDto_WithMaxId_ShouldAllowMaxValue()
    {
        // Arrange & Act
        var dto = new TicketCommentDto { Id = long.MaxValue };

        // Assert
        Assert.Equal(long.MaxValue, dto.Id);
    }

    [Fact]
    public void TicketCommentDto_WithZeroAuthorId_ShouldAllowZero()
    {
        // Arrange & Act
        var dto = new TicketCommentDto { AuthorId = 0L };

        // Assert
        Assert.Equal(0L, dto.AuthorId);
    }

    [Fact]
    public void TicketCommentDto_WithNegativeAuthorId_ShouldAllowNegative()
    {
        // Arrange & Act
        var dto = new TicketCommentDto { AuthorId = -1L };

        // Assert
        Assert.Equal(-1L, dto.AuthorId);
    }

    [Fact]
    public void TicketCommentDto_WithMaxAuthorId_ShouldAllowMaxValue()
    {
        // Arrange & Act
        var dto = new TicketCommentDto { AuthorId = long.MaxValue };

        // Assert
        Assert.Equal(long.MaxValue, dto.AuthorId);
    }

    [Fact]
    public void TicketCommentDto_WithZeroAuditId_ShouldAllowZero()
    {
        // Arrange & Act
        var dto = new TicketCommentDto { AuditId = 0L };

        // Assert
        Assert.Equal(0L, dto.AuditId);
    }

    [Fact]
    public void TicketCommentDto_WithNegativeAuditId_ShouldAllowNegative()
    {
        // Arrange & Act
        var dto = new TicketCommentDto { AuditId = -1L };

        // Assert
        Assert.Equal(-1L, dto.AuditId);
    }

    [Fact]
    public void TicketCommentDto_WithMaxAuditId_ShouldAllowMaxValue()
    {
        // Arrange & Act
        var dto = new TicketCommentDto { AuditId = long.MaxValue };

        // Assert
        Assert.Equal(long.MaxValue, dto.AuditId);
    }

    [Fact]
    public void TicketCommentDto_WithEmptyBody_ShouldSetEmptyString()
    {
        // Arrange & Act
        var dto = new TicketCommentDto { Body = string.Empty };

        // Assert
        Assert.Equal(string.Empty, dto.Body);
    }

    [Fact]
    public void TicketCommentDto_WithNullBody_ShouldAllowNull()
    {
        // Arrange & Act
        var dto = new TicketCommentDto { Body = null };

        // Assert
        Assert.Null(dto.Body);
    }

    [Fact]
    public void TicketCommentDto_WithEmptyHtmlBody_ShouldSetEmptyString()
    {
        // Arrange & Act
        var dto = new TicketCommentDto { HtmlBody = string.Empty };

        // Assert
        Assert.Equal(string.Empty, dto.HtmlBody);
    }

    [Fact]
    public void TicketCommentDto_WithNullHtmlBody_ShouldAllowNull()
    {
        // Arrange & Act
        var dto = new TicketCommentDto { HtmlBody = null };

        // Assert
        Assert.Null(dto.HtmlBody);
    }

    [Fact]
    public void TicketCommentDto_WithEmptyPlainBody_ShouldSetEmptyString()
    {
        // Arrange & Act
        var dto = new TicketCommentDto { PlainBody = string.Empty };

        // Assert
        Assert.Equal(string.Empty, dto.PlainBody);
    }

    [Fact]
    public void TicketCommentDto_WithNullPlainBody_ShouldAllowNull()
    {
        // Arrange & Act
        var dto = new TicketCommentDto { PlainBody = null };

        // Assert
        Assert.Null(dto.PlainBody);
    }

    [Fact]
    public void TicketCommentDto_WithEmptyType_ShouldSetEmptyString()
    {
        // Arrange & Act
        var dto = new TicketCommentDto { Type = string.Empty };

        // Assert
        Assert.Equal(string.Empty, dto.Type);
    }

    [Fact]
    public void TicketCommentDto_WithNullType_ShouldAllowNull()
    {
        // Arrange & Act
        var dto = new TicketCommentDto { Type = null };

        // Assert
        Assert.Null(dto.Type);
    }

    [Fact]
    public void TicketCommentDto_WithEmptyChannel_ShouldSetEmptyString()
    {
        // Arrange & Act
        var dto = new TicketCommentDto { Via = new ViaDto { Channel = string.Empty } };

        // Assert
        Assert.Equal(string.Empty, dto.Via.Channel);
    }

    [Fact]
    public void TicketCommentDto_WithNullChannel_ShouldAllowNull()
    {
        // Arrange & Act
        var dto = new TicketCommentDto { Via = new ViaDto { Channel = null } };

        // Assert
        Assert.Null(dto.Via.Channel);
    }

    [Fact]
    public void TicketCommentDto_WithNullAttachments_ShouldAllowNull()
    {
        // Arrange & Act
        var dto = new TicketCommentDto { Attachments = null };

        // Assert
        Assert.Null(dto.Attachments);
    }

    [Fact]
    public void TicketCommentDto_WithEmptyAttachments_ShouldInitializeEmptyList()
    {
        // Arrange & Act
        var dto = new TicketCommentDto { Attachments = new List<AttachmentDto>() };

        // Assert
        Assert.Empty(dto.Attachments);
    }

    [Fact]
    public void TicketCommentDto_WithNullVia_ShouldAllowNull()
    {
        // Arrange & Act
        var dto = new TicketCommentDto { Via = null };

        // Assert
        Assert.Null(dto.Via);
    }

    [Fact]
    public void TicketCommentDto_WithNullViaSource_ShouldAllowNull()
    {
        // Arrange & Act
        var dto = new TicketCommentDto { Via = new ViaDto { Source = null } };

        // Assert
        Assert.Null(dto.Via.Source);
    }

    [Fact]
    public void TicketCommentDto_WithNullMetadata_ShouldAllowNull()
    {
        // Arrange & Act
        var dto = new TicketCommentDto { Metadata = null };

        // Assert
        Assert.Null(dto.Metadata);
    }

    [Fact]
    public void TicketCommentDto_WithNullSystem_ShouldAllowNull()
    {
        // Arrange & Act
        var dto = new TicketCommentDto { Metadata = new CommentMetadataDto { System = null } };

        // Assert
        Assert.Null(dto.Metadata.System);
    }

    [Fact]
    public void TicketCommentDto_WithNullCustom_ShouldAllowNull()
    {
        // Arrange & Act
        var dto = new TicketCommentDto { Metadata = new CommentMetadataDto { Custom = null } };

        // Assert
        Assert.Null(dto.Metadata.Custom);
    }

    [Fact]
    public void TicketCommentDto_WithMinDateTime_ShouldAllowMinValue()
    {
        // Arrange & Act
        var dto = new TicketCommentDto { CreatedAt = DateTime.MinValue };

        // Assert
        Assert.Equal(DateTime.MinValue, dto.CreatedAt);
    }

    [Fact]
    public void TicketCommentDto_WithMaxDateTime_ShouldAllowMaxValue()
    {
        // Arrange & Act
        var dto = new TicketCommentDto { CreatedAt = DateTime.MaxValue };

        // Assert
        Assert.Equal(DateTime.MaxValue, dto.CreatedAt);
    }

    [Fact]
    public void TicketCommentDto_WithUtcDateTime_ShouldStoreUtcTime()
    {
        // Arrange
        var utcTime = DateTime.UtcNow;
        var dto = new TicketCommentDto { CreatedAt = utcTime };

        // Assert
        Assert.Equal(utcTime, dto.CreatedAt);
    }

    [Fact]
    public void TicketCommentDto_WithDifferentTimeZones_ShouldStoreTime()
    {
        // Arrange
        var localTime = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Local);
        var dto = new TicketCommentDto { CreatedAt = localTime };

        // Assert
        Assert.Equal(localTime, dto.CreatedAt);
        Assert.NotNull(dto.CreatedAt);
        Assert.Equal(DateTimeKind.Local, dto.CreatedAt.Kind);
    }

    [Fact]
    public void TicketCommentDto_WithMultiplePropertyAssignments_ShouldUpdateValues()
    {
        // Arrange
        var dto = new TicketCommentDto
        {
            Id = 1L,
            AuthorId = 1L,
            Body = "First body"
        };

        // Assert initial state
        Assert.Equal(1L, dto.Id);
        Assert.Equal(1L, dto.AuthorId);
        Assert.Equal("First body", dto.Body);

        // Update with object initializer
        dto = new TicketCommentDto
        {
            Id = 2L,
            AuthorId = 2L,
            Body = "Second body",
            Public = true
        };

        // Assert updated values
        Assert.Equal(2L, dto.Id);
        Assert.Equal(2L, dto.AuthorId);
        Assert.Equal("Second body", dto.Body);
        Assert.True(dto.Public);
    }
}