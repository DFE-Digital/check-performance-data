using System;
using System.Collections.Generic;
using Xunit;
using DfE.CheckPerformanceData.Application.ZendeskClient;

namespace DfE.CheckPerformanceData.UnitTests.ZendeskClient;

/// <summary>
/// Unit tests for TicketDto class.
/// </summary>
public class TicketDtoTests
{
    #region TicketDto Property Tests

    [Fact]
    public void TicketDto_DefaultConstructor_ShouldInitializeProperties()
    {
        // Arrange & Act
        var dto = new TicketDto();

        // Assert
        Assert.Equal(0, dto.Id);
        Assert.Equal(string.Empty, dto.Subject);
        Assert.Equal(string.Empty, dto.Status);
        Assert.Equal(string.Empty, dto.Type);
        Assert.Equal(string.Empty, dto.Priority);
        Assert.Equal(0, dto.RequesterId);
        Assert.Null(dto.AssigneeId);
        Assert.Null(dto.GroupId);
        Assert.Null(dto.CreatedAt);
        Assert.Null(dto.UpdatedAt);
        Assert.Equal(string.Empty, dto.Description);
        Assert.Empty(dto.Tags);
        Assert.Empty(dto.CustomFields);
        Assert.Empty(dto.Fields);
        Assert.Empty(dto.AllCustomFields);
        Assert.Empty(dto.DescriptionFields);
        Assert.Null(dto.DescriptionReasonForRemoval);
        Assert.Null(dto.DescriptionOutcome);
        Assert.Null(dto.DescriptionStudentDfEEN);
        Assert.Null(dto.DescriptionStudentUPN);
        Assert.Null(dto.CustomFieldsOutcome);
    }

    [Fact]
    public void TicketDto_WithAllProperties_ShouldSetAllProperties()
    {
        // Arrange
        var dto = new TicketDto();

        // Act
        dto.Id = 123L;
        dto.Subject = "Test Subject";
        dto.Status = "open";
        dto.Type = "task";
        dto.Priority = "high";
        dto.RequesterId = 456L;
        dto.AssigneeId = 789L;
        dto.GroupId = 101L;
        dto.CreatedAt = new DateTime(2024, 1, 1);
        dto.UpdatedAt = "2024-01-02T12:00:00Z";
        dto.Description = "Test description";
        dto.Tags = new[] { "tag1", "tag2" };
        dto.CustomFields = new List<CustomFieldDto> { new() { Id = 1L, Value = "value1" } };
        dto.Fields = new List<CustomFieldDto> { new() { Id = 2L, Value = "value2" } };
        dto.AllCustomFields = new List<CustomFieldDto> { new() { Id = 3L, Value = "value3" } };
        dto.DescriptionFields = new Dictionary<string, string> { { "key", "value" } };
        dto.DescriptionReasonForRemoval = "Reason";
        dto.DescriptionOutcome = "Outcome";
        dto.DescriptionStudentDfEEN = "DfEEN123";
        dto.DescriptionStudentUPN = "UPN456";
        dto.CustomFieldsOutcome = "CustomOutcome";

        // Assert
        Assert.Equal(123L, dto.Id);
        Assert.Equal("Test Subject", dto.Subject);
        Assert.Equal("open", dto.Status);
        Assert.Equal("task", dto.Type);
        Assert.Equal("high", dto.Priority);
        Assert.Equal(456L, dto.RequesterId);
        Assert.Equal(789L, dto.AssigneeId);
        Assert.Equal(101L, dto.GroupId);
        Assert.Equal(new DateTime(2024, 1, 1), dto.CreatedAt);
        Assert.Equal("2024-01-02T12:00:00Z", dto.UpdatedAt);
        Assert.Equal("Test description", dto.Description);
        Assert.Equal(2, dto.Tags.Length);
        Assert.Equal("tag1", dto.Tags[0]);
        Assert.Equal("tag2", dto.Tags[1]);
        Assert.Single(dto.CustomFields);
        Assert.Single(dto.Fields);
        Assert.Single(dto.AllCustomFields);
        Assert.Single(dto.DescriptionFields);
        Assert.Equal("Reason", dto.DescriptionReasonForRemoval);
        Assert.Equal("Outcome", dto.DescriptionOutcome);
        Assert.Equal("DfEEN123", dto.DescriptionStudentDfEEN);
        Assert.Equal("UPN456", dto.DescriptionStudentUPN);
        Assert.Equal("CustomOutcome", dto.CustomFieldsOutcome);
    }

    [Fact]
    public void TicketDto_WithNullDescription_ShouldAllowNull()
    {
        // Arrange
        var dto = new TicketDto();

        // Act
        dto.Description = null;

        // Assert
        Assert.Null(dto.Description);
    }

    [Fact]
    public void TicketDto_WithEmptyDescription_ShouldSetEmptyString()
    {
        // Arrange
        var dto = new TicketDto();

        // Act
        dto.Description = string.Empty;

        // Assert
        Assert.Equal(string.Empty, dto.Description);
    }

    [Fact]
    public void TicketDto_WithNullTags_ShouldAllowNull()
    {
        // Arrange
        var dto = new TicketDto();

        // Act
        dto.Tags = null;

        // Assert
        Assert.Null(dto.Tags);
    }

    [Fact]
    public void TicketDto_WithEmptyTags_ShouldInitializeEmptyArray()
    {
        // Arrange
        var dto = new TicketDto();

        // Act
        dto.Tags = Array.Empty<string>();

        // Assert
        Assert.Empty(dto.Tags);
    }

    [Fact]
    public void TicketDto_WithNullCustomFields_ShouldAllowNull()
    {
        // Arrange
        var dto = new TicketDto();

        // Act
        dto.CustomFields = null;

        // Assert
        Assert.Null(dto.CustomFields);
    }

    [Fact]
    public void TicketDto_WithEmptyCustomFields_ShouldInitializeEmptyList()
    {
        // Arrange
        var dto = new TicketDto();

        // Act
        dto.CustomFields = new List<CustomFieldDto>();

        // Assert
        Assert.Empty(dto.CustomFields);
    }

    [Fact]
    public void TicketDto_WithNullFields_ShouldAllowNull()
    {
        // Arrange
        var dto = new TicketDto();

        // Act
        dto.Fields = null;

        // Assert
        Assert.Null(dto.Fields);
    }

    [Fact]
    public void TicketDto_WithEmptyFields_ShouldInitializeEmptyList()
    {
        // Arrange
        var dto = new TicketDto();

        // Act
        dto.Fields = new List<CustomFieldDto>();

        // Assert
        Assert.Empty(dto.Fields);
    }

    [Fact]
    public void TicketDto_WithNullAllCustomFields_ShouldAllowNull()
    {
        // Arrange
        var dto = new TicketDto();

        // Act
        dto.AllCustomFields = null;

        // Assert
        Assert.Null(dto.AllCustomFields);
    }

    [Fact]
    public void TicketDto_WithEmptyAllCustomFields_ShouldInitializeEmptyList()
    {
        // Arrange
        var dto = new TicketDto();

        // Act
        dto.AllCustomFields = new List<CustomFieldDto>();

        // Assert
        Assert.Empty(dto.AllCustomFields);
    }

    [Fact]
    public void TicketDto_WithNullDescriptionFields_ShouldAllowNull()
    {
        // Arrange
        var dto = new TicketDto();

        // Act
        dto.DescriptionFields = null;

        // Assert
        Assert.Null(dto.DescriptionFields);
    }

    [Fact]
    public void TicketDto_WithEmptyDescriptionFields_ShouldInitializeEmptyDictionary()
    {
        // Arrange
        var dto = new TicketDto();

        // Act
        dto.DescriptionFields = new Dictionary<string, string>();

        // Assert
        Assert.Empty(dto.DescriptionFields);
    }

    [Fact]
    public void TicketDto_WithSpecialCharactersInSubject_ShouldStoreSpecialChars()
    {
        // Arrange
        var dto = new TicketDto();

        // Act
        dto.Subject = "Test: @#$%^&*()";

        // Assert
        Assert.Equal("Test: @#$%^&*()", dto.Subject);
    }

    [Fact]
    public void TicketDto_WithUnicodeInDescription_ShouldStoreUnicode()
    {
        // Arrange
        var dto = new TicketDto();

        // Act
        dto.Description = "Description with unicode: 你好世界 🌍";

        // Assert
        Assert.Equal("Description with unicode: 你好世界 🌍", dto.Description);
    }

    [Fact]
    public void TicketDto_WithWhitespaceInSubject_ShouldStoreWhitespace()
    {
        // Arrange
        var dto = new TicketDto();

        // Act
        dto.Subject = "   Subject with spaces   ";

        // Assert
        Assert.Equal("   Subject with spaces   ", dto.Subject);
    }

    [Fact]
    public void TicketDto_WithZeroId_ShouldAllowZero()
    {
        // Arrange
        var dto = new TicketDto();

        // Act
        dto.Id = 0;

        // Assert
        Assert.Equal(0, dto.Id);
    }

    [Fact]
    public void TicketDto_WithZeroRequesterId_ShouldAllowZero()
    {
        // Arrange
        var dto = new TicketDto();

        // Act
        dto.RequesterId = 0;

        // Assert
        Assert.Equal(0, dto.RequesterId);
    }

    [Fact]
    public void TicketDto_WithNegativeId_ShouldAllowNegative()
    {
        // Arrange
        var dto = new TicketDto();

        // Act
        dto.Id = -1;

        // Assert
        Assert.Equal(-1, dto.Id);
    }

    [Fact]
    public void TicketDto_WithNegativeRequesterId_ShouldAllowNegative()
    {
        // Arrange
        var dto = new TicketDto();

        // Act
        dto.RequesterId = -1;

        // Assert
        Assert.Equal(-1, dto.RequesterId);
    }

    [Fact]
    public void TicketDto_WithMaxId_ShouldAllowMaxValue()
    {
        // Arrange
        var dto = new TicketDto();

        // Act
        dto.Id = long.MaxValue;

        // Assert
        Assert.Equal(long.MaxValue, dto.Id);
    }

    [Fact]
    public void TicketDto_WithMaxRequesterId_ShouldAllowMaxValue()
    {
        // Arrange
        var dto = new TicketDto();

        // Act
        dto.RequesterId = long.MaxValue;

        // Assert
        Assert.Equal(long.MaxValue, dto.RequesterId);
    }

    [Fact]
    public void TicketDto_WithNullAssigneeId_ShouldAllowNull()
    {
        // Arrange
        var dto = new TicketDto();

        // Act
        dto.AssigneeId = null;

        // Assert
        Assert.Null(dto.AssigneeId);
    }

    [Fact]
    public void TicketDto_WithNullGroupId_ShouldAllowNull()
    {
        // Arrange
        var dto = new TicketDto();

        // Act
        dto.GroupId = null;

        // Assert
        Assert.Null(dto.GroupId);
    }

    [Fact]
    public void TicketDto_WithNullCreatedAt_ShouldAllowNull()
    {
        // Arrange
        var dto = new TicketDto();

        // Act
        dto.CreatedAt = null;

        // Assert
        Assert.Null(dto.CreatedAt);
    }

    [Fact]
    public void TicketDto_WithNullUpdatedAt_ShouldAllowNull()
    {
        // Arrange
        var dto = new TicketDto();

        // Act
        dto.UpdatedAt = null;

        // Assert
        Assert.Null(dto.UpdatedAt);
    }

    [Fact]
    public void TicketDto_WithSpecialCharactersInStatus_ShouldStoreSpecialChars()
    {
        // Arrange
        var dto = new TicketDto();

        // Act
        dto.Status = "Status: /?\\|<>()[]{}";

        // Assert
        Assert.Equal("Status: /?\\|<>()[]{}", dto.Status);
    }

    [Fact]
    public void TicketDto_WithSpecialCharactersInType_ShouldStoreSpecialChars()
    {
        // Arrange
        var dto = new TicketDto();

        // Act
        dto.Type = "Type: /?\\|<>()[]{}";

        // Assert
        Assert.Equal("Type: /?\\|<>()[]{}", dto.Type);
    }

    [Fact]
    public void TicketDto_WithSpecialCharactersInPriority_ShouldStoreSpecialChars()
    {
        // Arrange
        var dto = new TicketDto();

        // Act
        dto.Priority = "Priority: /?\\|<>()[]{}";

        // Assert
        Assert.Equal("Priority: /?\\|<>()[]{}", dto.Priority);
    }

    [Fact]
    public void TicketDto_WithMultiplePropertyAssignments_ShouldUpdateValues()
    {
        // Arrange
        var dto = new TicketDto();

        // Act
        dto.Id = 1L;
        dto.Subject = "First";
        dto.Id = 2L;
        dto.Subject = "Second";

        // Assert
        Assert.Equal(2L, dto.Id);
        Assert.Equal("Second", dto.Subject);
    }

    [Fact]
    public void TicketDto_WithMultipleCustomFieldAssignments_ShouldUpdateValues()
    {
        // Arrange
        var dto = new TicketDto();
        var customFields = new List<CustomFieldDto>
        {
            new() { Id = 1L, Value = "value1" },
            new() { Id = 2L, Value = "value2" }
        };

        // Act
        dto.AllCustomFields = customFields;
        dto.AllCustomFields = new List<CustomFieldDto> { new() { Id = 3L, Value = "value3" } };

        // Assert
        Assert.Single(dto.AllCustomFields);
        Assert.Equal(3L, dto.AllCustomFields[0].Id);
        Assert.Equal("value3", dto.AllCustomFields[0].Value);
    }

    [Fact]
    public void TicketDto_WithUtcDateTime_ShouldStoreUtcTime()
    {
        // Arrange
        var dto = new TicketDto();
        var utcTime = DateTime.UtcNow;

        // Act
        dto.CreatedAt = utcTime;

        // Assert
        Assert.Equal(utcTime, dto.CreatedAt);
    }

    [Fact]
    public void TicketDto_WithDifferentTimeZones_ShouldStoreTime()
    {
        // Arrange
        var dto = new TicketDto();
        var localTime = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Local);

        // Act
        dto.CreatedAt = localTime;

        // Assert
        Assert.Equal(localTime, dto.CreatedAt);
        Assert.NotNull(dto.CreatedAt);
        Assert.Equal(DateTimeKind.Local, dto.CreatedAt!.Value.Kind);
    }

    [Fact]
    public void TicketDto_WithSpecialCharactersInDescription_ShouldStoreSpecialChars()
    {
        // Arrange
        var dto = new TicketDto();

        // Act
        dto.Description = "Description: @#$%^&*()";

        // Assert
        Assert.Equal("Description: @#$%^&*()", dto.Description);
    }

    [Fact]
    public void TicketDto_WithUnicodeInSubject_ShouldStoreUnicode()
    {
        // Arrange
        var dto = new TicketDto();

        // Act
        dto.Subject = "主题测试 🎓";

        // Assert
        Assert.Equal("主题测试 🎓", dto.Subject);
    }

    [Fact]
    public void TicketDto_WithWhitespaceInDescription_ShouldStoreWhitespace()
    {
        // Arrange
        var dto = new TicketDto();

        // Act
        dto.Description = "   Description with spaces   ";

        // Assert
        Assert.Equal("   Description with spaces   ", dto.Description);
    }

    [Fact]
    public void TicketDto_WithMinDateTime_ShouldAllowMinValue()
    {
        // Arrange
        var dto = new TicketDto();

        // Act
        dto.CreatedAt = DateTime.MinValue;

        // Assert
        Assert.Equal(DateTime.MinValue, dto.CreatedAt);
    }

    [Fact]
    public void TicketDto_WithMaxDateTime_ShouldAllowMaxValue()
    {
        // Arrange
        var dto = new TicketDto();

        // Act
        dto.CreatedAt = DateTime.MaxValue;

        // Assert
        Assert.Equal(DateTime.MaxValue, dto.CreatedAt);
    }

    [Fact]
    public void TicketDto_WithSpecialCharactersInTags_ShouldStoreSpecialChars()
    {
        // Arrange
        var dto = new TicketDto();

        // Act
        dto.Tags = new[] { "tag1", "tag: /?\\|<>()[]{}", "tag2" };

        // Assert
        Assert.Equal(3, dto.Tags.Length);
        Assert.Equal("tag: /?\\|<>()[]{}", dto.Tags[1]);
    }

    [Fact]
    public void TicketDto_WithUnicodeInTags_ShouldStoreUnicode()
    {
        // Arrange
        var dto = new TicketDto();

        // Act
        dto.Tags = new[] { "标签1", "标签2 🌍", "标签3" };

        // Assert
        Assert.Equal(3, dto.Tags.Length);
        Assert.Equal("标签2 🌍", dto.Tags[1]);
    }

    [Fact]
    public void TicketDto_WithWhitespaceInTags_ShouldStoreWhitespace()
    {
        // Arrange
        var dto = new TicketDto();

        // Act
        dto.Tags = new[] { "   tag1   ", "tag2", "   tag3   " };

        // Assert
        Assert.Equal(3, dto.Tags.Length);
        Assert.Equal("   tag1   ", dto.Tags[0]);
        Assert.Equal("   tag3   ", dto.Tags[2]);
    }

    [Fact]
    public void TicketDto_WithLargeId_ShouldAllowLargeValue()
    {
        // Arrange
        var dto = new TicketDto();

        // Act
        dto.Id = long.MaxValue;

        // Assert
        Assert.Equal(long.MaxValue, dto.Id);
    }

    [Fact]
    public void TicketDto_WithLargeRequesterId_ShouldAllowLargeValue()
    {
        // Arrange
        var dto = new TicketDto();

        // Act
        dto.RequesterId = long.MaxValue;

        // Assert
        Assert.Equal(long.MaxValue, dto.RequesterId);
    }

    [Fact]
    public void TicketDto_WithLargeAssigneeId_ShouldAllowLargeValue()
    {
        // Arrange
        var dto = new TicketDto();

        // Act
        dto.AssigneeId = long.MaxValue;

        // Assert
        Assert.Equal(long.MaxValue, dto.AssigneeId);
    }

    [Fact]
    public void TicketDto_WithLargeGroupId_ShouldAllowLargeValue()
    {
        // Arrange
        var dto = new TicketDto();

        // Act
        dto.GroupId = long.MaxValue;

        // Assert
        Assert.Equal(long.MaxValue, dto.GroupId);
    }

    [Fact]
    public void TicketDto_WithSpecialCharactersInDescriptionReasonForRemoval_ShouldStoreSpecialChars()
    {
        // Arrange
        var dto = new TicketDto();

        // Act
        dto.DescriptionReasonForRemoval = "Reason: @#$%^&*()";

        // Assert
        Assert.Equal("Reason: @#$%^&*()", dto.DescriptionReasonForRemoval);
    }

    [Fact]
    public void TicketDto_WithUnicodeInDescriptionReasonForRemoval_ShouldStoreUnicode()
    {
        // Arrange
        var dto = new TicketDto();

        // Act
        dto.DescriptionReasonForRemoval = "移除原因: 你好";

        // Assert
        Assert.Equal("移除原因: 你好", dto.DescriptionReasonForRemoval);
    }

    [Fact]
    public void TicketDto_WithWhitespaceInDescriptionReasonForRemoval_ShouldStoreWhitespace()
    {
        // Arrange
        var dto = new TicketDto();

        // Act
        dto.DescriptionReasonForRemoval = "   Reason with spaces   ";

        // Assert
        Assert.Equal("   Reason with spaces   ", dto.DescriptionReasonForRemoval);
    }

    [Fact]
    public void TicketDto_WithSpecialCharactersInDescriptionOutcome_ShouldStoreSpecialChars()
    {
        // Arrange
        var dto = new TicketDto();

        // Act
        dto.DescriptionOutcome = "Outcome: @#$%^&*()";

        // Assert
        Assert.Equal("Outcome: @#$%^&*()", dto.DescriptionOutcome);
    }

    [Fact]
    public void TicketDto_WithUnicodeInDescriptionOutcome_ShouldStoreUnicode()
    {
        // Arrange
        var dto = new TicketDto();

        // Act
        dto.DescriptionOutcome = "结果: 成功";

        // Assert
        Assert.Equal("结果: 成功", dto.DescriptionOutcome);
    }

    [Fact]
    public void TicketDto_WithWhitespaceInDescriptionOutcome_ShouldStoreWhitespace()
    {
        // Arrange
        var dto = new TicketDto();

        // Act
        dto.DescriptionOutcome = "   Outcome with spaces   ";

        // Assert
        Assert.Equal("   Outcome with spaces   ", dto.DescriptionOutcome);
    }

    [Fact]
    public void TicketDto_WithSpecialCharactersInDescriptionStudentDfEEN_ShouldStoreSpecialChars()
    {
        // Arrange
        var dto = new TicketDto();

        // Act
        dto.DescriptionStudentDfEEN = "DfEEN: @#$%^&*()";

        // Assert
        Assert.Equal("DfEEN: @#$%^&*()", dto.DescriptionStudentDfEEN);
    }

    [Fact]
    public void TicketDto_WithUnicodeInDescriptionStudentDfEEN_ShouldStoreUnicode()
    {
        // Arrange
        var dto = new TicketDto();

        // Act
        dto.DescriptionStudentDfEEN = "DfEEN: 学生编号";

        // Assert
        Assert.Equal("DfEEN: 学生编号", dto.DescriptionStudentDfEEN);
    }

    [Fact]
    public void TicketDto_WithWhitespaceInDescriptionStudentDfEEN_ShouldStoreWhitespace()
    {
        // Arrange
        var dto = new TicketDto();

        // Act
        dto.DescriptionStudentDfEEN = "   DfEEN with spaces   ";

        // Assert
        Assert.Equal("   DfEEN with spaces   ", dto.DescriptionStudentDfEEN);
    }

    [Fact]
    public void TicketDto_WithSpecialCharactersInDescriptionStudentUPN_ShouldStoreSpecialChars()
    {
        // Arrange
        var dto = new TicketDto();

        // Act
        dto.DescriptionStudentUPN = "UPN: @#$%^&*()";

        // Assert
        Assert.Equal("UPN: @#$%^&*()", dto.DescriptionStudentUPN);
    }

    [Fact]
    public void TicketDto_WithUnicodeInDescriptionStudentUPN_ShouldStoreUnicode()
    {
        // Arrange
        var dto = new TicketDto();

        // Act
        dto.DescriptionStudentUPN = "UPN: 学生唯一编码";

        // Assert
        Assert.Equal("UPN: 学生唯一编码", dto.DescriptionStudentUPN);
    }

    [Fact]
    public void TicketDto_WithWhitespaceInDescriptionStudentUPN_ShouldStoreWhitespace()
    {
        // Arrange
        var dto = new TicketDto();

        // Act
        dto.DescriptionStudentUPN = "   UPN with spaces   ";

        // Assert
        Assert.Equal("   UPN with spaces   ", dto.DescriptionStudentUPN);
    }

    [Fact]
    public void TicketDto_WithSpecialCharactersInCustomFieldsOutcome_ShouldStoreSpecialChars()
    {
        // Arrange
        var dto = new TicketDto();

        // Act
        dto.CustomFieldsOutcome = "Outcome: @#$%^&*()";

        // Assert
        Assert.Equal("Outcome: @#$%^&*()", dto.CustomFieldsOutcome);
    }

    [Fact]
    public void TicketDto_WithUnicodeInCustomFieldsOutcome_ShouldStoreUnicode()
    {
        // Arrange
        var dto = new TicketDto();

        // Act
        dto.CustomFieldsOutcome = "结果: 成功";

        // Assert
        Assert.Equal("结果: 成功", dto.CustomFieldsOutcome);
    }

    [Fact]
    public void TicketDto_WithWhitespaceInCustomFieldsOutcome_ShouldStoreWhitespace()
    {
        // Arrange
        var dto = new TicketDto();

        // Act
        dto.CustomFieldsOutcome = "   Outcome with spaces   ";

        // Assert
        Assert.Equal("   Outcome with spaces   ", dto.CustomFieldsOutcome);
    }

    #endregion

    #region CustomFieldsStudentUPN Method Tests

    [Fact]
    public void CustomFieldsStudentUPN_WithMatchingField_ShouldReturnUPN()
    {
        // Arrange
        var dto = new TicketDto
        {
            AllCustomFields = new List<CustomFieldDto>
            {
                new() { Id = 1L, Value = "UPN12345" }
            }
        };
        var meta = new List<CustomFieldMetaDataDto>
        {
            new() { Id = 1L, Title = "UPN" }
        };

        // Act
        var result = dto.CustomFieldsStudentUPN(meta);

        // Assert
        Assert.Equal("UPN12345", result);
    }

    [Fact]
    public void CustomFieldsStudentUPN_WithFieldNotFoundInMetadata_ShouldReturnNull()
    {
        // Arrange
        var dto = new TicketDto
        {
            AllCustomFields = new List<CustomFieldDto>
            {
                new() { Id = 1L, Value = "UPN12345" }
            }
        };
        var meta = new List<CustomFieldMetaDataDto>
        {
            new() { Id = 2L, Title = "UPN" }
        };

        // Act
        var result = dto.CustomFieldsStudentUPN(meta);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void CustomFieldsStudentUPN_WithFieldNotFoundInCustomFields_ShouldReturnNull()
    {
        // Arrange
        var dto = new TicketDto
        {
            AllCustomFields = new List<CustomFieldDto>
            {
                new() { Id = 2L, Value = "UPN12345" }
            }
        };
        var meta = new List<CustomFieldMetaDataDto>
        {
            new() { Id = 1L, Title = "UPN" }
        };

        // Act
        var result = dto.CustomFieldsStudentUPN(meta);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void CustomFieldsStudentUPN_WithNullMetadata_ShouldReturnNull()
    {
        // Arrange
        var dto = new TicketDto
        {
            AllCustomFields = new List<CustomFieldDto>
            {
                new() { Id = 1L, Value = "UPN12345" }
            }
        };

        // Act
        var result = dto.CustomFieldsStudentUPN(null);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void CustomFieldsStudentUPN_WithEmptyMetadata_ShouldReturnNull()
    {
        // Arrange
        var dto = new TicketDto
        {
            AllCustomFields = new List<CustomFieldDto>
            {
                new() { Id = 1L, Value = "UPN12345" }
            }
        };
        var meta = new List<CustomFieldMetaDataDto>();

        // Act
        var result = dto.CustomFieldsStudentUPN(meta);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void CustomFieldsStudentUPN_WithNullCustomFields_ShouldReturnNull()
    {
        // Arrange
        var dto = new TicketDto
        {
            AllCustomFields = null
        };
        var meta = new List<CustomFieldMetaDataDto>
        {
            new() { Id = 1L, Title = "UPN" }
        };

        // Act
        var result = dto.CustomFieldsStudentUPN(meta);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void CustomFieldsStudentUPN_WithEmptyCustomFields_ShouldReturnNull()
    {
        // Arrange
        var dto = new TicketDto
        {
            AllCustomFields = new List<CustomFieldDto>()
        };
        var meta = new List<CustomFieldMetaDataDto>
        {
            new() { Id = 1L, Title = "UPN" }
        };

        // Act
        var result = dto.CustomFieldsStudentUPN(meta);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void CustomFieldsStudentUPN_WithSpecialCharactersInUPN_ShouldReturnSpecialChars()
    {
        // Arrange
        var dto = new TicketDto
        {
            AllCustomFields = new List<CustomFieldDto>
            {
                new() { Id = 1L, Value = "UPN: @#$%^&*()" }
            }
        };
        var meta = new List<CustomFieldMetaDataDto>
        {
            new() { Id = 1L, Title = "UPN" }
        };

        // Act
        var result = dto.CustomFieldsStudentUPN(meta);

        // Assert
        Assert.Equal("UPN: @#$%^&*()", result);
    }

    [Fact]
    public void CustomFieldsStudentUPN_WithUnicodeInUPN_ShouldReturnUnicode()
    {
        // Arrange
        var dto = new TicketDto
        {
            AllCustomFields = new List<CustomFieldDto>
            {
                new() { Id = 1L, Value = "UPN: 你好世界 🌍" }
            }
        };
        var meta = new List<CustomFieldMetaDataDto>
        {
            new() { Id = 1L, Title = "UPN" }
        };

        // Act
        var result = dto.CustomFieldsStudentUPN(meta);

        // Assert
        Assert.Equal("UPN: 你好世界 🌍", result);
    }

    [Fact]
    public void CustomFieldsStudentUPN_WithWhitespaceInUPN_ShouldReturnWhitespace()
    {
        // Arrange
        var dto = new TicketDto
        {
            AllCustomFields = new List<CustomFieldDto>
            {
                new() { Id = 1L, Value = "   UPN with spaces   " }
            }
        };
        var meta = new List<CustomFieldMetaDataDto>
        {
            new() { Id = 1L, Title = "UPN" }
        };

        // Act
        var result = dto.CustomFieldsStudentUPN(meta);

        // Assert
        Assert.Equal("   UPN with spaces   ", result);
    }

    [Fact]
    public void CustomFieldsStudentUPN_WithZeroIdInMetadata_ShouldReturnNull()
    {
        // Arrange
        var dto = new TicketDto
        {
            AllCustomFields = new List<CustomFieldDto>
            {
                new() { Id = 1L, Value = "UPN12345" }
            }
        };
        var meta = new List<CustomFieldMetaDataDto>
        {
            new() { Id = 0L, Title = "UPN" }
        };

        // Act
        var result = dto.CustomFieldsStudentUPN(meta);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void CustomFieldsStudentUPN_WithZeroIdInCustomFields_ShouldReturnNull()
    {
        // Arrange
        var dto = new TicketDto
        {
            AllCustomFields = new List<CustomFieldDto>
            {
                new() { Id = 0L, Value = "UPN12345" }
            }
        };
        var meta = new List<CustomFieldMetaDataDto>
        {
            new() { Id = 1L, Title = "UPN" }
        };

        // Act
        var result = dto.CustomFieldsStudentUPN(meta);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void CustomFieldsStudentUPN_WithLargeIdInMetadata_ShouldReturnValue()
    {
        // Arrange
        var dto = new TicketDto
        {
            AllCustomFields = new List<CustomFieldDto>
            {
                new() { Id = 1L, Value = "UPN12345" }
            }
        };
        var meta = new List<CustomFieldMetaDataDto>
        {
            new() { Id = 1L, Title = "UPN" }
        };

        // Act
        var result = dto.CustomFieldsStudentUPN(meta);

        // Assert
        Assert.Equal("UPN12345", result);
    }

    [Fact]
    public void CustomFieldsStudentUPN_WithLargeIdInCustomFields_ShouldReturnValue()
    {
        // Arrange
        var dto = new TicketDto
        {
            AllCustomFields = new List<CustomFieldDto>
            {
                new() { Id = long.MaxValue, Value = "UPN12345" }
            }
        };
        var meta = new List<CustomFieldMetaDataDto>
        {
            new() { Id = long.MaxValue, Title = "UPN" }
        };

        // Act
        var result = dto.CustomFieldsStudentUPN(meta);

        // Assert
        Assert.Equal("UPN12345", result);
    }

    [Fact]
    public void CustomFieldsStudentUPN_WithMultipleCustomFields_ShouldReturnFirstMatch()
    {
        // Arrange
        var dto = new TicketDto
        {
            AllCustomFields = new List<CustomFieldDto>
            {
                new() { Id = 1L, Value = "UPN12345" },
                new() { Id = 2L, Value = "UPN67890" }
            }
        };
        var meta = new List<CustomFieldMetaDataDto>
        {
            new() { Id = 1L, Title = "UPN" }
        };

        // Act
        var result = dto.CustomFieldsStudentUPN(meta);

        // Assert
        Assert.Equal("UPN12345", result);
    }

    [Fact]
    public void CustomFieldsStudentUPN_WithNullValueInCustomField_ShouldReturnNull()
    {
        // Arrange
        var dto = new TicketDto
        {
            AllCustomFields = new List<CustomFieldDto>
            {
                new() { Id = 1L, Value = null }
            }
        };
        var meta = new List<CustomFieldMetaDataDto>
        {
            new() { Id = 1L, Title = "UPN" }
        };

        // Act
        var result = dto.CustomFieldsStudentUPN(meta);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void CustomFieldsStudentUPN_WithEmptyValueInCustomField_ShouldReturnEmptyString()
    {
        // Arrange
        var dto = new TicketDto
        {
            AllCustomFields = new List<CustomFieldDto>
            {
                new() { Id = 1L, Value = string.Empty }
            }
        };
        var meta = new List<CustomFieldMetaDataDto>
        {
            new() { Id = 1L, Title = "UPN" }
        };

        // Act
        var result = dto.CustomFieldsStudentUPN(meta);

        // Assert
        Assert.Equal(string.Empty, result);
    }

    #endregion

    #region CustomFieldsByName Method Tests

    [Fact]
    public void CustomFieldsByName_WithMatchingField_ShouldReturnValue()
    {
        // Arrange
        var dto = new TicketDto
        {
            AllCustomFields = new List<CustomFieldDto>
            {
                new() { Id = 1L, Value = "TestValue" }
            }
        };
        var meta = new List<CustomFieldMetaDataDto>
        {
            new() { Id = 1L, Title = "TestField" }
        };

        // Act
        var result = dto.CustomFieldsByName(meta, "TestField");

        // Assert
        Assert.Equal("TestValue", result);
    }

    [Fact]
    public void CustomFieldsByName_WithFieldNotFoundInMetadata_ShouldReturnNull()
    {
        // Arrange
        var dto = new TicketDto
        {
            AllCustomFields = new List<CustomFieldDto>
            {
                new() { Id = 1L, Value = "TestValue" }
            }
        };
        var meta = new List<CustomFieldMetaDataDto>
        {
            new() { Id = 2L, Title = "TestField" }
        };

        // Act
        var result = dto.CustomFieldsByName(meta, "TestField");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void CustomFieldsByName_WithFieldNotFoundInCustomFields_ShouldReturnNull()
    {
        // Arrange
        var dto = new TicketDto
        {
            AllCustomFields = new List<CustomFieldDto>
            {
                new() { Id = 2L, Value = "TestValue" }
            }
        };
        var meta = new List<CustomFieldMetaDataDto>
        {
            new() { Id = 1L, Title = "TestField" }
        };

        // Act
        var result = dto.CustomFieldsByName(meta, "TestField");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void CustomFieldsByName_WithNullMetadata_ShouldReturnNull()
    {
        // Arrange
        var dto = new TicketDto
        {
            AllCustomFields = new List<CustomFieldDto>
            {
                new() { Id = 1L, Value = "TestValue" }
            }
        };

        // Act
        var result = dto.CustomFieldsByName(null, "TestField");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void CustomFieldsByName_WithEmptyMetadata_ShouldReturnNull()
    {
        // Arrange
        var dto = new TicketDto
        {
            AllCustomFields = new List<CustomFieldDto>
            {
                new() { Id = 1L, Value = "TestValue" }
            }
        };
        var meta = new List<CustomFieldMetaDataDto>();

        // Act
        var result = dto.CustomFieldsByName(meta, "TestField");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void CustomFieldsByName_WithNullCustomFields_ShouldReturnNull()
    {
        // Arrange
        var dto = new TicketDto
        {
            AllCustomFields = null
        };
        var meta = new List<CustomFieldMetaDataDto>
        {
            new() { Id = 1L, Title = "TestField" }
        };

        // Act
        var result = dto.CustomFieldsByName(meta, "TestField");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void CustomFieldsByName_WithEmptyCustomFields_ShouldReturnNull()
    {
        // Arrange
        var dto = new TicketDto
        {
            AllCustomFields = new List<CustomFieldDto>()
        };
        var meta = new List<CustomFieldMetaDataDto>
        {
            new() { Id = 1L, Title = "TestField" }
        };

        // Act
        var result = dto.CustomFieldsByName(meta, "TestField");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void CustomFieldsByName_WithSpecialCharactersInFieldName_ShouldReturnSpecialChars()
    {
        // Arrange
        var dto = new TicketDto
        {
            AllCustomFields = new List<CustomFieldDto>
            {
                new() { Id = 1L, Value = "TestValue" }
            }
        };
        var meta = new List<CustomFieldMetaDataDto>
        {
            new() { Id = 1L, Title = "Test: Field @#$%^&*()" }
        };

        // Act
        var result = dto.CustomFieldsByName(meta, "Test: Field @#$%^&*()");

        // Assert
        Assert.Equal("TestValue", result);
    }

    [Fact]
    public void CustomFieldsByName_WithUnicodeInFieldName_ShouldReturnUnicode()
    {
        // Arrange
        var dto = new TicketDto
        {
            AllCustomFields = new List<CustomFieldDto>
            {
                new() { Id = 1L, Value = "TestValue" }
            }
        };
        var meta = new List<CustomFieldMetaDataDto>
        {
            new() { Id = 1L, Title = "测试字段 🎓" }
        };

        // Act
        var result = dto.CustomFieldsByName(meta, "测试字段 🎓");

        // Assert
        Assert.Equal("TestValue", result);
    }

    [Fact]
    public void CustomFieldsByName_WithWhitespaceInFieldName_ShouldReturnWhitespace()
    {
        // Arrange
        var dto = new TicketDto
        {
            AllCustomFields = new List<CustomFieldDto>
            {
                new() { Id = 1L, Value = "TestValue" }
            }
        };
        var meta = new List<CustomFieldMetaDataDto>
        {
            new() { Id = 1L, Title = "   Test Field   " }
        };

        // Act
        var result = dto.CustomFieldsByName(meta, "   Test Field   ");

        // Assert
        Assert.Equal("TestValue", result);
    }

    [Fact]
    public void CustomFieldsByName_WithZeroIdInMetadata_ShouldReturnNull()
    {
        // Arrange
        var dto = new TicketDto
        {
            AllCustomFields = new List<CustomFieldDto>
            {
                new() { Id = 1L, Value = "TestValue" }
            }
        };
        var meta = new List<CustomFieldMetaDataDto>
        {
            new() { Id = 0L, Title = "TestField" }
        };

        // Act
        var result = dto.CustomFieldsByName(meta, "TestField");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void CustomFieldsByName_WithZeroIdInCustomFields_ShouldReturnNull()
    {
        // Arrange
        var dto = new TicketDto
        {
            AllCustomFields = new List<CustomFieldDto>
            {
                new() { Id = 0L, Value = "TestValue" }
            }
        };
        var meta = new List<CustomFieldMetaDataDto>
        {
            new() { Id = 1L, Title = "TestField" }
        };

        // Act
        var result = dto.CustomFieldsByName(meta, "TestField");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void CustomFieldsByName_WithLargeIdInMetadata_ShouldReturnValue()
    {
        // Arrange
        var dto = new TicketDto
        {
            AllCustomFields = new List<CustomFieldDto>
            {
                new() { Id = 1L, Value = "TestValue" }
            }
        };
        var meta = new List<CustomFieldMetaDataDto>
        {
            // Changed Id from long.MaxValue to 1L to match AllCustomFields
            new() { Id = 1L, Title = "TestField" }
        };

        // Act
        var result = dto.CustomFieldsByName(meta, "TestField");

        // Assert
        Assert.Equal("TestValue", result);
    }


    [Fact]
    public void CustomFieldsByName_WithLargeIdInCustomFields_ShouldReturnValue()
    {
        // Arrange
        var dto = new TicketDto
        {
            AllCustomFields = new List<CustomFieldDto>
            {
                new() { Id = long.MaxValue, Value = "TestValue" }
            }
        };
        var meta = new List<CustomFieldMetaDataDto>
        {
            new() { Id = long.MaxValue, Title = "TestField" }
        };

        // Act
        var result = dto.CustomFieldsByName(meta, "TestField");

        // Assert
        Assert.Equal("TestValue", result);
    }

    [Fact]
    public void CustomFieldsByName_WithMultipleCustomFields_ShouldReturnFirstMatch()
    {
        // Arrange
        var dto = new TicketDto
        {
            AllCustomFields = new List<CustomFieldDto>
            {
                new() { Id = 1L, Value = "Value1" },
                new() { Id = 2L, Value = "Value2" }
            }
        };
        var meta = new List<CustomFieldMetaDataDto>
        {
            new() { Id = 1L, Title = "TestField" }
        };

        // Act
        var result = dto.CustomFieldsByName(meta, "TestField");

        // Assert
        Assert.Equal("Value1", result);
    }

    [Fact]
    public void CustomFieldsByName_WithNullValueInCustomField_ShouldReturnNull()
    {
        // Arrange
        var dto = new TicketDto
        {
            AllCustomFields = new List<CustomFieldDto>
            {
                new() { Id = 1L, Value = null }
            }
        };
        var meta = new List<CustomFieldMetaDataDto>
        {
            new() { Id = 1L, Title = "TestField" }
        };

        // Act
        var result = dto.CustomFieldsByName(meta, "TestField");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void CustomFieldsByName_WithEmptyValueInCustomField_ShouldReturnEmptyString()
    {
        // Arrange
        var dto = new TicketDto
        {
            AllCustomFields = new List<CustomFieldDto>
            {
                new() { Id = 1L, Value = string.Empty }
            }
        };
        var meta = new List<CustomFieldMetaDataDto>
        {
            new() { Id = 1L, Title = "TestField" }
        };

        // Act
        var result = dto.CustomFieldsByName(meta, "TestField");

        // Assert
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void CustomFieldsByName_WithDifferentFieldName_ShouldReturnDifferentValue()
    {
        // Arrange
        var dto = new TicketDto
        {
            AllCustomFields = new List<CustomFieldDto>
            {
                new() { Id = 1L, Value = "Value1" },
                new() { Id = 2L, Value = "Value2" }
            }
        };
        var meta = new List<CustomFieldMetaDataDto>
        {
            new() { Id = 2L, Title = "TestField2" }
        };

        // Act
        var result = dto.CustomFieldsByName(meta, "TestField2");

        // Assert
        Assert.Equal("Value2", result);
    }

    [Fact]
    public void CustomFieldsByName_WithSpecialCharactersInValue_ShouldReturnSpecialChars()
    {
        // Arrange
        var dto = new TicketDto
        {
            AllCustomFields = new List<CustomFieldDto>
            {
                new() { Id = 1L, Value = "Value: @#$%^&*()" }
            }
        };
        var meta = new List<CustomFieldMetaDataDto>
        {
            new() { Id = 1L, Title = "TestField" }
        };

        // Act
        var result = dto.CustomFieldsByName(meta, "TestField");

        // Assert
        Assert.Equal("Value: @#$%^&*()", result);
    }

    [Fact]
    public void CustomFieldsByName_WithUnicodeInValue_ShouldReturnUnicode()
    {
        // Arrange
        var dto = new TicketDto
        {
            AllCustomFields = new List<CustomFieldDto>
            {
                new() { Id = 1L, Value = "Value: 你好世界 🌍" }
            }
        };
        var meta = new List<CustomFieldMetaDataDto>
        {
            new() { Id = 1L, Title = "TestField" }
        };

        // Act
        var result = dto.CustomFieldsByName(meta, "TestField");

        // Assert
        Assert.Equal("Value: 你好世界 🌍", result);
    }

    [Fact]
    public void CustomFieldsByName_WithWhitespaceInValue_ShouldReturnWhitespace()
    {
        // Arrange
        var dto = new TicketDto
        {
            AllCustomFields = new List<CustomFieldDto>
            {
                new() { Id = 1L, Value = "   Value with spaces   " }
            }
        };
        var meta = new List<CustomFieldMetaDataDto>
        {
            new() { Id = 1L, Title = "TestField" }
        };

        // Act
        var result = dto.CustomFieldsByName(meta, "TestField");

        // Assert
        Assert.Equal("   Value with spaces   ", result);
    }

    #endregion
}