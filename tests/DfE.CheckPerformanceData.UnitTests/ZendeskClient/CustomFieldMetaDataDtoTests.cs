using System;
using System.Collections.Generic;
using Xunit;
using DfE.CheckPerformanceData.Application.ZendeskClient;

namespace DfE.CheckPerformanceData.UnitTests.ZendeskClient;

/// <summary>
/// Unit tests for CustomFieldMetaDataDto and CustomFieldOptionDto classes.
/// </summary>
public class CustomFieldMetaDataDtoTests
{
    #region CustomFieldMetaDataDto Tests

    [Fact]
    public void CustomFieldMetaDataDto_WithAllProperties_ShouldSetAllProperties()
    {
        // Arrange
        var dto = new CustomFieldMetaDataDto();

        // Act
        dto.Url = "https://example.com";
        dto.Id = 123L;
        dto.Type = "text";
        dto.Key = "custom_field_key";
        dto.Title = "Custom Field";
        dto.Description = "A custom field description";
        dto.RawTitle = "Custom Field (raw)";
        dto.RawDescription = "A custom field description (raw)";
        dto.Position = 1;
        dto.Active = true;
        dto.System = false;
        dto.RegexpForValidation = @"^[a-zA-Z0-9]+$";
        dto.CreatedAt = new DateTime(2024, 1, 1);
        dto.UpdatedAt = new DateTime(2024, 1, 2);

        // Assert
        Assert.Equal("https://example.com", dto.Url);
        Assert.Equal(123L, dto.Id);
        Assert.Equal("text", dto.Type);
        Assert.Equal("custom_field_key", dto.Key);
        Assert.Equal("Custom Field", dto.Title);
        Assert.Equal("A custom field description", dto.Description);
        Assert.Equal("Custom Field (raw)", dto.RawTitle);
        Assert.Equal("A custom field description (raw)", dto.RawDescription);
        Assert.Equal(1, dto.Position);
        Assert.True(dto.Active);
        Assert.False(dto.System);
        Assert.Equal(@"^[a-zA-Z0-9]+$", dto.RegexpForValidation);
        Assert.Equal(new DateTime(2024, 1, 1), dto.CreatedAt);
        Assert.Equal(new DateTime(2024, 1, 2), dto.UpdatedAt);
    }

    [Fact]
    public void CustomFieldMetaDataDto_WithNullDescription_ShouldAllowNull()
    {
        // Arrange
        var dto = new CustomFieldMetaDataDto();

        // Act
        dto.Description = null;
        dto.RawDescription = null;

        // Assert
        Assert.Null(dto.Description);
        Assert.Null(dto.RawDescription);
    }

    [Fact]
    public void CustomFieldMetaDataDto_WithEmptyStrings_ShouldSetEmptyStrings()
    {
        // Arrange
        var dto = new CustomFieldMetaDataDto();

        // Act
        dto.Url = string.Empty;
        dto.Type = string.Empty;
        dto.Key = string.Empty;
        dto.Title = string.Empty;
        dto.RawTitle = string.Empty;

        // Assert
        Assert.Equal(string.Empty, dto.Url);
        Assert.Equal(string.Empty, dto.Type);
        Assert.Equal(string.Empty, dto.Key);
        Assert.Equal(string.Empty, dto.Title);
        Assert.Equal(string.Empty, dto.RawTitle);
    }

    [Fact]
    public void CustomFieldMetaDataDto_DefaultValues_ShouldHaveExpectedDefaults()
    {
        // Arrange
        var dto = new CustomFieldMetaDataDto();

        // Assert
        Assert.Equal(string.Empty, dto.Url);
        Assert.Equal(0L, dto.Id);
        Assert.Equal(string.Empty, dto.Type);
        Assert.Equal(string.Empty, dto.Key);
        Assert.Equal(string.Empty, dto.Title);
        Assert.Null(dto.Description);
        Assert.Equal(string.Empty, dto.RawTitle);
        Assert.Null(dto.RawDescription);
        Assert.Equal(0, dto.Position);
        Assert.False(dto.Active);
        Assert.False(dto.System);
        Assert.Null(dto.RegexpForValidation);
        Assert.Equal(default(DateTime), dto.CreatedAt);
        Assert.Equal(default(DateTime), dto.UpdatedAt);
        Assert.Empty(dto.CustomFieldOptions);
    }

    [Fact]
    public void CustomFieldMetaDataDto_WithCustomFieldOptions_ShouldPopulateList()
    {
        // Arrange
        var dto = new CustomFieldMetaDataDto();
        var options = new List<CustomFieldOptionDto>
        {
            new() { Id = 1L, Name = "Option 1", RawName = "Option 1 (raw)", Value = "value1" },
            new() { Id = 2L, Name = "Option 2", RawName = "Option 2 (raw)", Value = "value2" }
        };

        // Act
        dto.CustomFieldOptions = options;

        // Assert
        Assert.Equal(2, dto.CustomFieldOptions.Count);
        Assert.Equal(1L, dto.CustomFieldOptions[0].Id);
        Assert.Equal("Option 1", dto.CustomFieldOptions[0].Name);
        Assert.Equal("Option 1 (raw)", dto.CustomFieldOptions[0].RawName);
        Assert.Equal("value1", dto.CustomFieldOptions[0].Value);
        Assert.Equal(2L, dto.CustomFieldOptions[1].Id);
        Assert.Equal("Option 2", dto.CustomFieldOptions[1].Name);
        Assert.Equal("Option 2 (raw)", dto.CustomFieldOptions[1].RawName);
        Assert.Equal("value2", dto.CustomFieldOptions[1].Value);
    }

    [Fact]
    public void CustomFieldMetaDataDto_WithEmptyCustomFieldOptions_ShouldInitializeEmptyList()
    {
        // Arrange
        var dto = new CustomFieldMetaDataDto();

        // Act
        dto.CustomFieldOptions = new List<CustomFieldOptionDto>();

        // Assert
        Assert.Empty(dto.CustomFieldOptions);
    }

    [Fact]
    public void CustomFieldMetaDataDto_WithZeroId_ShouldAllowZero()
    {
        // Arrange
        var dto = new CustomFieldMetaDataDto();

        // Act
        dto.Id = 0L;

        // Assert
        Assert.Equal(0L, dto.Id);
    }

    [Fact]
    public void CustomFieldMetaDataDto_WithNegativePosition_ShouldAllowNegative()
    {
        // Arrange
        var dto = new CustomFieldMetaDataDto();

        // Act
        dto.Position = -1;

        // Assert
        Assert.Equal(-1, dto.Position);
    }

    [Fact]
    public void CustomFieldMetaDataDto_WithMaxPosition_ShouldAllowMaxValue()
    {
        // Arrange
        var dto = new CustomFieldMetaDataDto();

        // Act
        dto.Position = int.MaxValue;

        // Assert
        Assert.Equal(int.MaxValue, dto.Position);
    }

    [Fact]
    public void CustomFieldMetaDataDto_WithSystemTrue_ShouldMarkAsSystemField()
    {
        // Arrange
        var dto = new CustomFieldMetaDataDto();

        // Act
        dto.System = true;

        // Assert
        Assert.True(dto.System);
    }

    [Fact]
    public void CustomFieldMetaDataDto_WithActiveFalse_ShouldMarkAsInactive()
    {
        // Arrange
        var dto = new CustomFieldMetaDataDto();

        // Act
        dto.Active = false;

        // Assert
        Assert.False(dto.Active);
    }

    [Fact]
    public void CustomFieldMetaDataDto_WithRegexpForValidation_ShouldStoreRegex()
    {
        // Arrange
        var dto = new CustomFieldMetaDataDto();

        // Act
        dto.RegexpForValidation = @"^\d{3}-\d{2}-\d{4}$";

        // Assert
        Assert.Equal(@"^\d{3}-\d{2}-\d{4}$", dto.RegexpForValidation);
    }

    [Fact]
    public void CustomFieldMetaDataDto_WithUtcDateTime_ShouldStoreUtcTime()
    {
        // Arrange
        var dto = new CustomFieldMetaDataDto();
        var utcTime = DateTime.UtcNow;

        // Act
        dto.CreatedAt = utcTime;
        dto.UpdatedAt = utcTime;

        // Assert
        Assert.Equal(utcTime, dto.CreatedAt);
        Assert.Equal(utcTime, dto.UpdatedAt);
    }

    [Fact]
    public void CustomFieldMetaDataDto_WithDifferentTimeZones_ShouldStoreTime()
    {
        // Arrange
        var dto = new CustomFieldMetaDataDto();
        var localTime = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Local);

        // Act
        dto.CreatedAt = localTime;

        // Assert
        Assert.Equal(localTime, dto.CreatedAt);
        Assert.Equal(DateTimeKind.Local, dto.CreatedAt.Kind);
    }

    [Fact]
    public void CustomFieldMetaDataDto_WithSpecialCharactersInTitle_ShouldStoreSpecialChars()
    {
        // Arrange
        var dto = new CustomFieldMetaDataDto();

        // Act
        dto.Title = "Field with special chars: @#$%^&*()";

        // Assert
        Assert.Equal("Field with special chars: @#$%^&*()", dto.Title);
    }

    [Fact]
    public void CustomFieldMetaDataDto_WithUnicodeInDescription_ShouldStoreUnicode()
    {
        // Arrange
        var dto = new CustomFieldMetaDataDto();

        // Act
        dto.Description = "Field with unicode: 你好世界 🌍";

        // Assert
        Assert.Equal("Field with unicode: 你好世界 🌍", dto.Description);
    }

    #endregion

    #region CustomFieldOptionDto Tests

    [Fact]
    public void CustomFieldOptionDto_WithAllProperties_ShouldSetAllProperties()
    {
        // Arrange
        var option = new CustomFieldOptionDto();

        // Act
        option.Id = 456L;
        option.Name = "Option Name";
        option.RawName = "Option Name (raw)";
        option.Value = "option_value";

        // Assert
        Assert.Equal(456L, option.Id);
        Assert.Equal("Option Name", option.Name);
        Assert.Equal("Option Name (raw)", option.RawName);
        Assert.Equal("option_value", option.Value);
    }

    [Fact]
    public void CustomFieldOptionDto_WithEmptyStrings_ShouldSetEmptyStrings()
    {
        // Arrange
        var option = new CustomFieldOptionDto();

        // Act
        option.Name = string.Empty;
        option.RawName = string.Empty;
        option.Value = string.Empty;

        // Assert
        Assert.Equal(string.Empty, option.Name);
        Assert.Equal(string.Empty, option.RawName);
        Assert.Equal(string.Empty, option.Value);
    }

    [Fact]
    public void CustomFieldOptionDto_DefaultValues_ShouldHaveExpectedDefaults()
    {
        // Arrange
        var option = new CustomFieldOptionDto();

        // Assert
        Assert.Equal(0L, option.Id);
        Assert.Equal(string.Empty, option.Name);
        Assert.Equal(string.Empty, option.RawName);
        Assert.Equal(string.Empty, option.Value);
    }

    [Fact]
    public void CustomFieldOptionDto_WithZeroId_ShouldAllowZero()
    {
        // Arrange
        var option = new CustomFieldOptionDto();

        // Act
        option.Id = 0L;

        // Assert
        Assert.Equal(0L, option.Id);
    }

    [Fact]
    public void CustomFieldOptionDto_WithSpecialCharactersInName_ShouldStoreSpecialChars()
    {
        // Arrange
        var option = new CustomFieldOptionDto();

        // Act
        option.Name = "Option with: /?\\|<>()[]{}";

        // Assert
        Assert.Equal("Option with: /?\\|<>()[]{}", option.Name);
    }

    [Fact]
    public void CustomFieldOptionDto_WithUnicodeInValue_ShouldStoreUnicode()
    {
        // Arrange
        var option = new CustomFieldOptionDto();

        // Act
        option.Value = "value with unicode: 你好";

        // Assert
        Assert.Equal("value with unicode: 你好", option.Value);
    }

    [Fact]
    public void CustomFieldOptionDto_WithNullName_ShouldAllowNull()
    {
        // Arrange
        var option = new CustomFieldOptionDto();

        // Act
        option.Name = null;
        option.RawName = null;

        // Assert
        Assert.Null(option.Name);
        Assert.Null(option.RawName);
    }

    [Fact]
    public void CustomFieldOptionDto_WithEmptyValue_ShouldSetEmpty()
    {
        // Arrange
        var option = new CustomFieldOptionDto();

        // Act
        option.Value = string.Empty;

        // Assert
        Assert.Equal(string.Empty, option.Value);
    }

    [Fact]
    public void CustomFieldOptionDto_WithWhitespaceInName_ShouldStoreWhitespace()
    {
        // Arrange
        var option = new CustomFieldOptionDto();

        // Act
        option.Name = "   Option with spaces   ";

        // Assert
        Assert.Equal("   Option with spaces   ", option.Name);
    }

    [Fact]
    public void CustomFieldOptionDto_WithNumberInName_ShouldStoreNumber()
    {
        // Arrange
        var option = new CustomFieldOptionDto();

        // Act
        option.Name = "Option123";

        // Assert
        Assert.Equal("Option123", option.Name);
    }

    #endregion
}