using System;
using System.Collections.Generic;
using Xunit;
using DfE.CheckPerformanceData.Application.ZendeskClient;

namespace DfE.CheckPerformanceData.UnitTests.ZendeskClient;

/// <summary>
/// Unit tests for UpdateTicketResponseDto, UpdatedTicketDto, TicketAuditDto, and TicketAuditEventDto classes.
/// </summary>
public class UpdateTicketResponseDtoTests
{
    #region UpdateTicketResponseDto Tests

    [Fact]
    public void UpdateTicketResponseDto_DefaultConstructor_ShouldInitializeProperties()
    {
        // Arrange & Act
        var dto = new UpdateTicketResponseDto();

        // Assert
        Assert.NotNull(dto.Ticket);
        Assert.NotNull(dto.Audit);
    }

    [Fact]
    public void UpdateTicketResponseDto_WithCustomTickets_ShouldSetProperties()
    {
        // Arrange
        var ticket = new UpdatedTicketDto
        {
            Id = 123L,
            Status = "open",
            UpdatedAt = new DateTime(2024, 1, 1),
            Comment = new TicketCommentDto { Id = 456L, Body = "Test comment" }
        };

        var audit = new TicketAuditDto
        {
            Id = 789L,
            TicketId = 123L,
            CreatedAt = new DateTime(2024, 1, 2),
            AuthorId = 999L,
            Events = new List<TicketAuditEventDto>
            {
                new() { Id = 1L, Type = "system", Body = "Event 1" }
            }
        };

        var dto = new UpdateTicketResponseDto
        {
            Ticket = ticket,
            Audit = audit
        };

        // Assert
        Assert.NotNull(dto.Ticket);
        Assert.NotNull(dto.Audit);
        Assert.Equal(123L, dto.Ticket.Id);
        Assert.Equal("open", dto.Ticket.Status);
        Assert.Equal(new DateTime(2024, 1, 1), dto.Ticket.UpdatedAt);
        Assert.NotNull(dto.Ticket.Comment);
        Assert.Equal(789L, dto.Audit.Id);
        Assert.Equal(123L, dto.Audit.TicketId);
        Assert.Equal(new DateTime(2024, 1, 2), dto.Audit.CreatedAt);
        Assert.Equal(999L, dto.Audit.AuthorId);
        Assert.Single(dto.Audit.Events);
    }

    [Fact]
    public void UpdateTicketResponseDto_WithNullTicket_ShouldAllowNull()
    {
        // Arrange & Act
        var dto = new UpdateTicketResponseDto { Ticket = null };

        // Assert
        Assert.Null(dto.Ticket);
        Assert.NotNull(dto.Audit); // Audit should still be initialized
    }

    [Fact]
    public void UpdateTicketResponseDto_WithNullAudit_ShouldAllowNull()
    {
        // Arrange & Act
        var dto = new UpdateTicketResponseDto { Audit = null };

        // Assert
        Assert.NotNull(dto.Ticket); // Ticket should still be initialized
        Assert.Null(dto.Audit);
    }

    [Fact]
    public void UpdateTicketResponseDto_WithEmptyTicket_ShouldInitializeEmptyTicket()
    {
        // Arrange & Act
        var dto = new UpdateTicketResponseDto { Ticket = new UpdatedTicketDto() };

        // Assert
        Assert.NotNull(dto.Ticket);
        Assert.Equal(0L, dto.Ticket.Id);
        Assert.Equal(string.Empty, dto.Ticket.Status);
        Assert.Equal(default, dto.Ticket.UpdatedAt);
        Assert.Null(dto.Ticket.Comment);
    }

    [Fact]
    public void UpdateTicketResponseDto_WithEmptyAudit_ShouldInitializeEmptyAudit()
    {
        // Arrange & Act
        var dto = new UpdateTicketResponseDto { Audit = new TicketAuditDto() };

        // Assert
        Assert.NotNull(dto.Audit);
        Assert.Equal(0L, dto.Audit.Id);
        Assert.Equal(0L, dto.Audit.TicketId);
        Assert.Equal(default, dto.Audit.CreatedAt);
        Assert.Equal(0L, dto.Audit.AuthorId);
        Assert.Empty(dto.Audit.Events);
    }

    [Fact]
    public void UpdateTicketResponseDto_WithZeroId_ShouldAllowZero()
    {
        // Arrange
        var dto = new UpdateTicketResponseDto
        {
            Ticket = new UpdatedTicketDto { Id = 0L }
        };

        // Assert
        Assert.Equal(0L, dto.Ticket.Id);
    }

    [Fact]
    public void UpdateTicketResponseDto_WithNegativeId_ShouldAllowNegative()
    {
        // Arrange
        var dto = new UpdateTicketResponseDto
        {
            Ticket = new UpdatedTicketDto { Id = -1L }
        };

        // Assert
        Assert.Equal(-1L, dto.Ticket.Id);
    }

    [Fact]
    public void UpdateTicketResponseDto_WithMaxId_ShouldAllowMaxValue()
    {
        // Arrange
        var dto = new UpdateTicketResponseDto
        {
            Ticket = new UpdatedTicketDto { Id = long.MaxValue }
        };

        // Assert
        Assert.Equal(long.MaxValue, dto.Ticket.Id);
    }

    [Fact]
    public void UpdateTicketResponseDto_WithEmptyStatus_ShouldSetEmptyString()
    {
        // Arrange
        var dto = new UpdateTicketResponseDto
        {
            Ticket = new UpdatedTicketDto { Status = string.Empty }
        };

        // Assert
        Assert.Equal(string.Empty, dto.Ticket.Status);
    }

    [Fact]
    public void UpdateTicketResponseDto_WithNullStatus_ShouldAllowNull()
    {
        // Arrange
        var dto = new UpdateTicketResponseDto
        {
            Ticket = new UpdatedTicketDto { Status = null }
        };

        // Assert
        Assert.Null(dto.Ticket.Status);
    }

    [Fact]
    public void UpdateTicketResponseDto_WithSpecialCharactersInStatus_ShouldStoreSpecialChars()
    {
        // Arrange
        var dto = new UpdateTicketResponseDto
        {
            Ticket = new UpdatedTicketDto { Status = "Status: /?\\|<>()[]{}" }
        };

        // Assert
        Assert.Equal("Status: /?\\|<>()[]{}", dto.Ticket.Status);
    }

    [Fact]
    public void UpdateTicketResponseDto_WithUnicodeInStatus_ShouldStoreUnicode()
    {
        // Arrange
        var dto = new UpdateTicketResponseDto
        {
            Ticket = new UpdatedTicketDto { Status = "状态: 开放" }
        };

        // Assert
        Assert.Equal("状态: 开放", dto.Ticket.Status);
    }

    [Fact]
    public void UpdateTicketResponseDto_WithWhitespaceInStatus_ShouldStoreWhitespace()
    {
        // Arrange
        var dto = new UpdateTicketResponseDto
        {
            Ticket = new UpdatedTicketDto { Status = "   Status with spaces   " }
        };

        // Assert
        Assert.Equal("   Status with spaces   ", dto.Ticket.Status);
    }

    [Fact]
    public void UpdateTicketResponseDto_WithMinDateTime_ShouldAllowMinValue()
    {
        // Arrange
        var dto = new UpdateTicketResponseDto
        {
            Ticket = new UpdatedTicketDto { UpdatedAt = DateTime.MinValue }
        };

        // Assert
        Assert.Equal(DateTime.MinValue, dto.Ticket.UpdatedAt);
    }

    [Fact]
    public void UpdateTicketResponseDto_WithMaxDateTime_ShouldAllowMaxValue()
    {
        // Arrange
        var dto = new UpdateTicketResponseDto
        {
            Ticket = new UpdatedTicketDto { UpdatedAt = DateTime.MaxValue }
        };

        // Assert
        Assert.Equal(DateTime.MaxValue, dto.Ticket.UpdatedAt);
    }

    [Fact]
    public void UpdateTicketResponseDto_WithUtcDateTime_ShouldStoreUtcTime()
    {
        // Arrange
        var utcTime = DateTime.UtcNow;
        var dto = new UpdateTicketResponseDto
        {
            Ticket = new UpdatedTicketDto { UpdatedAt = utcTime }
        };

        // Assert
        Assert.Equal(utcTime, dto.Ticket.UpdatedAt);
    }

    [Fact]
    public void UpdateTicketResponseDto_WithDifferentTimeZones_ShouldStoreTime()
    {
        // Arrange
        var localTime = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Local);
        var dto = new UpdateTicketResponseDto
        {
            Ticket = new UpdatedTicketDto { UpdatedAt = localTime }
        };

        // Assert
        Assert.Equal(localTime, dto.Ticket.UpdatedAt);
        Assert.NotNull(dto.Ticket.UpdatedAt);
        Assert.Equal(DateTimeKind.Local, dto.Ticket.UpdatedAt.Kind);
    }

    [Fact]
    public void UpdateTicketResponseDto_WithNullComment_ShouldAllowNull()
    {
        // Arrange
        var dto = new UpdateTicketResponseDto
        {
            Ticket = new UpdatedTicketDto { Comment = null }
        };

        // Assert
        Assert.Null(dto.Ticket.Comment);
    }

    [Fact]
    public void UpdateTicketResponseDto_WithEmptyComment_ShouldInitializeEmptyComment()
    {
        // Arrange
        var dto = new UpdateTicketResponseDto
        {
            Ticket = new UpdatedTicketDto { Comment = new TicketCommentDto() }
        };

        // Assert
        Assert.NotNull(dto.Ticket.Comment);
        Assert.Equal(0L, dto.Ticket.Comment.Id);
        Assert.Equal(string.Empty, dto.Ticket.Comment.Body);
    }

    [Fact]
    public void UpdateTicketResponseDto_WithZeroTicketId_ShouldAllowZero()
    {
        // Arrange
        var dto = new UpdateTicketResponseDto
        {
            Audit = new TicketAuditDto { TicketId = 0L }
        };

        // Assert
        Assert.Equal(0L, dto.Audit.TicketId);
    }

    [Fact]
    public void UpdateTicketResponseDto_WithNegativeTicketId_ShouldAllowNegative()
    {
        // Arrange
        var dto = new UpdateTicketResponseDto
        {
            Audit = new TicketAuditDto { TicketId = -1L }
        };

        // Assert
        Assert.Equal(-1L, dto.Audit.TicketId);
    }

    [Fact]
    public void UpdateTicketResponseDto_WithMaxTicketId_ShouldAllowMaxValue()
    {
        // Arrange
        var dto = new UpdateTicketResponseDto
        {
            Audit = new TicketAuditDto { TicketId = long.MaxValue }
        };

        // Assert
        Assert.Equal(long.MaxValue, dto.Audit.TicketId);
    }

    [Fact]
    public void UpdateTicketResponseDto_WithZeroAuthorId_ShouldAllowZero()
    {
        // Arrange
        var dto = new UpdateTicketResponseDto
        {
            Audit = new TicketAuditDto { AuthorId = 0L }
        };

        // Assert
        Assert.Equal(0L, dto.Audit.AuthorId);
    }

    [Fact]
    public void UpdateTicketResponseDto_WithNegativeAuthorId_ShouldAllowNegative()
    {
        // Arrange
        var dto = new UpdateTicketResponseDto
        {
            Audit = new TicketAuditDto { AuthorId = -1L }
        };

        // Assert
        Assert.Equal(-1L, dto.Audit.AuthorId);
    }

    [Fact]
    public void UpdateTicketResponseDto_WithMaxAuthorId_ShouldAllowMaxValue()
    {
        // Arrange
        var dto = new UpdateTicketResponseDto
        {
            Audit = new TicketAuditDto { AuthorId = long.MaxValue }
        };

        // Assert
        Assert.Equal(long.MaxValue, dto.Audit.AuthorId);
    }

    [Fact]
    public void UpdateTicketResponseDto_WithEmptyEvents_ShouldInitializeEmptyList()
    {
        // Arrange
        var dto = new UpdateTicketResponseDto
        {
            Audit = new TicketAuditDto { Events = new List<TicketAuditEventDto>() }
        };

        // Assert
        Assert.Empty(dto.Audit.Events);
    }

    [Fact]
    public void UpdateTicketResponseDto_WithNullEvents_ShouldAllowNull()
    {
        // Arrange
        var dto = new UpdateTicketResponseDto
        {
            Audit = new TicketAuditDto { Events = null }
        };

        // Assert
        Assert.Null(dto.Audit.Events);
    }

    [Fact]
    public void UpdateTicketResponseDto_WithMultiplePropertyAssignments_ShouldUpdateValues()
    {
        // Arrange
        var dto = new UpdateTicketResponseDto
        {
            Ticket = new UpdatedTicketDto { Id = 1L, Status = "First" },
            Audit = new TicketAuditDto { Id = 1L, TicketId = 1L }
        };

        // Assert initial state
        Assert.Equal(1L, dto.Ticket.Id);
        Assert.Equal("First", dto.Ticket.Status);
        Assert.Equal(1L, dto.Audit.Id);
        Assert.Equal(1L, dto.Audit.TicketId);

        // Update with object initializer
        dto = new UpdateTicketResponseDto
        {
            Ticket = new UpdatedTicketDto { Id = 2L, Status = "Second" },
            Audit = new TicketAuditDto { Id = 2L, TicketId = 2L }
        };

        // Assert updated values
        Assert.Equal(2L, dto.Ticket.Id);
        Assert.Equal("Second", dto.Ticket.Status);
        Assert.Equal(2L, dto.Audit.Id);
        Assert.Equal(2L, dto.Audit.TicketId);
    }

    #endregion

    #region UpdatedTicketDto Tests

    [Fact]
    public void UpdatedTicketDto_DefaultConstructor_ShouldInitializeProperties()
    {
        // Arrange & Act
        var dto = new UpdatedTicketDto();

        // Assert
        Assert.Equal(0L, dto.Id);
        Assert.Equal(string.Empty, dto.Status);
        Assert.Equal(default, dto.UpdatedAt);
        Assert.Null(dto.Comment);
    }

    [Fact]
    public void UpdatedTicketDto_WithAllProperties_ShouldSetAllProperties()
    {
        // Arrange & Act
        var dto = new UpdatedTicketDto
        {
            Id = 123L,
            Status = "open",
            UpdatedAt = new DateTime(2024, 1, 1),
            Comment = new TicketCommentDto { Id = 456L, Body = "Test comment" }
        };

        // Assert
        Assert.Equal(123L, dto.Id);
        Assert.Equal("open", dto.Status);
        Assert.Equal(new DateTime(2024, 1, 1), dto.UpdatedAt);
        Assert.NotNull(dto.Comment);
        Assert.Equal(456L, dto.Comment.Id);
        Assert.Equal("Test comment", dto.Comment.Body);
    }

    [Fact]
    public void UpdatedTicketDto_WithZeroId_ShouldAllowZero()
    {
        // Arrange & Act
        var dto = new UpdatedTicketDto { Id = 0L };

        // Assert
        Assert.Equal(0L, dto.Id);
    }

    [Fact]
    public void UpdatedTicketDto_WithNegativeId_ShouldAllowNegative()
    {
        // Arrange & Act
        var dto = new UpdatedTicketDto { Id = -1L };

        // Assert
        Assert.Equal(-1L, dto.Id);
    }

    [Fact]
    public void UpdatedTicketDto_WithMaxId_ShouldAllowMaxValue()
    {
        // Arrange & Act
        var dto = new UpdatedTicketDto { Id = long.MaxValue };

        // Assert
        Assert.Equal(long.MaxValue, dto.Id);
    }

    [Fact]
    public void UpdatedTicketDto_WithEmptyStatus_ShouldSetEmptyString()
    {
        // Arrange & Act
        var dto = new UpdatedTicketDto { Status = string.Empty };

        // Assert
        Assert.Equal(string.Empty, dto.Status);
    }

    [Fact]
    public void UpdatedTicketDto_WithNullStatus_ShouldAllowNull()
    {
        // Arrange & Act
        var dto = new UpdatedTicketDto { Status = null };

        // Assert
        Assert.Null(dto.Status);
    }

    [Fact]
    public void UpdatedTicketDto_WithSpecialCharactersInStatus_ShouldStoreSpecialChars()
    {
        // Arrange & Act
        var dto = new UpdatedTicketDto { Status = "Status: /?\\|<>()[]{}" };

        // Assert
        Assert.Equal("Status: /?\\|<>()[]{}", dto.Status);
    }

    [Fact]
    public void UpdatedTicketDto_WithUnicodeInStatus_ShouldStoreUnicode()
    {
        // Arrange & Act
        var dto = new UpdatedTicketDto { Status = "状态: 开放" };

        // Assert
        Assert.Equal("状态: 开放", dto.Status);
    }

    [Fact]
    public void UpdatedTicketDto_WithWhitespaceInStatus_ShouldStoreWhitespace()
    {
        // Arrange & Act
        var dto = new UpdatedTicketDto { Status = "   Status with spaces   " };

        // Assert
        Assert.Equal("   Status with spaces   ", dto.Status);
    }

    [Fact]
    public void UpdatedTicketDto_WithMinDateTime_ShouldAllowMinValue()
    {
        // Arrange & Act
        var dto = new UpdatedTicketDto { UpdatedAt = DateTime.MinValue };

        // Assert
        Assert.Equal(DateTime.MinValue, dto.UpdatedAt);
    }

    [Fact]
    public void UpdatedTicketDto_WithMaxDateTime_ShouldAllowMaxValue()
    {
        // Arrange & Act
        var dto = new UpdatedTicketDto { UpdatedAt = DateTime.MaxValue };

        // Assert
        Assert.Equal(DateTime.MaxValue, dto.UpdatedAt);
    }

    [Fact]
    public void UpdatedTicketDto_WithUtcDateTime_ShouldStoreUtcTime()
    {
        // Arrange
        var utcTime = DateTime.UtcNow;
        var dto = new UpdatedTicketDto { UpdatedAt = utcTime };

        // Assert
        Assert.Equal(utcTime, dto.UpdatedAt);
    }

    [Fact]
    public void UpdatedTicketDto_WithDifferentTimeZones_ShouldStoreTime()
    {
        // Arrange
        var localTime = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Local);
        var dto = new UpdatedTicketDto { UpdatedAt = localTime };

        // Assert
        Assert.Equal(localTime, dto.UpdatedAt);
        Assert.NotNull(dto.UpdatedAt);
        Assert.Equal(DateTimeKind.Local, dto.UpdatedAt.Kind);
    }

    [Fact]
    public void UpdatedTicketDto_WithNullComment_ShouldAllowNull()
    {
        // Arrange & Act
        var dto = new UpdatedTicketDto { Comment = null };

        // Assert
        Assert.Null(dto.Comment);
    }

    [Fact]
    public void UpdatedTicketDto_WithEmptyComment_ShouldInitializeEmptyComment()
    {
        // Arrange & Act
        var dto = new UpdatedTicketDto { Comment = new TicketCommentDto() };

        // Assert
        Assert.NotNull(dto.Comment);
        Assert.Equal(0L, dto.Comment.Id);
        Assert.Equal(string.Empty, dto.Comment.Body);
    }

    [Fact]
    public void UpdatedTicketDto_WithMultiplePropertyAssignments_ShouldUpdateValues()
    {
        // Arrange
        var dto = new UpdatedTicketDto
        {
            Id = 1L,
            Status = "First",
            UpdatedAt = DateTime.UtcNow
        };

        // Assert initial state
        Assert.Equal(1L, dto.Id);
        Assert.Equal("First", dto.Status);

        // Update with object initializer
        dto = new UpdatedTicketDto
        {
            Id = 2L,
            Status = "Second",
            UpdatedAt = DateTime.UtcNow.AddDays(1)
        };

        // Assert updated values
        Assert.Equal(2L, dto.Id);
        Assert.Equal("Second", dto.Status);
    }

    #endregion

    #region TicketAuditDto Tests

    [Fact]
    public void TicketAuditDto_DefaultConstructor_ShouldInitializeProperties()
    {
        // Arrange & Act
        var dto = new TicketAuditDto();

        // Assert
        Assert.Equal(0L, dto.Id);
        Assert.Equal(0L, dto.TicketId);
        Assert.Equal(default, dto.CreatedAt);
        Assert.Equal(0L, dto.AuthorId);
        Assert.Empty(dto.Events);
    }

    [Fact]
    public void TicketAuditDto_WithAllProperties_ShouldSetAllProperties()
    {
        // Arrange & Act
        var dto = new TicketAuditDto
        {
            Id = 123L,
            TicketId = 456L,
            CreatedAt = new DateTime(2024, 1, 1),
            AuthorId = 789L,
            Events = new List<TicketAuditEventDto>
            {
                new() { Id = 1L, Type = "system", Body = "Event 1" },
                new() { Id = 2L, Type = "comment", Body = "Event 2" }
            }
        };

        // Assert
        Assert.Equal(123L, dto.Id);
        Assert.Equal(456L, dto.TicketId);
        Assert.Equal(new DateTime(2024, 1, 1), dto.CreatedAt);
        Assert.Equal(789L, dto.AuthorId);
        Assert.Equal(2, dto.Events.Count);
    }

    [Fact]
    public void TicketAuditDto_WithZeroId_ShouldAllowZero()
    {
        // Arrange & Act
        var dto = new TicketAuditDto { Id = 0L };

        // Assert
        Assert.Equal(0L, dto.Id);
    }

    [Fact]
    public void TicketAuditDto_WithNegativeId_ShouldAllowNegative()
    {
        // Arrange & Act
        var dto = new TicketAuditDto { Id = -1L };

        // Assert
        Assert.Equal(-1L, dto.Id);
    }

    [Fact]
    public void TicketAuditDto_WithMaxId_ShouldAllowMaxValue()
    {
        // Arrange & Act
        var dto = new TicketAuditDto { Id = long.MaxValue };

        // Assert
        Assert.Equal(long.MaxValue, dto.Id);
    }

    [Fact]
    public void TicketAuditDto_WithZeroTicketId_ShouldAllowZero()
    {
        // Arrange & Act
        var dto = new TicketAuditDto { TicketId = 0L };

        // Assert
        Assert.Equal(0L, dto.TicketId);
    }

    [Fact]
    public void TicketAuditDto_WithNegativeTicketId_ShouldAllowNegative()
    {
        // Arrange & Act
        var dto = new TicketAuditDto { TicketId = -1L };

        // Assert
        Assert.Equal(-1L, dto.TicketId);
    }

    [Fact]
    public void TicketAuditDto_WithMaxTicketId_ShouldAllowMaxValue()
    {
        // Arrange & Act
        var dto = new TicketAuditDto { TicketId = long.MaxValue };

        // Assert
        Assert.Equal(long.MaxValue, dto.TicketId);
    }

    [Fact]
    public void TicketAuditDto_WithZeroAuthorId_ShouldAllowZero()
    {
        // Arrange & Act
        var dto = new TicketAuditDto { AuthorId = 0L };

        // Assert
        Assert.Equal(0L, dto.AuthorId);
    }

    [Fact]
    public void TicketAuditDto_WithNegativeAuthorId_ShouldAllowNegative()
    {
        // Arrange & Act
        var dto = new TicketAuditDto { AuthorId = -1L };

        // Assert
        Assert.Equal(-1L, dto.AuthorId);
    }

    [Fact]
    public void TicketAuditDto_WithMaxAuthorId_ShouldAllowMaxValue()
    {
        // Arrange & Act
        var dto = new TicketAuditDto { AuthorId = long.MaxValue };

        // Assert
        Assert.Equal(long.MaxValue, dto.AuthorId);
    }

    [Fact]
    public void TicketAuditDto_WithEmptyEvents_ShouldInitializeEmptyList()
    {
        // Arrange & Act
        var dto = new TicketAuditDto { Events = new List<TicketAuditEventDto>() };

        // Assert
        Assert.Empty(dto.Events);
    }

    [Fact]
    public void TicketAuditDto_WithNullEvents_ShouldAllowNull()
    {
        // Arrange & Act
        var dto = new TicketAuditDto { Events = null };

        // Assert
        Assert.Null(dto.Events);
    }

    [Fact]
    public void TicketAuditDto_WithMinDateTime_ShouldAllowMinValue()
    {
        // Arrange & Act
        var dto = new TicketAuditDto { CreatedAt = DateTime.MinValue };

        // Assert
        Assert.Equal(DateTime.MinValue, dto.CreatedAt);
    }

    [Fact]
    public void TicketAuditDto_WithMaxDateTime_ShouldAllowMaxValue()
    {
        // Arrange & Act
        var dto = new TicketAuditDto { CreatedAt = DateTime.MaxValue };

        // Assert
        Assert.Equal(DateTime.MaxValue, dto.CreatedAt);
    }

    [Fact]
    public void TicketAuditDto_WithUtcDateTime_ShouldStoreUtcTime()
    {
        // Arrange
        var utcTime = DateTime.UtcNow;
        var dto = new TicketAuditDto { CreatedAt = utcTime };

        // Assert
        Assert.Equal(utcTime, dto.CreatedAt);
    }

    [Fact]
    public void TicketAuditDto_WithDifferentTimeZones_ShouldStoreTime()
    {
        // Arrange
        var localTime = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Local);
        var dto = new TicketAuditDto { CreatedAt = localTime };

        // Assert
        Assert.Equal(localTime, dto.CreatedAt);
        Assert.NotNull(dto.CreatedAt);
        Assert.Equal(DateTimeKind.Local, dto.CreatedAt.Kind);
    }

    [Fact]
    public void TicketAuditDto_WithMultiplePropertyAssignments_ShouldUpdateValues()
    {
        // Arrange
        var dto = new TicketAuditDto
        {
            Id = 1L,
            TicketId = 1L,
            AuthorId = 1L
        };

        // Assert initial state
        Assert.Equal(1L, dto.Id);
        Assert.Equal(1L, dto.TicketId);
        Assert.Equal(1L, dto.AuthorId);

        // Update with object initializer
        dto = new TicketAuditDto
        {
            Id = 2L,
            TicketId = 2L,
            AuthorId = 2L
        };

        // Assert updated values
        Assert.Equal(2L, dto.Id);
        Assert.Equal(2L, dto.TicketId);
        Assert.Equal(2L, dto.AuthorId);
    }

    #endregion

    #region TicketAuditEventDto Tests

    [Fact]
    public void TicketAuditEventDto_DefaultConstructor_ShouldInitializeProperties()
    {
        // Arrange & Act
        var dto = new TicketAuditEventDto();

        // Assert
        Assert.Equal(0L, dto.Id);
        Assert.Equal(string.Empty, dto.Type);
        Assert.Equal(string.Empty, dto.Body);
        Assert.Null(dto.Attachments);
    }

    [Fact]
    public void TicketAuditEventDto_WithAllProperties_ShouldSetAllProperties()
    {
        // Arrange & Act
        var dto = new TicketAuditEventDto
        {
            Id = 123L,
            Type = "system",
            Body = "Event body",
            Attachments = new List<AttachmentDto>
            {
                new() { Id = 1L, FileName = "file1.pdf" },
                new() { Id = 2L, FileName = "file2.jpg" }
            }
        };

        // Assert
        Assert.Equal(123L, dto.Id);
        Assert.Equal("system", dto.Type);
        Assert.Equal("Event body", dto.Body);
        Assert.Equal(2, dto.Attachments.Count);
    }

    [Fact]
    public void TicketAuditEventDto_WithZeroId_ShouldAllowZero()
    {
        // Arrange & Act
        var dto = new TicketAuditEventDto { Id = 0L };

        // Assert
        Assert.Equal(0L, dto.Id);
    }

    [Fact]
    public void TicketAuditEventDto_WithNegativeId_ShouldAllowNegative()
    {
        // Arrange & Act
        var dto = new TicketAuditEventDto { Id = -1L };

        // Assert
        Assert.Equal(-1L, dto.Id);
    }

    [Fact]
    public void TicketAuditEventDto_WithMaxId_ShouldAllowMaxValue()
    {
        // Arrange & Act
        var dto = new TicketAuditEventDto { Id = long.MaxValue };

        // Assert
        Assert.Equal(long.MaxValue, dto.Id);
    }

    [Fact]
    public void TicketAuditEventDto_WithEmptyType_ShouldSetEmptyString()
    {
        // Arrange & Act
        var dto = new TicketAuditEventDto { Type = string.Empty };

        // Assert
        Assert.Equal(string.Empty, dto.Type);
    }

    [Fact]
    public void TicketAuditEventDto_WithNullType_ShouldAllowNull()
    {
        // Arrange & Act
        var dto = new TicketAuditEventDto { Type = null };

        // Assert
        Assert.Null(dto.Type);
    }

    [Fact]
    public void TicketAuditEventDto_WithSpecialCharactersInType_ShouldStoreSpecialChars()
    {
        // Arrange & Act
        var dto = new TicketAuditEventDto { Type = "Type: /?\\|<>()[]{}" };

        // Assert
        Assert.Equal("Type: /?\\|<>()[]{}", dto.Type);
    }

    [Fact]
    public void TicketAuditEventDto_WithUnicodeInType_ShouldStoreUnicode()
    {
        // Arrange & Act
        var dto = new TicketAuditEventDto { Type = "类型: 系统" };

        // Assert
        Assert.Equal("类型: 系统", dto.Type);
    }

    [Fact]
    public void TicketAuditEventDto_WithWhitespaceInType_ShouldStoreWhitespace()
    {
        // Arrange & Act
        var dto = new TicketAuditEventDto { Type = "   Type with spaces   " };

        // Assert
        Assert.Equal("   Type with spaces   ", dto.Type);
    }

    [Fact]
    public void TicketAuditEventDto_WithEmptyBody_ShouldSetEmptyString()
    {
        // Arrange & Act
        var dto = new TicketAuditEventDto { Body = string.Empty };

        // Assert
        Assert.Equal(string.Empty, dto.Body);
    }

    [Fact]
    public void TicketAuditEventDto_WithNullBody_ShouldAllowNull()
    {
        // Arrange & Act
        var dto = new TicketAuditEventDto { Body = null };

        // Assert
        Assert.Null(dto.Body);
    }

    [Fact]
    public void TicketAuditEventDto_WithSpecialCharactersInBody_ShouldStoreSpecialChars()
    {
        // Arrange & Act
        var dto = new TicketAuditEventDto { Body = "Body: @#$%^&*()_+-=[]{}|;':\",./<>?" };

        // Assert
        Assert.Equal("Body: @#$%^&*()_+-=[]{}|;':\",./<>?", dto.Body);
    }

    [Fact]
    public void TicketAuditEventDto_WithUnicodeInBody_ShouldStoreUnicode()
    {
        // Arrange & Act
        var dto = new TicketAuditEventDto { Body = "Body with unicode: 你好世界 🌍" };

        // Assert
        Assert.Equal("Body with unicode: 你好世界 🌍", dto.Body);
    }

    [Fact]
    public void TicketAuditEventDto_WithWhitespaceInBody_ShouldStoreWhitespace()
    {
        // Arrange & Act
        var dto = new TicketAuditEventDto { Body = "   Body with spaces   " };

        // Assert
        Assert.Equal("   Body with spaces   ", dto.Body);
    }

    [Fact]
    public void TicketAuditEventDto_WithNullAttachments_ShouldAllowNull()
    {
        // Arrange & Act
        var dto = new TicketAuditEventDto { Attachments = null };

        // Assert
        Assert.Null(dto.Attachments);
    }

    [Fact]
    public void TicketAuditEventDto_WithEmptyAttachments_ShouldInitializeEmptyList()
    {
        // Arrange & Act
        var dto = new TicketAuditEventDto { Attachments = new List<AttachmentDto>() };

        // Assert
        Assert.Empty(dto.Attachments);
    }

    [Fact]
    public void TicketAuditEventDto_WithMinDateTime_ShouldAllowMinValue()
    {
        // Arrange & Act
        var dto = new TicketAuditEventDto { Id = long.MinValue };

        // Assert
        Assert.Equal(long.MinValue, dto.Id);
    }

    [Fact]
    public void TicketAuditEventDto_WithMaxDateTime_ShouldAllowMaxValue()
    {
        // Arrange & Act
        var dto = new TicketAuditEventDto { Id = long.MaxValue };

        // Assert
        Assert.Equal(long.MaxValue, dto.Id);
    }

    [Fact]
    public void TicketAuditEventDto_WithMultiplePropertyAssignments_ShouldUpdateValues()
    {
        // Arrange
        var dto = new TicketAuditEventDto
        {
            Id = 1L,
            Type = "First",
            Body = "Body 1"
        };

        // Assert initial state
        Assert.Equal(1L, dto.Id);
        Assert.Equal("First", dto.Type);
        Assert.Equal("Body 1", dto.Body);

        // Update with object initializer
        dto = new TicketAuditEventDto
        {
            Id = 2L,
            Type = "Second",
            Body = "Body 2"
        };

        // Assert updated values
        Assert.Equal(2L, dto.Id);
        Assert.Equal("Second", dto.Type);
        Assert.Equal("Body 2", dto.Body);
    }

    #endregion
}