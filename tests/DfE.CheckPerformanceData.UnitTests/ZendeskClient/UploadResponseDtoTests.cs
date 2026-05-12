using System.Collections.Generic;
using Xunit;
using DfE.CheckPerformanceData.Application.ZendeskClient;

namespace DfE.CheckPerformanceData.UnitTests.ZendeskClient;

/// <summary>
/// Unit tests for UploadResponseDto and UploadDto classes.
/// </summary>
public class UploadResponseDtoTests
{
    #region UploadResponseDto Tests

    [Fact]
    public void UploadResponseDto_DefaultConstructor_ShouldInitializeProperties()
    {
        // Arrange & Act
        var dto = new UploadResponseDto();

        // Assert
        Assert.Null(dto.Upload);
    }

    [Fact]
    public void UploadResponseDto_WithCustomUpload_ShouldSetUpload()
    {
        // Arrange
        var upload = new UploadDto
        {
            Token = "test-token",
            Attachments = new List<AttachmentDto>
            {
                new() { Id = 1L, FileName = "file1.pdf" },
                new() { Id = 2L, FileName = "file2.jpg" }
            }
        };

        var dto = new UploadResponseDto { Upload = upload };

        // Assert
        Assert.NotNull(dto.Upload);
        Assert.Equal("test-token", dto.Upload.Token);
        Assert.Equal(2, dto.Upload.Attachments.Count);
    }

    [Fact]
    public void UploadResponseDto_WithNullUpload_ShouldAllowNull()
    {
        // Arrange & Act
        var dto = new UploadResponseDto { Upload = null };

        // Assert
        Assert.Null(dto.Upload);
    }

    [Fact]
    public void UploadResponseDto_WithEmptyUpload_ShouldInitializeEmptyUpload()
    {
        // Arrange & Act
        var dto = new UploadResponseDto { Upload = new UploadDto() };

        // Assert
        Assert.NotNull(dto.Upload);
        Assert.Null(dto.Upload.Token);
        Assert.Empty(dto.Upload.Attachments);
    }

    [Fact]
    public void UploadResponseDto_WithMultipleAssignments_ShouldUpdateValues()
    {
        // Arrange
        var dto = new UploadResponseDto { Upload = new UploadDto { Token = "first-token" } };

        // Assert initial state
        Assert.NotNull(dto.Upload);
        Assert.Equal("first-token", dto.Upload.Token);

        // Update with new object
        dto = new UploadResponseDto { Upload = new UploadDto { Token = "second-token" } };

        // Assert updated values
        Assert.NotNull(dto.Upload);
        Assert.Equal("second-token", dto.Upload.Token);
    }

    #endregion

    #region UploadDto Tests

    [Fact]
    public void UploadDto_DefaultConstructor_ShouldInitializeProperties()
    {
        // Arrange & Act
        var dto = new UploadDto();

        // Assert
        Assert.Null(dto.Token);
        Assert.Empty(dto.Attachments);
    }

    [Fact]
    public void UploadDto_WithAllProperties_ShouldSetAllProperties()
    {
        // Arrange & Act
        var dto = new UploadDto
        {
            Token = "upload-token-123",
            Attachments = new List<AttachmentDto>
            {
                new() { Id = 1L, FileName = "document.pdf" },
                new() { Id = 2L, FileName = "image.png" },
                new() { Id = 3L, FileName = "spreadsheet.xlsx" }
            }
        };

        // Assert
        Assert.Equal("upload-token-123", dto.Token);
        Assert.Equal(3, dto.Attachments.Count);
    }

    [Fact]
    public void UploadDto_WithNullToken_ShouldAllowNull()
    {
        // Arrange & Act
        var dto = new UploadDto { Token = null };

        // Assert
        Assert.Null(dto.Token);
    }

    [Fact]
    public void UploadDto_WithEmptyToken_ShouldSetEmptyString()
    {
        // Arrange & Act
        var dto = new UploadDto { Token = string.Empty };

        // Assert
        Assert.Equal(string.Empty, dto.Token);
    }

    [Fact]
    public void UploadDto_WithSpecialCharactersInToken_ShouldStoreSpecialChars()
    {
        // Arrange & Act
        var dto = new UploadDto { Token = "Token: /?\\|<>()[]{}" };

        // Assert
        Assert.Equal("Token: /?\\|<>()[]{}", dto.Token);
    }

    [Fact]
    public void UploadDto_WithUnicodeInToken_ShouldStoreUnicode()
    {
        // Arrange & Act
        var dto = new UploadDto { Token = "令牌: 上传" };

        // Assert
        Assert.Equal("令牌: 上传", dto.Token);
    }

    [Fact]
    public void UploadDto_WithWhitespaceInToken_ShouldStoreWhitespace()
    {
        // Arrange & Act
        var dto = new UploadDto { Token = "   Token with spaces   " };

        // Assert
        Assert.Equal("   Token with spaces   ", dto.Token);
    }

    [Fact]
    public void UploadDto_WithZeroAttachments_ShouldInitializeEmptyList()
    {
        // Arrange & Act
        var dto = new UploadDto { Attachments = new List<AttachmentDto>() };

        // Assert
        Assert.Empty(dto.Attachments);
    }

    [Fact]
    public void UploadDto_WithNullAttachments_ShouldAllowNull()
    {
        // Arrange & Act
        var dto = new UploadDto { Attachments = null };

        // Assert
        Assert.Null(dto.Attachments);
    }

    [Fact]
    public void UploadDto_WithSingleAttachment_ShouldStoreOneItem()
    {
        // Arrange & Act
        var dto = new UploadDto
        {
            Attachments = new List<AttachmentDto>
            {
                new() { Id = 1L, FileName = "single.pdf" }
            }
        };

        // Assert
        Assert.Single(dto.Attachments);
        Assert.Equal(1L, dto.Attachments[0].Id);
        Assert.Equal("single.pdf", dto.Attachments[0].FileName);
    }

    [Fact]
    public void UploadDto_WithMultipleAttachments_ShouldStoreAllItems()
    {
        // Arrange & Act
        var dto = new UploadDto
        {
            Attachments = new List<AttachmentDto>
            {
                new() { Id = 1L, FileName = "file1.pdf" },
                new() { Id = 2L, FileName = "file2.jpg" },
                new() { Id = 3L, FileName = "file3.png" },
                new() { Id = 4L, FileName = "file4.docx" }
            }
        };

        // Assert
        Assert.Equal(4, dto.Attachments.Count);
        Assert.Equal(1L, dto.Attachments[0].Id);
        Assert.Equal(2L, dto.Attachments[1].Id);
        Assert.Equal(3L, dto.Attachments[2].Id);
        Assert.Equal(4L, dto.Attachments[3].Id);
    }

    [Fact]
    public void UploadDto_WithDuplicateAttachmentIds_ShouldStoreAll()
    {
        // Arrange & Act
        var dto = new UploadDto
        {
            Attachments = new List<AttachmentDto>
            {
                new() { Id = 1L, FileName = "file1.pdf" },
                new() { Id = 1L, FileName = "file2.pdf" },
                new() { Id = 1L, FileName = "file3.pdf" }
            }
        };

        // Assert
        Assert.Equal(3, dto.Attachments.Count);
        Assert.Equal(1L, dto.Attachments[0].Id);
        Assert.Equal(1L, dto.Attachments[1].Id);
        Assert.Equal(1L, dto.Attachments[2].Id);
    }

    [Fact]
    public void UploadDto_WithEmptyAttachmentList_ShouldInitializeEmptyList()
    {
        // Arrange & Act
        var dto = new UploadDto { Attachments = new List<AttachmentDto>() };

        // Assert
        Assert.Empty(dto.Attachments);
        Assert.NotNull(dto.Attachments); // Should be a new list instance
    }

    [Fact]
    public void UploadDto_WithMultiplePropertyAssignments_ShouldUpdateValues()
    {
        // Arrange
        var dto = new UploadDto
        {
            Token = "first-token",
            Attachments = new List<AttachmentDto> { new() { Id = 1L } }
        };

        // Assert initial state
        Assert.Equal("first-token", dto.Token);
        Assert.Single(dto.Attachments);

        // Update with object initializer
        dto = new UploadDto
        {
            Token = "second-token",
            Attachments = new List<AttachmentDto>
            {
                new() { Id = 2L },
                new() { Id = 3L }
            }
        };

        // Assert updated values
        Assert.Equal("second-token", dto.Token);
        Assert.Equal(2, dto.Attachments.Count);
        Assert.Equal(2L, dto.Attachments[0].Id);
        Assert.Equal(3L, dto.Attachments[1].Id);
    }

    [Fact]
    public void UploadDto_WithMaxAttachmentCount_ShouldStoreManyItems()
    {
        // Arrange & Act
        var attachments = new List<AttachmentDto>();
        for (int i = 0; i < 1000; i++)
        {
            attachments.Add(new AttachmentDto { Id = (long)i, FileName = $"file{i}.pdf" });
        }

        var dto = new UploadDto { Attachments = attachments };

        // Assert
        Assert.Equal(1000, dto.Attachments.Count);
        Assert.Equal(0L, dto.Attachments[0].Id);
        Assert.Equal(999L, dto.Attachments[999].Id);
    }

    [Fact]
    public void UploadDto_WithLongToken_ShouldStoreLongString()
    {
        // Arrange & Act
        var longToken = new string('a', 10000);
        var dto = new UploadDto { Token = longToken };

        // Assert
        Assert.Equal(10000, dto.Token.Length);
        Assert.Equal(longToken, dto.Token);
    }

    #endregion
}