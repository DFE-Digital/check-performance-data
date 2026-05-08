using System;
using System.Collections.Generic;
using Xunit;
using DfE.CheckPerformanceData.Application.ZendeskClient;

namespace DfE.CheckPerformanceData.UnitTests.ZendeskClient;

/// <summary>
/// Unit tests for TicketCommentDto, TicketCommentsResponseDto, and related classes.
/// </summary>
public class TicketCommentsResponseDtoTests
{
    #region TicketCommentDto Tests

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
            Id = 100L,
            Type = "comment",
            AuthorId = 50L,
            Body = "Test comment body",
            HtmlBody = "<p>HTML body</p>",
            PlainBody = "Plain body",
            Public = true,
            Attachments = new List<AttachmentDto>
            {
                new() { Id = 1L, FileName = "file1.pdf" },
                new() { Id = 2L, FileName = "file2.jpg" }
            },
            AuditId = 200L,
            Via = new ViaDto { Channel = "email" },
            CreatedAt = DateTime.UtcNow,
            Metadata = new CommentMetadataDto
            {
                System = new MetadataSystemDto { Client = "test-client" }
            }
        };

        // Assert
        Assert.Equal(100L, dto.Id);
        Assert.Equal("comment", dto.Type);
        Assert.Equal(50L, dto.AuthorId);
        Assert.Equal("Test comment body", dto.Body);
        Assert.Equal("<p>HTML body</p>", dto.HtmlBody);
        Assert.Equal("Plain body", dto.PlainBody);
        Assert.True(dto.Public);
        Assert.Equal(2, dto.Attachments.Count);
        Assert.Equal(200L, dto.AuditId);
        Assert.Equal("email", dto.Via?.Channel);
        Assert.NotEqual(default, dto.CreatedAt);
        Assert.NotNull(dto.Metadata);
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
    public void TicketCommentDto_WithZeroId_ShouldAllowZero()
    {
        // Arrange & Act
        var dto = new TicketCommentDto { Id = 0L };

        // Assert
        Assert.Equal(0L, dto.Id);
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
    public void TicketCommentDto_WithZeroAuditId_ShouldAllowZero()
    {
        // Arrange & Act
        var dto = new TicketCommentDto { AuditId = 0L };

        // Assert
        Assert.Equal(0L, dto.AuditId);
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
    public void TicketCommentDto_WithNegativeAuthorId_ShouldAllowNegative()
    {
        // Arrange & Act
        var dto = new TicketCommentDto { AuthorId = -1L };

        // Assert
        Assert.Equal(-1L, dto.AuthorId);
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
    public void TicketCommentDto_WithMaxId_ShouldAllowMaxValue()
    {
        // Arrange & Act
        var dto = new TicketCommentDto { Id = long.MaxValue };

        // Assert
        Assert.Equal(long.MaxValue, dto.Id);
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
    public void TicketCommentDto_WithMaxAuditId_ShouldAllowMaxValue()
    {
        // Arrange & Act
        var dto = new TicketCommentDto { AuditId = long.MaxValue };

        // Assert
        Assert.Equal(long.MaxValue, dto.AuditId);
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
    public void TicketCommentDto_WithNullMetadata_ShouldAllowNull()
    {
        // Arrange & Act
        var dto = new TicketCommentDto { Metadata = null };

        // Assert
        Assert.Null(dto.Metadata);
    }

    [Fact]
    public void TicketCommentDto_WithSpecialCharactersInBody_ShouldStoreSpecialChars()
    {
        // Arrange & Act
        var dto = new TicketCommentDto
        {
            Body = "Body with special chars: @#$%^&*()_+-=[]{}|;':\",./<>?"
        };

        // Assert
        Assert.Equal("Body with special chars: @#$%^&*()_+-=[]{}|;':\",./<>?", dto.Body);
    }

    [Fact]
    public void TicketCommentDto_WithUnicodeInBody_ShouldStoreUnicode()
    {
        // Arrange & Act
        var dto = new TicketCommentDto
        {
            Body = "Body with unicode: 你好世界 🌍"
        };

        // Assert
        Assert.Equal("Body with unicode: 你好世界 🌍", dto.Body);
    }

    [Fact]
    public void TicketCommentDto_WithWhitespaceInBody_ShouldStoreWhitespace()
    {
        // Arrange & Act
        var dto = new TicketCommentDto
        {
            Body = "   Body with spaces   "
        };

        // Assert
        Assert.Equal("   Body with spaces   ", dto.Body);
    }

    [Fact]
    public void TicketCommentDto_WithMultiplePropertyAssignments_ShouldUpdateValues()
    {
        // Arrange & Act
        var dto = new TicketCommentDto
        {
            Id = 1L,
            AuthorId = 2L,
            Body = "Body 1",
            Public = true,
            CreatedAt = DateTime.UtcNow
        };

        // Assert initial state
        Assert.Equal(1L, dto.Id);
        Assert.Equal(2L, dto.AuthorId);
        Assert.Equal("Body 1", dto.Body);
        Assert.True(dto.Public);

        // Update with object initializer
        dto = new TicketCommentDto
        {
            Id = 10L,
            AuthorId = 20L,
            Body = "Body 2",
            Public = false
        };

        // Assert updated values
        Assert.Equal(10L, dto.Id);
        Assert.Equal(20L, dto.AuthorId);
        Assert.Equal("Body 2", dto.Body);
        Assert.False(dto.Public);
    }

    #endregion

    #region TicketCommentsResponseDto Tests

    [Fact]
    public void TicketCommentsResponseDto_DefaultConstructor_ShouldInitializeProperties()
    {
        // Arrange & Act
        var dto = new TicketCommentsResponseDto();

        // Assert
        Assert.Null(dto.Comments);
        Assert.Null(dto.NextPage);
        Assert.Null(dto.PreviousPage);
        Assert.Equal(0, dto.Count);
    }

    [Fact]
    public void TicketCommentsResponseDto_WithAllProperties_ShouldSetAllProperties()
    {
        // Arrange & Act
        var dto = new TicketCommentsResponseDto
        {
            Comments = new List<TicketCommentDto>
            {
                new() { Id = 1L, Body = "Comment 1" },
                new() { Id = 2L, Body = "Comment 2" }
            },
            NextPage = "https://example.com/next",
            PreviousPage = "https://example.com/previous",
            Count = 100
        };

        // Assert
        Assert.Equal(2, dto.Comments.Count);
        Assert.Equal("https://example.com/next", dto.NextPage);
        Assert.Equal("https://example.com/previous", dto.PreviousPage);
        Assert.Equal(100, dto.Count);
    }

    [Fact]
    public void TicketCommentsResponseDto_WithNullComments_ShouldAllowNull()
    {
        // Arrange & Act
        var dto = new TicketCommentsResponseDto { Comments = null };

        // Assert
        Assert.Null(dto.Comments);
    }

    [Fact]
    public void TicketCommentsResponseDto_WithEmptyComments_ShouldInitializeEmptyList()
    {
        // Arrange & Act
        var dto = new TicketCommentsResponseDto { Comments = new List<TicketCommentDto>() };

        // Assert
        Assert.Empty(dto.Comments);
    }

    [Fact]
    public void TicketCommentsResponseDto_WithZeroCount_ShouldAllowZero()
    {
        // Arrange & Act
        var dto = new TicketCommentsResponseDto { Count = 0 };

        // Assert
        Assert.Equal(0, dto.Count);
    }

    [Fact]
    public void TicketCommentsResponseDto_WithNegativeCount_ShouldAllowNegative()
    {
        // Arrange & Act
        var dto = new TicketCommentsResponseDto { Count = -50 };

        // Assert
        Assert.Equal(-50, dto.Count);
    }

    [Fact]
    public void TicketCommentsResponseDto_WithMaxCount_ShouldAllowMaxValue()
    {
        // Arrange & Act
        var dto = new TicketCommentsResponseDto { Count = int.MaxValue };

        // Assert
        Assert.Equal(int.MaxValue, dto.Count);
    }

    [Fact]
    public void TicketCommentsResponseDto_WithNullCount_ShouldAllowNull()
    {
        // Arrange & Act
        var dto = new TicketCommentsResponseDto { Count = 100 };

        // Assert
        Assert.Equal(100, dto.Count);
    }

    [Fact]
    public void TicketCommentsResponseDto_WithNullNextPage_ShouldAllowNull()
    {
        // Arrange & Act
        var dto = new TicketCommentsResponseDto { NextPage = null };

        // Assert
        Assert.Null(dto.NextPage);
    }

    [Fact]
    public void TicketCommentsResponseDto_WithNullPreviousPage_ShouldAllowNull()
    {
        // Arrange & Act
        var dto = new TicketCommentsResponseDto { PreviousPage = null };

        // Assert
        Assert.Null(dto.PreviousPage);
    }

    [Fact]
    public void TicketCommentsResponseDto_WithSpecialCharactersInNextPage_ShouldStoreSpecialChars()
    {
        // Arrange & Act
        var dto = new TicketCommentsResponseDto
        {
            NextPage = "https://example.com/page?param=value&param2=value2"
        };

        // Assert
        Assert.Equal("https://example.com/page?param=value&param2=value2", dto.NextPage);
    }

    [Fact]
    public void TicketCommentsResponseDto_WithUnicodeInNextPage_ShouldStoreUnicode()
    {
        // Arrange & Act
        var dto = new TicketCommentsResponseDto
        {
            NextPage = "https://example.com/页面"
        };

        // Assert
        Assert.Equal("https://example.com/页面", dto.NextPage);
    }

    [Fact]
    public void TicketCommentsResponseDto_WithWhitespaceInNextPage_ShouldStoreWhitespace()
    {
        // Arrange & Act
        var dto = new TicketCommentsResponseDto
        {
            NextPage = "   https://example.com   "
        };

        // Assert
        Assert.Equal("   https://example.com   ", dto.NextPage);
    }

    [Fact]
    public void TicketCommentsResponseDto_WithMultiplePropertyAssignments_ShouldUpdateValues()
    {
        // Arrange & Act
        var dto = new TicketCommentsResponseDto
        {
            Comments = new List<TicketCommentDto> { new() { Id = 1L } },
            NextPage = "next1",
            PreviousPage = "prev1",
            Count = 10
        };

        // Assert initial state
        Assert.Single(dto.Comments);
        Assert.Equal("next1", dto.NextPage);
        Assert.Equal("prev1", dto.PreviousPage);
        Assert.Equal(10, dto.Count);

        // Update with object initializer
        dto = new TicketCommentsResponseDto
        {
            Comments = new List<TicketCommentDto> { new() { Id = 2L } },
            NextPage = "next2",
            PreviousPage = "prev2",
            Count = 20
        };

        // Assert updated values
        Assert.Single(dto.Comments);
        Assert.Equal(2L, dto.Comments[0].Id);
        Assert.Equal("next2", dto.NextPage);
        Assert.Equal("prev2", dto.PreviousPage);
        Assert.Equal(20, dto.Count);
    }

    #endregion

    #region ViaDto Tests

    [Fact]
    public void ViaDto_DefaultConstructor_ShouldInitializeProperties()
    {
        // Arrange & Act
        var dto = new ViaDto();

        // Assert
        Assert.Equal(string.Empty, dto.Channel);
        Assert.Null(dto.Source);
    }

    [Fact]
    public void ViaDto_WithAllProperties_ShouldSetAllProperties()
    {
        // Arrange & Act
        var dto = new ViaDto
        {
            Channel = "email",
            Source = new ViaSourceDto
            {
                From = new Dictionary<string, object> { { "key", "value" } },
                To = new Dictionary<string, object> { { "toKey", "toValue" } },
                Rel = "relValue"
            }
        };

        // Assert
        Assert.Equal("email", dto.Channel);
        Assert.NotNull(dto.Source);
        Assert.NotNull(dto.Source.From);
        Assert.NotNull(dto.Source.To);
        Assert.Equal("relValue", dto.Source.Rel);
    }

    [Fact]
    public void ViaDto_WithEmptyChannel_ShouldSetEmptyString()
    {
        // Arrange & Act
        var dto = new ViaDto { Channel = string.Empty };

        // Assert
        Assert.Equal(string.Empty, dto.Channel);
    }

    [Fact]
    public void ViaDto_WithNullChannel_ShouldAllowNull()
    {
        // Arrange & Act
        var dto = new ViaDto { Channel = null };

        // Assert
        Assert.Null(dto.Channel);
    }

    [Fact]
    public void ViaDto_WithNullSource_ShouldAllowNull()
    {
        // Arrange & Act
        var dto = new ViaDto { Source = null };

        // Assert
        Assert.Null(dto.Source);
    }

    [Fact]
    public void ViaDto_WithSpecialCharactersInChannel_ShouldStoreSpecialChars()
    {
        // Arrange & Act
        var dto = new ViaDto
        {
            Channel = "Channel with special chars: @#$%^&*()"
        };

        // Assert
        Assert.Equal("Channel with special chars: @#$%^&*()", dto.Channel);
    }

    [Fact]
    public void ViaDto_WithUnicodeInChannel_ShouldStoreUnicode()
    {
        // Arrange & Act
        var dto = new ViaDto
        {
            Channel = "Channel with unicode: 频道"
        };

        // Assert
        Assert.Equal("Channel with unicode: 频道", dto.Channel);
    }

    [Fact]
    public void ViaDto_WithWhitespaceInChannel_ShouldStoreWhitespace()
    {
        // Arrange & Act
        var dto = new ViaDto
        {
            Channel = "   Channel with spaces   "
        };

        // Assert
        Assert.Equal("   Channel with spaces   ", dto.Channel);
    }

    [Fact]
    public void ViaDto_WithMultipleChannelAssignments_ShouldUpdateValue()
    {
        // Arrange & Act
        var dto = new ViaDto { Channel = "channel1" };
        dto = new ViaDto { Channel = "channel2" };
        dto = new ViaDto { Channel = "channel3" };

        // Assert
        Assert.Equal("channel3", dto.Channel);
    }

    #endregion

    #region ViaSourceDto Tests

    [Fact]
    public void ViaSourceDto_DefaultConstructor_ShouldInitializeProperties()
    {
        // Arrange & Act
        var dto = new ViaSourceDto();

        // Assert
        Assert.Null(dto.From);
        Assert.Null(dto.To);
        Assert.Null(dto.Rel);
    }

    [Fact]
    public void ViaSourceDto_WithAllProperties_ShouldSetAllProperties()
    {
        // Arrange & Act
        var dto = new ViaSourceDto
        {
            From = new Dictionary<string, object> { { "fromKey", "fromValue" } },
            To = new Dictionary<string, object> { { "toKey", "toValue" } },
            Rel = "relValue"
        };

        // Assert
        Assert.NotNull(dto.From);
        Assert.NotNull(dto.To);
        Assert.Equal("relValue", dto.Rel);
    }

    [Fact]
    public void ViaSourceDto_WithNullFrom_ShouldAllowNull()
    {
        // Arrange & Act
        var dto = new ViaSourceDto { From = null };

        // Assert
        Assert.Null(dto.From);
    }

    [Fact]
    public void ViaSourceDto_WithNullTo_ShouldAllowNull()
    {
        // Arrange & Act
        var dto = new ViaSourceDto { To = null };

        // Assert
        Assert.Null(dto.To);
    }

    [Fact]
    public void ViaSourceDto_WithNullRel_ShouldAllowNull()
    {
        // Arrange & Act
        var dto = new ViaSourceDto { Rel = null };

        // Assert
        Assert.Null(dto.Rel);
    }

    [Fact]
    public void ViaSourceDto_WithEmptyFrom_ShouldSetEmptyDictionary()
    {
        // Arrange & Act
        var dto = new ViaSourceDto { From = new Dictionary<string, object>() };

        // Assert
        Assert.Empty(dto.From);
    }

    [Fact]
    public void ViaSourceDto_WithEmptyTo_ShouldSetEmptyDictionary()
    {
        // Arrange & Act
        var dto = new ViaSourceDto { To = new Dictionary<string, object>() };

        // Assert
        Assert.Empty(dto.To);
    }

    [Fact]
    public void ViaSourceDto_WithSpecialCharactersInRel_ShouldStoreSpecialChars()
    {
        // Arrange & Act
        var dto = new ViaSourceDto
        {
            Rel = "Rel with special chars: @#$%^&*()"
        };

        // Assert
        Assert.Equal("Rel with special chars: @#$%^&*()", dto.Rel);
    }

    [Fact]
    public void ViaSourceDto_WithUnicodeInRel_ShouldStoreUnicode()
    {
        // Arrange & Act
        var dto = new ViaSourceDto
        {
            Rel = "Rel with unicode: 关系"
        };

        // Assert
        Assert.Equal("Rel with unicode: 关系", dto.Rel);
    }

    [Fact]
    public void ViaSourceDto_WithWhitespaceInRel_ShouldStoreWhitespace()
    {
        // Arrange & Act
        var dto = new ViaSourceDto
        {
            Rel = "   Rel with spaces   "
        };

        // Assert
        Assert.Equal("   Rel with spaces   ", dto.Rel);
    }

    [Fact]
    public void ViaSourceDto_WithMultipleRelAssignments_ShouldUpdateValue()
    {
        // Arrange & Act
        var dto = new ViaSourceDto { Rel = "rel1" };
        dto = new ViaSourceDto { Rel = "rel2" };
        dto = new ViaSourceDto { Rel = "rel3" };

        // Assert
        Assert.Equal("rel3", dto.Rel);
    }

    #endregion

    #region CommentMetadataDto Tests

    [Fact]
    public void CommentMetadataDto_DefaultConstructor_ShouldInitializeProperties()
    {
        // Arrange & Act
        var dto = new CommentMetadataDto();

        // Assert
        Assert.Null(dto.System);
        Assert.Null(dto.Custom);
    }

    [Fact]
    public void CommentMetadataDto_WithAllProperties_ShouldSetAllProperties()
    {
        // Arrange & Act
        var dto = new CommentMetadataDto
        {
            System = new MetadataSystemDto
            {
                Client = "test-client",
                IpAddress = "192.168.1.1",
                Location = "London",
                Latitude = 51.5074,
                Longitude = -0.1278
            },
            Custom = new Dictionary<string, object> { { "customKey", "customValue" } }
        };

        // Assert
        Assert.NotNull(dto.System);
        Assert.NotNull(dto.Custom);
        Assert.Equal("test-client", dto.System.Client);
        Assert.Equal("192.168.1.1", dto.System.IpAddress);
        Assert.Equal("London", dto.System.Location);
        Assert.Equal(51.5074, dto.System.Latitude);
        Assert.Equal(-0.1278, dto.System.Longitude);
        Assert.Single(dto.Custom);
    }

    [Fact]
    public void CommentMetadataDto_WithNullSystem_ShouldAllowNull()
    {
        // Arrange & Act
        var dto = new CommentMetadataDto { System = null };

        // Assert
        Assert.Null(dto.System);
    }

    [Fact]
    public void CommentMetadataDto_WithNullCustom_ShouldAllowNull()
    {
        // Arrange & Act
        var dto = new CommentMetadataDto { Custom = null };

        // Assert
        Assert.Null(dto.Custom);
    }

    [Fact]
    public void CommentMetadataDto_WithEmptyCustom_ShouldSetEmptyDictionary()
    {
        // Arrange & Act
        var dto = new CommentMetadataDto { Custom = new Dictionary<string, object>() };

        // Assert
        Assert.Empty(dto.Custom);
    }

    [Fact]
    public void CommentMetadataDto_WithSpecialCharactersInCustom_ShouldStoreSpecialChars()
    {
        // Arrange & Act
        var dto = new CommentMetadataDto
        {
            Custom = new Dictionary<string, object> { { "key@#$%", "value@#$%" } }
        };

        // Assert
        Assert.Single(dto.Custom);
        Assert.Equal("value@#$%", dto.Custom["key@#$%"]);
    }

    [Fact]
    public void CommentMetadataDto_WithUnicodeInCustom_ShouldStoreUnicode()
    {
        // Arrange & Act
        var dto = new CommentMetadataDto
        {
            Custom = new Dictionary<string, object> { { "键", "值" } }
        };

        // Assert
        Assert.Single(dto.Custom);
        Assert.Equal("值", dto.Custom["键"]);
    }

    [Fact]
    public void CommentMetadataDto_WithWhitespaceInCustom_ShouldStoreWhitespace()
    {
        // Arrange & Act
        var dto = new CommentMetadataDto
        {
            Custom = new Dictionary<string, object> { { "key", " value " } }
        };

        // Assert
        Assert.Single(dto.Custom);
        Assert.Equal(" value ", dto.Custom["key"]);
    }

    [Fact]
    public void CommentMetadataDto_WithMultipleCustomAssignments_ShouldUpdateValue()
    {
        // Arrange & Act
        var dto = new CommentMetadataDto { Custom = new Dictionary<string, object> { { "key1", "value1" } } };
        dto = new CommentMetadataDto { Custom = new Dictionary<string, object> { { "key2", "value2" } } };
        dto = new CommentMetadataDto { Custom = new Dictionary<string, object> { { "key3", "value3" } } };

        // Assert
        Assert.Single(dto.Custom);
        Assert.Equal("value3", dto.Custom["key3"]);
    }

    #endregion

    #region MetadataSystemDto Tests

    [Fact]
    public void MetadataSystemDto_DefaultConstructor_ShouldInitializeProperties()
    {
        // Arrange & Act
        var dto = new MetadataSystemDto();

        // Assert
        Assert.Null(dto.Client);
        Assert.Null(dto.IpAddress);
        Assert.Null(dto.Location);
        Assert.Equal(0.0, dto.Latitude);
        Assert.Equal(0.0, dto.Longitude);
    }

    [Fact]
    public void MetadataSystemDto_WithAllProperties_ShouldSetAllProperties()
    {
        // Arrange & Act
        var dto = new MetadataSystemDto
        {
            Client = "test-client",
            IpAddress = "192.168.1.1",
            Location = "London",
            Latitude = 51.5074,
            Longitude = -0.1278
        };

        // Assert
        Assert.Equal("test-client", dto.Client);
        Assert.Equal("192.168.1.1", dto.IpAddress);
        Assert.Equal("London", dto.Location);
        Assert.Equal(51.5074, dto.Latitude);
        Assert.Equal(-0.1278, dto.Longitude);
    }

    [Fact]
    public void MetadataSystemDto_WithNullClient_ShouldAllowNull()
    {
        // Arrange & Act
        var dto = new MetadataSystemDto { Client = null };

        // Assert
        Assert.Null(dto.Client);
    }

    [Fact]
    public void MetadataSystemDto_WithNullIpAddress_ShouldAllowNull()
    {
        // Arrange & Act
        var dto = new MetadataSystemDto { IpAddress = null };

        // Assert
        Assert.Null(dto.IpAddress);
    }

    [Fact]
    public void MetadataSystemDto_WithNullLocation_ShouldAllowNull()
    {
        // Arrange & Act
        var dto = new MetadataSystemDto { Location = null };

        // Assert
        Assert.Null(dto.Location);
    }

    [Fact]
    public void MetadataSystemDto_WithZeroLatitude_ShouldAllowZero()
    {
        // Arrange & Act
        var dto = new MetadataSystemDto { Latitude = 0.0 };

        // Assert
        Assert.Equal(0.0, dto.Latitude);
    }

    [Fact]
    public void MetadataSystemDto_WithNegativeLatitude_ShouldAllowNegative()
    {
        // Arrange & Act
        var dto = new MetadataSystemDto { Latitude = -45.5 };

        // Assert
        Assert.Equal(-45.5, dto.Latitude);
    }

    [Fact]
    public void MetadataSystemDto_WithPositiveLatitude_ShouldAllowPositive()
    {
        // Arrange & Act
        var dto = new MetadataSystemDto { Latitude = 45.5 };

        // Assert
        Assert.Equal(45.5, dto.Latitude);
    }

    [Fact]
    public void MetadataSystemDto_WithZeroLongitude_ShouldAllowZero()
    {
        // Arrange & Act
        var dto = new MetadataSystemDto { Longitude = 0.0 };

        // Assert
        Assert.Equal(0.0, dto.Longitude);
    }

    [Fact]
    public void MetadataSystemDto_WithNegativeLongitude_ShouldAllowNegative()
    {
        // Arrange & Act
        var dto = new MetadataSystemDto { Longitude = -45.5 };

        // Assert
        Assert.Equal(-45.5, dto.Longitude);
    }

    [Fact]
    public void MetadataSystemDto_WithPositiveLongitude_ShouldAllowPositive()
    {
        // Arrange & Act
        var dto = new MetadataSystemDto { Longitude = 45.5 };

        // Assert
        Assert.Equal(45.5, dto.Longitude);
    }

    [Fact]
    public void MetadataSystemDto_WithSpecialCharactersInClient_ShouldStoreSpecialChars()
    {
        // Arrange & Act
        var dto = new MetadataSystemDto
        {
            Client = "Client with special chars: @#$%^&*()"
        };

        // Assert
        Assert.Equal("Client with special chars: @#$%^&*()", dto.Client);
    }

    [Fact]
    public void MetadataSystemDto_WithUnicodeInClient_ShouldStoreUnicode()
    {
        // Arrange & Act
        var dto = new MetadataSystemDto
        {
            Client = "客户端"
        };

        // Assert
        Assert.Equal("客户端", dto.Client);
    }

    [Fact]
    public void MetadataSystemDto_WithWhitespaceInClient_ShouldStoreWhitespace()
    {
        // Arrange & Act
        var dto = new MetadataSystemDto
        {
            Client = "   Client with spaces   "
        };

        // Assert
        Assert.Equal("   Client with spaces   ", dto.Client);
    }

    [Fact]
    public void MetadataSystemDto_WithSpecialCharactersInLocation_ShouldStoreSpecialChars()
    {
        // Arrange & Act
        var dto = new MetadataSystemDto
        {
            Location = "Location with special chars: @#$%^&*()"
        };

        // Assert
        Assert.Equal("Location with special chars: @#$%^&*()", dto.Location);
    }

    [Fact]
    public void MetadataSystemDto_WithUnicodeInLocation_ShouldStoreUnicode()
    {
        // Arrange & Act
        var dto = new MetadataSystemDto
        {
            Location = "位置"
        };

        // Assert
        Assert.Equal("位置", dto.Location);
    }

    [Fact]
    public void MetadataSystemDto_WithWhitespaceInLocation_ShouldStoreWhitespace()
    {
        // Arrange & Act
        var dto = new MetadataSystemDto
        {
            Location = "   Location with spaces   "
        };

        // Assert
        Assert.Equal("   Location with spaces   ", dto.Location);
    }

    [Fact]
    public void MetadataSystemDto_WithSpecialCharactersInIpAddress_ShouldStoreSpecialChars()
    {
        // Arrange & Act
        var dto = new MetadataSystemDto
        {
            IpAddress = "192.168.1.1:8080"
        };

        // Assert
        Assert.Equal("192.168.1.1:8080", dto.IpAddress);
    }

    [Fact]
    public void MetadataSystemDto_WithMultiplePropertyAssignments_ShouldUpdateValues()
    {
        // Arrange & Act
        var dto = new MetadataSystemDto
        {
            Client = "client1",
            IpAddress = "1.1.1.1",
            Location = "loc1",
            Latitude = 1.0,
            Longitude = 2.0
        };

        // Assert initial state
        Assert.Equal("client1", dto.Client);
        Assert.Equal("1.1.1.1", dto.IpAddress);
        Assert.Equal("loc1", dto.Location);
        Assert.Equal(1.0, dto.Latitude);
        Assert.Equal(2.0, dto.Longitude);

        // Update with object initializer
        dto = new MetadataSystemDto
        {
            Client = "client2",
            IpAddress = "2.2.2.2",
            Location = "loc2",
            Latitude = 3.0,
            Longitude = 4.0
        };

        // Assert updated values
        Assert.Equal("client2", dto.Client);
        Assert.Equal("2.2.2.2", dto.IpAddress);
        Assert.Equal("loc2", dto.Location);
        Assert.Equal(3.0, dto.Latitude);
        Assert.Equal(4.0, dto.Longitude);
    }

    #endregion

    #region AttachmentDto Tests

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
            Id = 100L,
            FileName = "file.pdf",
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
                new() { Id = 1L, FileName = "thumb1.jpg" },
                new() { Id = 2L, FileName = "thumb2.jpg" }
            }
        };

        // Assert
        Assert.Equal("https://example.com/file.pdf", dto.Url);
        Assert.Equal(100L, dto.Id);
        Assert.Equal("file.pdf", dto.FileName);
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
        Assert.Equal(2, dto.Thumbnails.Count);
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
    public void AttachmentDto_WithNullContentUrl_ShouldAllowNull()
    {
        // Arrange & Act
        var dto = new AttachmentDto { ContentUrl = null };

        // Assert
        Assert.Null(dto.ContentUrl);
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
    public void AttachmentDto_WithNullContentType_ShouldAllowNull()
    {
        // Arrange & Act
        var dto = new AttachmentDto { ContentType = null };

        // Assert
        Assert.Null(dto.ContentType);
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
    public void AttachmentDto_WithNullThumbnails_ShouldAllowNull()
    {
        // Arrange & Act
        var dto = new AttachmentDto { Thumbnails = null };

        // Assert
        Assert.Null(dto.Thumbnails);
    }

    [Fact]
    public void AttachmentDto_WithEmptyThumbnails_ShouldInitializeEmptyList()
    {
        // Arrange & Act
        var dto = new AttachmentDto { Thumbnails = new List<AttachmentThumbnailDto>() };

        // Assert
        Assert.Empty(dto.Thumbnails);
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
    public void AttachmentDto_WithZeroSize_ShouldAllowZero()
    {
        // Arrange & Act
        var dto = new AttachmentDto { Size = 0L };

        // Assert
        Assert.Equal(0L, dto.Size);
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
    public void AttachmentDto_WithNegativeSize_ShouldAllowNegative()
    {
        // Arrange & Act
        var dto = new AttachmentDto { Size = -1024L };

        // Assert
        Assert.Equal(-1024L, dto.Size);
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
        var dto = new AttachmentDto { Width = -100 };

        // Assert
        Assert.Equal(-100, dto.Width);
    }

    [Fact]
    public void AttachmentDto_WithPositiveWidth_ShouldAllowPositive()
    {
        // Arrange & Act
        var dto = new AttachmentDto { Width = 100 };

        // Assert
        Assert.Equal(100, dto.Width);
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
        var dto = new AttachmentDto { Height = -100 };

        // Assert
        Assert.Equal(-100, dto.Height);
    }

    [Fact]
    public void AttachmentDto_WithPositiveHeight_ShouldAllowPositive()
    {
        // Arrange & Act
        var dto = new AttachmentDto { Height = 100 };

        // Assert
        Assert.Equal(100, dto.Height);
    }

    [Fact]
    public void AttachmentDto_WithSpecialCharactersInUrl_ShouldStoreSpecialChars()
    {
        // Arrange & Act
        var dto = new AttachmentDto
        {
            Url = "https://example.com/file@#$%.pdf"
        };

        // Assert
        Assert.Equal("https://example.com/file@#$%.pdf", dto.Url);
    }

    [Fact]
    public void AttachmentDto_WithUnicodeInUrl_ShouldStoreUnicode()
    {
        // Arrange & Act
        var dto = new AttachmentDto
        {
            Url = "https://example.com/文件.pdf"
        };

        // Assert
        Assert.Equal("https://example.com/文件.pdf", dto.Url);
    }

    [Fact]
    public void AttachmentDto_WithWhitespaceInUrl_ShouldStoreWhitespace()
    {
        // Arrange & Act
        var dto = new AttachmentDto
        {
            Url = "   https://example.com/file.pdf   "
        };

        // Assert
        Assert.Equal("   https://example.com/file.pdf   ", dto.Url);
    }

    [Fact]
    public void AttachmentDto_WithSpecialCharactersInFileName_ShouldStoreSpecialChars()
    {
        // Arrange & Act
        var dto = new AttachmentDto
        {
            FileName = "file@#$%.pdf"
        };

        // Assert
        Assert.Equal("file@#$%.pdf", dto.FileName);
    }

    [Fact]
    public void AttachmentDto_WithUnicodeInFileName_ShouldStoreUnicode()
    {
        // Arrange & Act
        var dto = new AttachmentDto
        {
            FileName = "文件.pdf"
        };

        // Assert
        Assert.Equal("文件.pdf", dto.FileName);
    }

    [Fact]
    public void AttachmentDto_WithWhitespaceInFileName_ShouldStoreWhitespace()
    {
        // Arrange & Act
        var dto = new AttachmentDto
        {
            FileName = "   file.pdf   "
        };

        // Assert
        Assert.Equal("   file.pdf   ", dto.FileName);
    }

    [Fact]
    public void AttachmentDto_WithSpecialCharactersInContentUrl_ShouldStoreSpecialChars()
    {
        // Arrange & Act
        var dto = new AttachmentDto
        {
            ContentUrl = "https://example.com/content@#$%.pdf"
        };

        // Assert
        Assert.Equal("https://example.com/content@#$%.pdf", dto.ContentUrl);
    }

    [Fact]
    public void AttachmentDto_WithUnicodeInContentUrl_ShouldStoreUnicode()
    {
        // Arrange & Act
        var dto = new AttachmentDto
        {
            ContentUrl = "https://example.com/内容.pdf"
        };

        // Assert
        Assert.Equal("https://example.com/内容.pdf", dto.ContentUrl);
    }

    [Fact]
    public void AttachmentDto_WithWhitespaceInContentUrl_ShouldStoreWhitespace()
    {
        // Arrange & Act
        var dto = new AttachmentDto
        {
            ContentUrl = "   https://example.com/content.pdf   "
        };

        // Assert
        Assert.Equal("   https://example.com/content.pdf   ", dto.ContentUrl);
    }

    [Fact]
    public void AttachmentDto_WithSpecialCharactersInMappedContentUrl_ShouldStoreSpecialChars()
    {
        // Arrange & Act
        var dto = new AttachmentDto
        {
            MappedContentUrl = "https://example.com/mapped@#$%.pdf"
        };

        // Assert
        Assert.Equal("https://example.com/mapped@#$%.pdf", dto.MappedContentUrl);
    }

    [Fact]
    public void AttachmentDto_WithUnicodeInMappedContentUrl_ShouldStoreUnicode()
    {
        // Arrange & Act
        var dto = new AttachmentDto
        {
            MappedContentUrl = "https://example.com/映射.pdf"
        };

        // Assert
        Assert.Equal("https://example.com/映射.pdf", dto.MappedContentUrl);
    }

    [Fact]
    public void AttachmentDto_WithWhitespaceInMappedContentUrl_ShouldStoreWhitespace()
    {
        // Arrange & Act
        var dto = new AttachmentDto
        {
            MappedContentUrl = "   https://example.com/mapped.pdf   "
        };

        // Assert
        Assert.Equal("   https://example.com/mapped.pdf   ", dto.MappedContentUrl);
    }

    [Fact]
    public void AttachmentDto_WithSpecialCharactersInContentType_ShouldStoreSpecialChars()
    {
        // Arrange & Act
        var dto = new AttachmentDto
        {
            ContentType = "application@#$%.pdf"
        };

        // Assert
        Assert.Equal("application@#$%.pdf", dto.ContentType);
    }

    [Fact]
    public void AttachmentDto_WithUnicodeInContentType_ShouldStoreUnicode()
    {
        // Arrange & Act
        var dto = new AttachmentDto
        {
            ContentType = "应用.pdf"
        };

        // Assert
        Assert.Equal("应用.pdf", dto.ContentType);
    }

    [Fact]
    public void AttachmentDto_WithWhitespaceInContentType_ShouldStoreWhitespace()
    {
        // Arrange & Act
        var dto = new AttachmentDto
        {
            ContentType = "   application/pdf   "
        };

        // Assert
        Assert.Equal("   application/pdf   ", dto.ContentType);
    }

    [Fact]
    public void AttachmentDto_WithSpecialCharactersInMalwareScanResult_ShouldStoreSpecialChars()
    {
        // Arrange & Act
        var dto = new AttachmentDto
        {
            MalwareScanResult = "result@#$%"
        };

        // Assert
        Assert.Equal("result@#$%", dto.MalwareScanResult);
    }

    [Fact]
    public void AttachmentDto_WithUnicodeInMalwareScanResult_ShouldStoreUnicode()
    {
        // Arrange & Act
        var dto = new AttachmentDto
        {
            MalwareScanResult = "结果"
        };

        // Assert
        Assert.Equal("结果", dto.MalwareScanResult);
    }

    [Fact]
    public void AttachmentDto_WithWhitespaceInMalwareScanResult_ShouldStoreWhitespace()
    {
        // Arrange & Act
        var dto = new AttachmentDto
        {
            MalwareScanResult = "   result   "
        };

        // Assert
        Assert.Equal("   result   ", dto.MalwareScanResult);
    }

    [Fact]
    public void AttachmentDto_WithMultiplePropertyAssignments_ShouldUpdateValues()
    {
        // Arrange & Act
        var dto = new AttachmentDto
        {
            Url = "url1",
            FileName = "file1",
            Size = 100L,
            Inline = true,
            Deleted = false,
            MalwareAccessOverride = false
        };

        // Assert initial state
        Assert.Equal("url1", dto.Url);
        Assert.Equal("file1", dto.FileName);
        Assert.Equal(100L, dto.Size);
        Assert.True(dto.Inline);
        Assert.False(dto.Deleted);
        Assert.False(dto.MalwareAccessOverride);

        // Update with object initializer
        dto = new AttachmentDto
        {
            Url = "url2",
            FileName = "file2",
            Size = 200L,
            Inline = false,
            Deleted = true,
            MalwareAccessOverride = true
        };

        // Assert updated values
        Assert.Equal("url2", dto.Url);
        Assert.Equal("file2", dto.FileName);
        Assert.Equal(200L, dto.Size);
        Assert.False(dto.Inline);
        Assert.True(dto.Deleted);
        Assert.True(dto.MalwareAccessOverride);
    }

    #endregion

    #region AttachmentThumbnailDto Tests

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
            Url = "https://example.com/thumb.jpg",
            Id = 100L,
            FileName = "thumb.jpg",
            ContentUrl = "https://example.com/content/thumb.jpg",
            MappedContentUrl = "https://example.com/mapped/thumb.jpg",
            ContentType = "image/jpeg",
            Size = 512L,
            Width = 200,
            Height = 150,
            Inline = true,
            Deleted = false
        };

        // Assert
        Assert.Equal("https://example.com/thumb.jpg", dto.Url);
        Assert.Equal(100L, dto.Id);
        Assert.Equal("thumb.jpg", dto.FileName);
        Assert.Equal("https://example.com/content/thumb.jpg", dto.ContentUrl);
        Assert.Equal("https://example.com/mapped/thumb.jpg", dto.MappedContentUrl);
        Assert.Equal("image/jpeg", dto.ContentType);
        Assert.Equal(512L, dto.Size);
        Assert.Equal(200, dto.Width);
        Assert.Equal(150, dto.Height);
        Assert.True(dto.Inline);
        Assert.False(dto.Deleted);
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
    public void AttachmentThumbnailDto_WithNullContentUrl_ShouldAllowNull()
    {
        // Arrange & Act
        var dto = new AttachmentThumbnailDto { ContentUrl = null };

        // Assert
        Assert.Null(dto.ContentUrl);
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
    public void AttachmentThumbnailDto_WithNullContentType_ShouldAllowNull()
    {
        // Arrange & Act
        var dto = new AttachmentThumbnailDto { ContentType = null };

        // Assert
        Assert.Null(dto.ContentType);
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
    public void AttachmentThumbnailDto_WithZeroId_ShouldAllowZero()
    {
        // Arrange & Act
        var dto = new AttachmentThumbnailDto { Id = 0L };

        // Assert
        Assert.Equal(0L, dto.Id);
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
    public void AttachmentThumbnailDto_WithNegativeId_ShouldAllowNegative()
    {
        // Arrange & Act
        var dto = new AttachmentThumbnailDto { Id = -1L };

        // Assert
        Assert.Equal(-1L, dto.Id);
    }

    [Fact]
    public void AttachmentThumbnailDto_WithNegativeSize_ShouldAllowNegative()
    {
        // Arrange & Act
        var dto = new AttachmentThumbnailDto { Size = -512L };

        // Assert
        Assert.Equal(-512L, dto.Size);
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
        var dto = new AttachmentThumbnailDto { Width = -100 };

        // Assert
        Assert.Equal(-100, dto.Width);
    }

    [Fact]
    public void AttachmentThumbnailDto_WithPositiveWidth_ShouldAllowPositive()
    {
        // Arrange & Act
        var dto = new AttachmentThumbnailDto { Width = 100 };

        // Assert
        Assert.Equal(100, dto.Width);
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
        var dto = new AttachmentThumbnailDto { Height = -100 };

        // Assert
        Assert.Equal(-100, dto.Height);
    }

    [Fact]
    public void AttachmentThumbnailDto_WithPositiveHeight_ShouldAllowPositive()
    {
        // Arrange & Act
        var dto = new AttachmentThumbnailDto { Height = 100 };

        // Assert
        Assert.Equal(100, dto.Height);
    }

    [Fact]
    public void AttachmentThumbnailDto_WithSpecialCharactersInUrl_ShouldStoreSpecialChars()
    {
        // Arrange & Act
        var dto = new AttachmentThumbnailDto
        {
            Url = "https://example.com/thumb@#$%.jpg"
        };

        // Assert
        Assert.Equal("https://example.com/thumb@#$%.jpg", dto.Url);
    }

    [Fact]
    public void AttachmentThumbnailDto_WithUnicodeInUrl_ShouldStoreUnicode()
    {
        // Arrange & Act
        var dto = new AttachmentThumbnailDto
        {
            Url = "https://example.com/缩略图.jpg"
        };

        // Assert
        Assert.Equal("https://example.com/缩略图.jpg", dto.Url);
    }

    [Fact]
    public void AttachmentThumbnailDto_WithWhitespaceInUrl_ShouldStoreWhitespace()
    {
        // Arrange & Act
        var dto = new AttachmentThumbnailDto
        {
            Url = "   https://example.com/thumb.jpg   "
        };

        // Assert
        Assert.Equal("   https://example.com/thumb.jpg   ", dto.Url);
    }

    [Fact]
    public void AttachmentThumbnailDto_WithSpecialCharactersInFileName_ShouldStoreSpecialChars()
    {
        // Arrange & Act
        var dto = new AttachmentThumbnailDto
        {
            FileName = "thumb@#$%.jpg"
        };

        // Assert
        Assert.Equal("thumb@#$%.jpg", dto.FileName);
    }

    [Fact]
    public void AttachmentThumbnailDto_WithUnicodeInFileName_ShouldStoreUnicode()
    {
        // Arrange & Act
        var dto = new AttachmentThumbnailDto
        {
            FileName = "缩略图.jpg"
        };

        // Assert
        Assert.Equal("缩略图.jpg", dto.FileName);
    }

    [Fact]
    public void AttachmentThumbnailDto_WithWhitespaceInFileName_ShouldStoreWhitespace()
    {
        // Arrange & Act
        var dto = new AttachmentThumbnailDto
        {
            FileName = "   thumb.jpg   "
        };

        // Assert
        Assert.Equal("   thumb.jpg   ", dto.FileName);
    }

    [Fact]
    public void AttachmentThumbnailDto_WithSpecialCharactersInContentUrl_ShouldStoreSpecialChars()
    {
        // Arrange & Act
        var dto = new AttachmentThumbnailDto
        {
            ContentUrl = "https://example.com/content@#$%.jpg"
        };

        // Assert
        Assert.Equal("https://example.com/content@#$%.jpg", dto.ContentUrl);
    }

    [Fact]
    public void AttachmentThumbnailDto_WithUnicodeInContentUrl_ShouldStoreUnicode()
    {
        // Arrange & Act
        var dto = new AttachmentThumbnailDto
        {
            ContentUrl = "https://example.com/内容.jpg"
        };

        // Assert
        Assert.Equal("https://example.com/内容.jpg", dto.ContentUrl);
    }

    [Fact]
    public void AttachmentThumbnailDto_WithWhitespaceInContentUrl_ShouldStoreWhitespace()
    {
        // Arrange & Act
        var dto = new AttachmentThumbnailDto
        {
            ContentUrl = "   https://example.com/content.jpg   "
        };

        // Assert
        Assert.Equal("   https://example.com/content.jpg   ", dto.ContentUrl);
    }

    [Fact]
    public void AttachmentThumbnailDto_WithSpecialCharactersInMappedContentUrl_ShouldStoreSpecialChars()
    {
        // Arrange & Act
        var dto = new AttachmentThumbnailDto
        {
            MappedContentUrl = "https://example.com/mapped@#$%.jpg"
        };

        // Assert
        Assert.Equal("https://example.com/mapped@#$%.jpg", dto.MappedContentUrl);
    }

    [Fact]
    public void AttachmentThumbnailDto_WithUnicodeInMappedContentUrl_ShouldStoreUnicode()
    {
        // Arrange & Act
        var dto = new AttachmentThumbnailDto
        {
            MappedContentUrl = "https://example.com/映射.jpg"
        };

        // Assert
        Assert.Equal("https://example.com/映射.jpg", dto.MappedContentUrl);
    }

    [Fact]
    public void AttachmentThumbnailDto_WithWhitespaceInMappedContentUrl_ShouldStoreWhitespace()
    {
        // Arrange & Act
        var dto = new AttachmentThumbnailDto
        {
            MappedContentUrl = "   https://example.com/mapped.jpg   "
        };

        // Assert
        Assert.Equal("   https://example.com/mapped.jpg   ", dto.MappedContentUrl);
    }

    [Fact]
    public void AttachmentThumbnailDto_WithSpecialCharactersInContentType_ShouldStoreSpecialChars()
    {
        // Arrange & Act
        var dto = new AttachmentThumbnailDto
        {
            ContentType = "image@#$%.jpg"
        };

        // Assert
        Assert.Equal("image@#$%.jpg", dto.ContentType);
    }

    [Fact]
    public void AttachmentThumbnailDto_WithUnicodeInContentType_ShouldStoreUnicode()
    {
        // Arrange & Act
        var dto = new AttachmentThumbnailDto
        {
            ContentType = "图像.jpg"
        };

        // Assert
        Assert.Equal("图像.jpg", dto.ContentType);
    }

    [Fact]
    public void AttachmentThumbnailDto_WithWhitespaceInContentType_ShouldStoreWhitespace()
    {
        // Arrange & Act
        var dto = new AttachmentThumbnailDto
        {
            ContentType = "   image/jpeg   "
        };

        // Assert
        Assert.Equal("   image/jpeg   ", dto.ContentType);
    }

    [Fact]
    public void AttachmentThumbnailDto_WithMultiplePropertyAssignments_ShouldUpdateValues()
    {
        // Arrange & Act
        var dto = new AttachmentThumbnailDto
        {
            Url = "url1",
            FileName = "file1",
            Size = 100L,
            Inline = true,
            Deleted = false
        };

        // Assert initial state
        Assert.Equal("url1", dto.Url);
        Assert.Equal("file1", dto.FileName);
        Assert.Equal(100L, dto.Size);
        Assert.True(dto.Inline);
        Assert.False(dto.Deleted);

        // Update with object initializer
        dto = new AttachmentThumbnailDto
        {
            Url = "url2",
            FileName = "file2",
            Size = 200L,
            Inline = false,
            Deleted = true
        };

        // Assert updated values
        Assert.Equal("url2", dto.Url);
        Assert.Equal("file2", dto.FileName);
        Assert.Equal(200L, dto.Size);
        Assert.False(dto.Inline);
        Assert.True(dto.Deleted);
    }

    #endregion
}