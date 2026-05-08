using System;
using System.Collections.Generic;
using Xunit;
using DfE.CheckPerformanceData.Application.ZendeskClient;

namespace DfE.CheckPerformanceData.UnitTests.ZendeskClient;

/// <summary>
/// Unit tests for UpdateTicketRequestDto, UpdateTicketDto, and TicketCommentUpdateDto.
/// </summary>
public class UpdateTicketRequestDtoTests
{
    #region UpdateTicketRequestDto Tests

    [Fact]
    public void UpdateTicketRequestDto_DefaultConstructor_ShouldInitializeProperties()
    {
        // Arrange & Act
        var dto = new UpdateTicketRequestDto();

        // Assert
        Assert.Null(dto.Ticket);
    }

    [Fact]
    public void UpdateTicketRequestDto_WithNullTicket_ShouldAllowNull()
    {
        // Arrange & Act
        var dto = new UpdateTicketRequestDto { Ticket = null };

        // Assert
        Assert.Null(dto.Ticket);
    }

    [Fact]
    public void UpdateTicketRequestDto_WithEmptyTicket_ShouldInitializeNull()
    {
        // Arrange & Act
        var dto = new UpdateTicketRequestDto { Ticket = new UpdateTicketDto() };

        // Assert
        Assert.NotNull(dto.Ticket);
        Assert.Null(dto.Ticket.Comment);
    }

    [Fact]
    public void UpdateTicketRequestDto_WithAllProperties_ShouldSetAllProperties()
    {
        // Arrange & Act
        var dto = new UpdateTicketRequestDto
        {
            Ticket = new UpdateTicketDto
            {
                Comment = new TicketCommentUpdateDto
                {
                    Body = "Test comment body",
                    Uploads = new List<string> { "file1.pdf", "file2.jpg" }
                }
            }
        };

        // Assert
        Assert.NotNull(dto.Ticket);
        Assert.NotNull(dto.Ticket.Comment);
        Assert.Equal("Test comment body", dto.Ticket.Comment.Body);
        Assert.Equal(2, dto.Ticket.Comment.Uploads.Count);
        Assert.Equal("file1.pdf", dto.Ticket.Comment.Uploads[0]);
        Assert.Equal("file2.jpg", dto.Ticket.Comment.Uploads[1]);
    }

    [Fact]
    public void UpdateTicketRequestDto_WithEmptyBody_ShouldSetEmptyString()
    {
        // Arrange & Act
        var dto = new UpdateTicketRequestDto
        {
            Ticket = new UpdateTicketDto
            {
                Comment = new TicketCommentUpdateDto { Body = string.Empty }
            }
        };

        // Assert
        Assert.Equal(string.Empty, dto.Ticket.Comment.Body);
    }

    [Fact]
    public void UpdateTicketRequestDto_WithNullBody_ShouldAllowNull()
    {
        // Arrange & Act
        var dto = new UpdateTicketRequestDto
        {
            Ticket = new UpdateTicketDto
            {
                Comment = new TicketCommentUpdateDto { Body = null }
            }
        };

        // Assert
        Assert.Null(dto.Ticket.Comment.Body);
    }

    [Fact]
    public void UpdateTicketRequestDto_WithNullUploads_ShouldAllowNull()
    {
        // Arrange & Act
        var dto = new UpdateTicketRequestDto
        {
            Ticket = new UpdateTicketDto
            {
                Comment = new TicketCommentUpdateDto { Uploads = null }
            }
        };

        // Assert
        Assert.Null(dto.Ticket.Comment.Uploads);
    }

    [Fact]
    public void UpdateTicketRequestDto_WithEmptyUploads_ShouldInitializeEmptyList()
    {
        // Arrange & Act
        var dto = new UpdateTicketRequestDto
        {
            Ticket = new UpdateTicketDto
            {
                Comment = new TicketCommentUpdateDto { Uploads = new List<string>() }
            }
        };

        // Assert
        Assert.Empty(dto.Ticket.Comment.Uploads);
    }

    [Fact]
    public void UpdateTicketRequestDto_WithSingleUpload_ShouldSetSingleItem()
    {
        // Arrange & Act
        var dto = new UpdateTicketRequestDto
        {
            Ticket = new UpdateTicketDto
            {
                Comment = new TicketCommentUpdateDto
                {
                    Uploads = new List<string> { "single.pdf" }
                }
            }
        };

        // Assert
        Assert.Single(dto.Ticket.Comment.Uploads);
        Assert.Equal("single.pdf", dto.Ticket.Comment.Uploads[0]);
    }

    [Fact]
    public void UpdateTicketRequestDto_WithSpecialCharactersInBody_ShouldStoreSpecialChars()
    {
        // Arrange & Act
        var dto = new UpdateTicketRequestDto
        {
            Ticket = new UpdateTicketDto
            {
                Comment = new TicketCommentUpdateDto
                {
                    Body = "Body with special chars: @#$%^&*()_+-=[]{}|;':\",./<>?"
                }
            }
        };

        // Assert
        Assert.Equal("Body with special chars: @#$%^&*()_+-=[]{}|;':\",./<>?", dto.Ticket.Comment.Body);
    }

    [Fact]
    public void UpdateTicketRequestDto_WithUnicodeInBody_ShouldStoreUnicode()
    {
        // Arrange & Act
        var dto = new UpdateTicketRequestDto
        {
            Ticket = new UpdateTicketDto
            {
                Comment = new TicketCommentUpdateDto
                {
                    Body = "Body with unicode: 你好世界 🌍"
                }
            }
        };

        // Assert
        Assert.Equal("Body with unicode: 你好世界 🌍", dto.Ticket.Comment.Body);
    }

    [Fact]
    public void UpdateTicketRequestDto_WithWhitespaceInBody_ShouldStoreWhitespace()
    {
        // Arrange & Act
        var dto = new UpdateTicketRequestDto
        {
            Ticket = new UpdateTicketDto
            {
                Comment = new TicketCommentUpdateDto
                {
                    Body = "   Body with spaces   "
                }
            }
        };

        // Assert
        Assert.Equal("   Body with spaces   ", dto.Ticket.Comment.Body);
    }

    [Fact]
    public void UpdateTicketRequestDto_WithMultipleBodyAssignments_ShouldUpdateValue()
    {
        // Arrange & Act
        var dto = new UpdateTicketRequestDto
        {
            Ticket = new UpdateTicketDto
            {
                Comment = new TicketCommentUpdateDto { Body = "body1" }
            }
        };
        dto.Ticket.Comment.Body = "body2";
        dto.Ticket.Comment.Body = "body3";

        // Assert
        Assert.Equal("body3", dto.Ticket.Comment.Body);
    }

    [Fact]
    public void UpdateTicketRequestDto_WithMultipleUploads_ShouldSetMultipleItems()
    {
        // Arrange & Act
        var dto = new UpdateTicketRequestDto
        {
            Ticket = new UpdateTicketDto
            {
                Comment = new TicketCommentUpdateDto
                {
                    Uploads = new List<string> { "file1.pdf", "file2.jpg", "file3.png" }
                }
            }
        };

        // Assert
        Assert.Equal(3, dto.Ticket.Comment.Uploads.Count);
        Assert.Equal("file1.pdf", dto.Ticket.Comment.Uploads[0]);
        Assert.Equal("file2.jpg", dto.Ticket.Comment.Uploads[1]);
        Assert.Equal("file3.png", dto.Ticket.Comment.Uploads[2]);
    }

    [Fact]
    public void UpdateTicketRequestDto_WithNullComment_ShouldAllowNull()
    {
        // Arrange & Act
        var dto = new UpdateTicketRequestDto
        {
            Ticket = new UpdateTicketDto
            {
                Comment = null
            }
        };

        // Assert
        Assert.Null(dto.Ticket.Comment);
    }

    [Fact]
    public void UpdateTicketRequestDto_WithZeroBody_ShouldAllowZero()
    {
        // Arrange & Act
        var dto = new UpdateTicketRequestDto
        {
            Ticket = new UpdateTicketDto
            {
                Comment = new TicketCommentUpdateDto { Body = "" }
            }
        };

        // Assert
        Assert.Equal("", dto.Ticket.Comment.Body);
    }

    // [Fact]
    // public void UpdateTicketRequestDto_WithMaxBody_ShouldAllowMaxValue()
    // {
    //     // Arrange & Act
    //     var dto = new UpdateTicketRequestDto
    //     {
    //         Ticket = new UpdateTicketDto
    //         {
    //             Comment = new TicketCommentUpdateDto
    //             {
    //                 Body = new string('a', int.MaxValue)
    //             }
    //         }
    //     };

    //     // Assert
    //     Assert.Equal(int.MaxValue, dto.Ticket.Comment.Body.Length);
    // }

    [Fact]
    public void UpdateTicketRequestDto_WithMultiplePropertyAssignments_ShouldUpdateValues()
    {
        // Arrange & Act
        var dto = new UpdateTicketRequestDto
        {
            Ticket = new UpdateTicketDto
            {
                Comment = new TicketCommentUpdateDto
                {
                    Body = "Initial body",
                    Uploads = new List<string> { "file1.pdf" }
                }
            }
        };

        // Assert initial state
        Assert.NotNull(dto.Ticket);
        Assert.NotNull(dto.Ticket.Comment);
        Assert.Equal("Initial body", dto.Ticket.Comment.Body);
        Assert.Single(dto.Ticket.Comment.Uploads);

        // Update with object initializer
        dto = new UpdateTicketRequestDto
        {
            Ticket = new UpdateTicketDto
            {
                Comment = new TicketCommentUpdateDto
                {
                    Body = "Updated body",
                    Uploads = new List<string> { "file2.jpg", "file3.png" }
                }
            }
        };

        // Assert updated values
        Assert.NotNull(dto.Ticket);
        Assert.NotNull(dto.Ticket.Comment);
        Assert.Equal("Updated body", dto.Ticket.Comment.Body);
        Assert.Equal(2, dto.Ticket.Comment.Uploads.Count);
    }

    #endregion

    #region UpdateTicketDto Tests

    [Fact]
    public void UpdateTicketDto_DefaultConstructor_ShouldInitializeProperties()
    {
        // Arrange & Act
        var dto = new UpdateTicketDto();

        // Assert
        Assert.Null(dto.Comment);
    }

    [Fact]
    public void UpdateTicketDto_WithNullComment_ShouldAllowNull()
    {
        // Arrange & Act
        var dto = new UpdateTicketDto { Comment = null };

        // Assert
        Assert.Null(dto.Comment);
    }

    [Fact]
    public void UpdateTicketDto_WithEmptyComment_ShouldInitializeNull()
    {
        // Arrange & Act
        var dto = new UpdateTicketDto { Comment = new TicketCommentUpdateDto() };

        // Assert
        Assert.NotNull(dto.Comment);
        Assert.Null(dto.Comment.Body);
        Assert.Null(dto.Comment.Uploads);
    }

    [Fact]
    public void UpdateTicketDto_WithAllProperties_ShouldSetAllProperties()
    {
        // Arrange & Act
        var dto = new UpdateTicketDto
        {
            Comment = new TicketCommentUpdateDto
            {
                Body = "Test comment body",
                Uploads = new List<string> { "file1.pdf", "file2.jpg" }
            }
        };

        // Assert
        Assert.NotNull(dto.Comment);
        Assert.Equal("Test comment body", dto.Comment.Body);
        Assert.Equal(2, dto.Comment.Uploads.Count);
        Assert.Equal("file1.pdf", dto.Comment.Uploads[0]);
        Assert.Equal("file2.jpg", dto.Comment.Uploads[1]);
    }

    [Fact]
    public void UpdateTicketDto_WithNullBody_ShouldAllowNull()
    {
        // Arrange & Act
        var dto = new UpdateTicketDto
        {
            Comment = new TicketCommentUpdateDto { Body = null }
        };

        // Assert
        Assert.Null(dto.Comment.Body);
    }

    [Fact]
    public void UpdateTicketDto_WithEmptyBody_ShouldSetEmptyString()
    {
        // Arrange & Act
        var dto = new UpdateTicketDto
        {
            Comment = new TicketCommentUpdateDto { Body = string.Empty }
        };

        // Assert
        Assert.Equal(string.Empty, dto.Comment.Body);
    }

    [Fact]
    public void UpdateTicketDto_WithNullUploads_ShouldAllowNull()
    {
        // Arrange & Act
        var dto = new UpdateTicketDto
        {
            Comment = new TicketCommentUpdateDto { Uploads = null }
        };

        // Assert
        Assert.Null(dto.Comment.Uploads);
    }

    [Fact]
    public void UpdateTicketDto_WithEmptyUploads_ShouldInitializeEmptyList()
    {
        // Arrange & Act
        var dto = new UpdateTicketDto
        {
            Comment = new TicketCommentUpdateDto { Uploads = new List<string>() }
        };

        // Assert
        Assert.Empty(dto.Comment.Uploads);
    }

    [Fact]
    public void UpdateTicketDto_WithSpecialCharactersInBody_ShouldStoreSpecialChars()
    {
        // Arrange & Act
        var dto = new UpdateTicketDto
        {
            Comment = new TicketCommentUpdateDto
            {
                Body = "Body with special chars: @#$%^&*()_+-=[]{}|;':\",./<>?"
            }
        };

        // Assert
        Assert.Equal("Body with special chars: @#$%^&*()_+-=[]{}|;':\",./<>?", dto.Comment.Body);
    }

    [Fact]
    public void UpdateTicketDto_WithUnicodeInBody_ShouldStoreUnicode()
    {
        // Arrange & Act
        var dto = new UpdateTicketDto
        {
            Comment = new TicketCommentUpdateDto
            {
                Body = "Body with unicode: 你好世界 🌍"
            }
        };

        // Assert
        Assert.Equal("Body with unicode: 你好世界 🌍", dto.Comment.Body);
    }

    [Fact]
    public void UpdateTicketDto_WithWhitespaceInBody_ShouldStoreWhitespace()
    {
        // Arrange & Act
        var dto = new UpdateTicketDto
        {
            Comment = new TicketCommentUpdateDto
            {
                Body = "   Body with spaces   "
            }
        };

        // Assert
        Assert.Equal("   Body with spaces   ", dto.Comment.Body);
    }

    [Fact]
    public void UpdateTicketDto_WithMultipleBodyAssignments_ShouldUpdateValue()
    {
        // Arrange & Act
        var dto = new UpdateTicketDto
        {
            Comment = new TicketCommentUpdateDto { Body = "body1" }
        };
        dto.Comment.Body = "body2";
        dto.Comment.Body = "body3";

        // Assert
        Assert.Equal("body3", dto.Comment.Body);
    }

    [Fact]
    public void UpdateTicketDto_WithMultipleUploads_ShouldSetMultipleItems()
    {
        // Arrange & Act
        var dto = new UpdateTicketDto
        {
            Comment = new TicketCommentUpdateDto
            {
                Uploads = new List<string> { "file1.pdf", "file2.jpg", "file3.png" }
            }
        };

        // Assert
        Assert.Equal(3, dto.Comment.Uploads.Count);
        Assert.Equal("file1.pdf", dto.Comment.Uploads[0]);
        Assert.Equal("file2.jpg", dto.Comment.Uploads[1]);
        Assert.Equal("file3.png", dto.Comment.Uploads[2]);
    }

    // [Fact]
    // public void UpdateTicketDto_WithNullComment_ShouldAllowNull()
    // {
    //     // Arrange & Act
    //     var dto = new UpdateTicketDto { Comment = null };

    //     // Assert
    //     Assert.Null(dto.Comment);
    // }

    [Fact]
    public void UpdateTicketDto_WithMultiplePropertyAssignments_ShouldUpdateValues()
    {
        // Arrange & Act
        var dto = new UpdateTicketDto
        {
            Comment = new TicketCommentUpdateDto
            {
                Body = "Initial body",
                Uploads = new List<string> { "file1.pdf" }
            }
        };

        // Assert initial state
        Assert.NotNull(dto.Comment);
        Assert.Equal("Initial body", dto.Comment.Body);
        Assert.Single(dto.Comment.Uploads);

        // Update with object initializer
        dto = new UpdateTicketDto
        {
            Comment = new TicketCommentUpdateDto
            {
                Body = "Updated body",
                Uploads = new List<string> { "file2.jpg", "file3.png" }
            }
        };

        // Assert updated values
        Assert.NotNull(dto.Comment);
        Assert.Equal("Updated body", dto.Comment.Body);
        Assert.Equal(2, dto.Comment.Uploads.Count);
    }

    #endregion

    #region TicketCommentUpdateDto Tests

    [Fact]
    public void TicketCommentUpdateDto_DefaultConstructor_ShouldInitializeProperties()
    {
        // Arrange & Act
        var dto = new TicketCommentUpdateDto();

        // Assert
        Assert.Null(dto.Body);
        Assert.Null(dto.Uploads);
    }

    [Fact]
    public void TicketCommentUpdateDto_WithNullBody_ShouldAllowNull()
    {
        // Arrange & Act
        var dto = new TicketCommentUpdateDto { Body = null };

        // Assert
        Assert.Null(dto.Body);
    }

    [Fact]
    public void TicketCommentUpdateDto_WithEmptyBody_ShouldSetEmptyString()
    {
        // Arrange & Act
        var dto = new TicketCommentUpdateDto { Body = string.Empty };

        // Assert
        Assert.Equal(string.Empty, dto.Body);
    }

    [Fact]
    public void TicketCommentUpdateDto_WithNullUploads_ShouldAllowNull()
    {
        // Arrange & Act
        var dto = new TicketCommentUpdateDto { Uploads = null };

        // Assert
        Assert.Null(dto.Uploads);
    }

    [Fact]
    public void TicketCommentUpdateDto_WithEmptyUploads_ShouldInitializeEmptyList()
    {
        // Arrange & Act
        var dto = new TicketCommentUpdateDto { Uploads = new List<string>() };

        // Assert
        Assert.Empty(dto.Uploads);
    }

    [Fact]
    public void TicketCommentUpdateDto_WithSingleUpload_ShouldSetSingleItem()
    {
        // Arrange & Act
        var dto = new TicketCommentUpdateDto
        {
            Uploads = new List<string> { "single.pdf" }
        };

        // Assert
        Assert.Single(dto.Uploads);
        Assert.Equal("single.pdf", dto.Uploads[0]);
    }

    [Fact]
    public void TicketCommentUpdateDto_WithSpecialCharactersInBody_ShouldStoreSpecialChars()
    {
        // Arrange & Act
        var dto = new TicketCommentUpdateDto
        {
            Body = "Body with special chars: @#$%^&*()_+-=[]{}|;':\",./<>?"
        };

        // Assert
        Assert.Equal("Body with special chars: @#$%^&*()_+-=[]{}|;':\",./<>?", dto.Body);
    }

    [Fact]
    public void TicketCommentUpdateDto_WithUnicodeInBody_ShouldStoreUnicode()
    {
        // Arrange & Act
        var dto = new TicketCommentUpdateDto
        {
            Body = "Body with unicode: 你好世界 🌍"
        };

        // Assert
        Assert.Equal("Body with unicode: 你好世界 🌍", dto.Body);
    }

    [Fact]
    public void TicketCommentUpdateDto_WithWhitespaceInBody_ShouldStoreWhitespace()
    {
        // Arrange & Act
        var dto = new TicketCommentUpdateDto
        {
            Body = "   Body with spaces   "
        };

        // Assert
        Assert.Equal("   Body with spaces   ", dto.Body);
    }

    [Fact]
    public void TicketCommentUpdateDto_WithMultipleBodyAssignments_ShouldUpdateValue()
    {
        // Arrange & Act
        var dto = new TicketCommentUpdateDto { Body = "body1" };
        dto.Body = "body2";
        dto.Body = "body3";

        // Assert
        Assert.Equal("body3", dto.Body);
    }

    [Fact]
    public void TicketCommentUpdateDto_WithMultipleUploads_ShouldSetMultipleItems()
    {
        // Arrange & Act
        var dto = new TicketCommentUpdateDto
        {
            Uploads = new List<string> { "file1.pdf", "file2.jpg", "file3.png" }
        };

        // Assert
        Assert.Equal(3, dto.Uploads.Count);
        Assert.Equal("file1.pdf", dto.Uploads[0]);
        Assert.Equal("file2.jpg", dto.Uploads[1]);
        Assert.Equal("file3.png", dto.Uploads[2]);
    }

    [Fact]
    public void TicketCommentUpdateDto_WithZeroBody_ShouldAllowZero()
    {
        // Arrange & Act
        var dto = new TicketCommentUpdateDto { Body = "" };

        // Assert
        Assert.Equal("", dto.Body);
    }

    // [Fact]
    // public void TicketCommentUpdateDto_WithMaxBody_ShouldAllowMaxValue()
    // {
    //     // Arrange & Act
    //     var dto = new TicketCommentUpdateDto
    //     {
    //         Body = new string('a', int.MaxValue)
    //     };

    //     // Assert
    //     Assert.Equal(int.MaxValue, dto.Body.Length);
    // }

    [Fact]
    public void TicketCommentUpdateDto_WithMultiplePropertyAssignments_ShouldUpdateValues()
    {
        // Arrange & Act
        var dto = new TicketCommentUpdateDto
        {
            Body = "Initial body",
            Uploads = new List<string> { "file1.pdf" }
        };

        // Assert initial state
        Assert.Equal("Initial body", dto.Body);
        Assert.Single(dto.Uploads);

        // Update with object initializer
        dto = new TicketCommentUpdateDto
        {
            Body = "Updated body",
            Uploads = new List<string> { "file2.jpg", "file3.png" }
        };

        // Assert updated values
        Assert.Equal("Updated body", dto.Body);
        Assert.Equal(2, dto.Uploads.Count);
    }


    #endregion
}