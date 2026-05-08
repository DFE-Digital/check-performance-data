using System;
using System.Collections.Generic;
using Xunit;
using DfE.CheckPerformanceData.Application.ZendeskClient;

namespace DfE.CheckPerformanceData.UnitTests.ZendeskClient;

/// <summary>
/// Unit tests for CreateTicketRequestDto and CreateTicketDto classes.
/// </summary>
public class CreateTicketRequestDtoTests
{
    #region CreateTicketRequestDto Tests

    [Fact]
    public void CreateTicketRequestDto_DefaultConstructor_ShouldInitializeTicket()
    {
        // Arrange & Act
        var dto = new CreateTicketRequestDto();

        // Assert
        Assert.NotNull(dto.Ticket);
        Assert.Equal(string.Empty, dto.Ticket.Subject);
        Assert.Equal(string.Empty, dto.Ticket.Status);
        Assert.Null(dto.Ticket.GroupId);
        Assert.Equal(string.Empty, dto.Ticket.Type);
        Assert.Null(dto.Ticket.Comment);
        Assert.Null(dto.Ticket.RequesterId);
        Assert.Equal(string.Empty, dto.Ticket.Priority);
        Assert.Equal(string.Empty, dto.Ticket.Description);
        Assert.Empty(dto.Ticket.CustomFields);
        Assert.Null(dto.Ticket.BrandId);
    }

    [Fact]
    public void CreateTicketRequestDto_WithTicket_ShouldSetTicket()
    {
        // Arrange
        var dto = new CreateTicketRequestDto();
        var ticket = new CreateTicketDto
        {
            Subject = "Test Subject",
            Status = "open",
            GroupId = 123L,
            Type = "task",
            Priority = "high",
            Description = "Test description"
        };

        // Act
        dto.Ticket = ticket;

        // Assert
        Assert.Equal(ticket, dto.Ticket);
        Assert.Equal("Test Subject", dto.Ticket.Subject);
        Assert.Equal("open", dto.Ticket.Status);
        Assert.Equal(123L, dto.Ticket.GroupId);
        Assert.Equal("task", dto.Ticket.Type);
        Assert.Equal("high", dto.Ticket.Priority);
        Assert.Equal("Test description", dto.Ticket.Description);
    }

    [Fact]
    public void CreateTicketRequestDto_WithNullTicket_ShouldAllowNull()
    {
        // Arrange
        var dto = new CreateTicketRequestDto();
        dto.Ticket = new CreateTicketDto();

        // Act
        dto.Ticket = null;

        // Assert
        Assert.Null(dto.Ticket);
    }

    [Fact]
    public void CreateTicketRequestDto_WithMultipleTicketAssignments_ShouldUpdateValue()
    {
        // Arrange
        var dto = new CreateTicketRequestDto();
        var ticket1 = new CreateTicketDto { Subject = "Subject 1" };
        var ticket2 = new CreateTicketDto { Subject = "Subject 2" };

        // Act
        dto.Ticket = ticket1;
        dto.Ticket = ticket2;

        // Assert
        Assert.Equal(ticket2, dto.Ticket);
        Assert.Equal("Subject 2", dto.Ticket.Subject);
    }

    #endregion

    #region CreateTicketDto Tests

    [Fact]
    public void CreateTicketDto_DefaultConstructor_ShouldInitializeProperties()
    {
        // Arrange & Act
        var dto = new CreateTicketDto();

        // Assert
        Assert.Equal(string.Empty, dto.Subject);
        Assert.Equal(string.Empty, dto.Status);
        Assert.Null(dto.GroupId);
        Assert.Equal(string.Empty, dto.Type);
        Assert.Null(dto.Comment);
        Assert.Null(dto.RequesterId);
        Assert.Equal(string.Empty, dto.Priority);
        Assert.Equal(string.Empty, dto.Description);
        Assert.Empty(dto.CustomFields);
        Assert.Null(dto.BrandId);
    }

    [Fact]
    public void CreateTicketDto_WithAllProperties_ShouldSetAllProperties()
    {
        // Arrange
        var dto = new CreateTicketDto();
        var comment = new TicketCommentDto
        {
            Id = 1L,
            Body = "Test comment",
            Public = true,
            CreatedAt = DateTime.UtcNow
        };

        // Act
        dto.Subject = "Test Subject";
        dto.Status = "open";
        dto.GroupId = 123L;
        dto.Type = "task";
        dto.Comment = comment;
        dto.RequesterId = 456L;
        dto.Priority = "high";
        dto.Description = "Test description";
        dto.BrandId = 789L;

        // Assert
        Assert.Equal("Test Subject", dto.Subject);
        Assert.Equal("open", dto.Status);
        Assert.Equal(123L, dto.GroupId);
        Assert.Equal("task", dto.Type);
        Assert.Equal(comment, dto.Comment);
        Assert.Equal(456L, dto.RequesterId);
        Assert.Equal("high", dto.Priority);
        Assert.Equal("Test description", dto.Description);
        Assert.Equal(789L, dto.BrandId);
    }

    [Fact]
    public void CreateTicketDto_WithEmptyStrings_ShouldSetEmptyStrings()
    {
        // Arrange
        var dto = new CreateTicketDto();

        // Act
        dto.Subject = string.Empty;
        dto.Status = string.Empty;
        dto.Type = string.Empty;
        dto.Priority = string.Empty;
        dto.Description = string.Empty;

        // Assert
        Assert.Equal(string.Empty, dto.Subject);
        Assert.Equal(string.Empty, dto.Status);
        Assert.Equal(string.Empty, dto.Type);
        Assert.Equal(string.Empty, dto.Priority);
        Assert.Equal(string.Empty, dto.Description);
    }

    [Fact]
    public void CreateTicketDto_WithNullComment_ShouldAllowNull()
    {
        // Arrange
        var dto = new CreateTicketDto();
        dto.Comment = new TicketCommentDto();

        // Act
        dto.Comment = null;

        // Assert
        Assert.Null(dto.Comment);
    }

    [Fact]
    public void CreateTicketDto_WithNullGroupId_ShouldAllowNull()
    {
        // Arrange
        var dto = new CreateTicketDto();

        // Act
        dto.GroupId = null;

        // Assert
        Assert.Null(dto.GroupId);
    }

    [Fact]
    public void CreateTicketDto_WithNullRequesterId_ShouldAllowNull()
    {
        // Arrange
        var dto = new CreateTicketDto();

        // Act
        dto.RequesterId = null;

        // Assert
        Assert.Null(dto.RequesterId);
    }

    [Fact]
    public void CreateTicketDto_WithNullBrandId_ShouldAllowNull()
    {
        // Arrange
        var dto = new CreateTicketDto();

        // Act
        dto.BrandId = null;

        // Assert
        Assert.Null(dto.BrandId);
    }

    [Fact]
    public void CreateTicketDto_WithZeroGroupId_ShouldAllowZero()
    {
        // Arrange
        var dto = new CreateTicketDto();

        // Act
        dto.GroupId = 0L;

        // Assert
        Assert.Equal(0L, dto.GroupId);
    }

    [Fact]
    public void CreateTicketDto_WithZeroRequesterId_ShouldAllowZero()
    {
        // Arrange
        var dto = new CreateTicketDto();

        // Act
        dto.RequesterId = 0L;

        // Assert
        Assert.Equal(0L, dto.RequesterId);
    }

    [Fact]
    public void CreateTicketDto_WithZeroBrandId_ShouldAllowZero()
    {
        // Arrange
        var dto = new CreateTicketDto();

        // Act
        dto.BrandId = 0L;

        // Assert
        Assert.Equal(0L, dto.BrandId);
    }

    [Fact]
    public void CreateTicketDto_WithNegativeGroupId_ShouldAllowNegative()
    {
        // Arrange
        var dto = new CreateTicketDto();

        // Act
        dto.GroupId = -1L;

        // Assert
        Assert.Equal(-1L, dto.GroupId);
    }

    [Fact]
    public void CreateTicketDto_WithNegativeRequesterId_ShouldAllowNegative()
    {
        // Arrange
        var dto = new CreateTicketDto();

        // Act
        dto.RequesterId = -1L;

        // Assert
        Assert.Equal(-1L, dto.RequesterId);
    }

    [Fact]
    public void CreateTicketDto_WithNegativeBrandId_ShouldAllowNegative()
    {
        // Arrange
        var dto = new CreateTicketDto();

        // Act
        dto.BrandId = -1L;

        // Assert
        Assert.Equal(-1L, dto.BrandId);
    }

    [Fact]
    public void CreateTicketDto_WithMaxGroupId_ShouldAllowMaxValue()
    {
        // Arrange
        var dto = new CreateTicketDto();

        // Act
        dto.GroupId = long.MaxValue;

        // Assert
        Assert.Equal(long.MaxValue, dto.GroupId);
    }

    [Fact]
    public void CreateTicketDto_WithMaxRequesterId_ShouldAllowMaxValue()
    {
        // Arrange
        var dto = new CreateTicketDto();

        // Act
        dto.RequesterId = long.MaxValue;

        // Assert
        Assert.Equal(long.MaxValue, dto.RequesterId);
    }

    [Fact]
    public void CreateTicketDto_WithMaxBrandId_ShouldAllowMaxValue()
    {
        // Arrange
        var dto = new CreateTicketDto();

        // Act
        dto.BrandId = long.MaxValue;

        // Assert
        Assert.Equal(long.MaxValue, dto.BrandId);
    }

    [Fact]
    public void CreateTicketDto_WithCustomFields_ShouldPopulateList()
    {
        // Arrange
        var dto = new CreateTicketDto();
        var customFields = new List<CustomFieldDto>
        {
            new() { Id = 1L, Value = "value1" },
            new() { Id = 2L, Value = "value2" }
        };

        // Act
        dto.CustomFields = customFields;

        // Assert
        Assert.Equal(2, dto.CustomFields.Count);
        Assert.Equal(1L, dto.CustomFields[0].Id);
        Assert.Equal("value1", dto.CustomFields[0].Value);
        Assert.Equal(2L, dto.CustomFields[1].Id);
        Assert.Equal("value2", dto.CustomFields[1].Value);
    }

    [Fact]
    public void CreateTicketDto_WithEmptyCustomFields_ShouldInitializeEmptyList()
    {
        // Arrange
        var dto = new CreateTicketDto();

        // Act
        dto.CustomFields = new List<CustomFieldDto>();

        // Assert
        Assert.Empty(dto.CustomFields);
    }

    [Fact]
    public void CreateTicketDto_WithNullCustomFields_ShouldAllowNull()
    {
        // Arrange
        var dto = new CreateTicketDto();
        dto.CustomFields = new List<CustomFieldDto>();

        // Act
        dto.CustomFields = null;

        // Assert
        Assert.Null(dto.CustomFields);
    }

    [Fact]
    public void CreateTicketDto_WithSpecialCharactersInSubject_ShouldStoreSpecialChars()
    {
        // Arrange
        var dto = new CreateTicketDto();

        // Act
        dto.Subject = "Subject with special chars: @#$%^&*()";

        // Assert
        Assert.Equal("Subject with special chars: @#$%^&*()", dto.Subject);
    }

    [Fact]
    public void CreateTicketDto_WithUnicodeInDescription_ShouldStoreUnicode()
    {
        // Arrange
        var dto = new CreateTicketDto();

        // Act
        dto.Description = "Description with unicode: 你好世界 🌍";

        // Assert
        Assert.Equal("Description with unicode: 你好世界 🌍", dto.Description);
    }

    [Fact]
    public void CreateTicketDto_WithNumberInStatus_ShouldStoreNumber()
    {
        // Arrange
        var dto = new CreateTicketDto();

        // Act
        dto.Status = "123";

        // Assert
        Assert.Equal("123", dto.Status);
    }

    [Fact]
    public void CreateTicketDto_WithWhitespaceInSubject_ShouldStoreWhitespace()
    {
        // Arrange
        var dto = new CreateTicketDto();

        // Act
        dto.Subject = "   Subject with spaces   ";

        // Assert
        Assert.Equal("   Subject with spaces   ", dto.Subject);
    }

    [Fact]
    public void CreateTicketDto_WithNullSubject_ShouldAllowNull()
    {
        // Arrange
        var dto = new CreateTicketDto();

        // Act
        dto.Subject = null;

        // Assert
        Assert.Null(dto.Subject);
    }

    [Fact]
    public void CreateTicketDto_WithNullStatus_ShouldAllowNull()
    {
        // Arrange
        var dto = new CreateTicketDto();

        // Act
        dto.Status = null;

        // Assert
        Assert.Null(dto.Status);
    }

    [Fact]
    public void CreateTicketDto_WithNullType_ShouldAllowNull()
    {
        // Arrange
        var dto = new CreateTicketDto();

        // Act
        dto.Type = null;

        // Assert
        Assert.Null(dto.Type);
    }

    [Fact]
    public void CreateTicketDto_WithNullPriority_ShouldAllowNull()
    {
        // Arrange
        var dto = new CreateTicketDto();

        // Act
        dto.Priority = null;

        // Assert
        Assert.Null(dto.Priority);
    }

    [Fact]
    public void CreateTicketDto_WithNullDescription_ShouldAllowNull()
    {
        // Arrange
        var dto = new CreateTicketDto();

        // Act
        dto.Description = null;

        // Assert
        Assert.Null(dto.Description);
    }

    [Fact]
    public void CreateTicketDto_WithMultipleCustomFieldAssignments_ShouldUpdateValue()
    {
        // Arrange
        var dto = new CreateTicketDto();

        // Act
        dto.CustomFields = new List<CustomFieldDto> { new() { Id = 1L, Value = "value1" } };
        dto.CustomFields = new List<CustomFieldDto> { new() { Id = 2L, Value = "value2" } };
        dto.CustomFields = new List<CustomFieldDto> { new() { Id = 3L, Value = "value3" } };

        // Assert
        Assert.Single(dto.CustomFields);
        Assert.Equal(3L, dto.CustomFields[0].Id);
        Assert.Equal("value3", dto.CustomFields[0].Value);
    }

    #endregion

    #region Nested Ticket Property Tests

    [Fact]
    public void CreateTicketRequestDto_WithNestedTicket_ShouldSetNestedProperties()
    {
        // Arrange
        var dto = new CreateTicketRequestDto();
        var ticket = new CreateTicketDto
        {
            Subject = "Test Subject",
            Status = "open",
            Priority = "high"
        };

        // Act
        dto.Ticket = ticket;

        // Assert
        Assert.NotNull(dto.Ticket);
        Assert.Equal("Test Subject", dto.Ticket.Subject);
        Assert.Equal("open", dto.Ticket.Status);
        Assert.Equal("high", dto.Ticket.Priority);
    }

    [Fact]
    public void CreateTicketRequestDto_WithNestedTicketAndComment_ShouldSetComment()
    {
        // Arrange
        var dto = new CreateTicketRequestDto();
        var ticket = new CreateTicketDto
        {
            Comment = new TicketCommentDto
            {
                Id = 1L,
                Body = "Test comment",
                Public = true
            }
        };

        // Act
        dto.Ticket = ticket;

        // Assert
        Assert.NotNull(dto.Ticket);
        Assert.NotNull(dto.Ticket.Comment);
        Assert.Equal(1L, dto.Ticket.Comment.Id);
        Assert.Equal("Test comment", dto.Ticket.Comment.Body);
        Assert.True(dto.Ticket.Comment.Public);
    }

    [Fact]
    public void CreateTicketRequestDto_WithNestedTicketAndCustomFields_ShouldSetCustomFields()
    {
        // Arrange
        var dto = new CreateTicketRequestDto();
        var ticket = new CreateTicketDto
        {
            CustomFields = new List<CustomFieldDto>
            {
                new() { Id = 1L, Value = "value1" },
                new() { Id = 2L, Value = "value2" }
            }
        };

        // Act
        dto.Ticket = ticket;

        // Assert
        Assert.NotNull(dto.Ticket);
        Assert.Equal(2, dto.Ticket.CustomFields.Count);
        Assert.Equal(1L, dto.Ticket.CustomFields[0].Id);
        Assert.Equal("value1", dto.Ticket.CustomFields[0].Value);
    }

    #endregion
}