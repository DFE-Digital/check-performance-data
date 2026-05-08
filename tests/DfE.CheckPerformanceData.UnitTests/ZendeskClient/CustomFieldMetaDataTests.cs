using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Xunit;
using DfE.CheckPerformanceData.Infrastructure.ZendeskClient.Models;

namespace DfE.CheckPerformanceData.UnitTests.ZendeskClient;

/// <summary>
/// Unit tests for CustomFieldMetaData and CustomFieldOption classes.
/// </summary>
public class CustomFieldMetaDataTests
{
    #region CustomFieldMetaData Tests

    [Fact]
    public void CustomFieldMetaData_WithAllProperties_ShouldSetAllProperties()
    {
        // Arrange
        var field = new CustomFieldMetaData();

        // Act
        field.Url = "https://example.com";
        field.Id = 123L;
        field.Type = "text";
        field.Key = "custom_field_key";
        field.Title = "Custom Field";
        field.Description = "A custom field description";
        field.RawTitle = "Custom Field (raw)";
        field.RawDescription = "A custom field description (raw)";
        field.Position = 1;
        field.Active = true;
        field.System = false;
        field.RegexpForValidation = @"^[a-zA-Z0-9]+$";
        field.CreatedAt = new DateTime(2024, 1, 1);
        field.UpdatedAt = new DateTime(2024, 1, 2);

        // Assert
        Assert.Equal("https://example.com", field.Url);
        Assert.Equal(123L, field.Id);
        Assert.Equal("text", field.Type);
        Assert.Equal("custom_field_key", field.Key);
        Assert.Equal("Custom Field", field.Title);
        Assert.Equal("A custom field description", field.Description);
        Assert.Equal("Custom Field (raw)", field.RawTitle);
        Assert.Equal("A custom field description (raw)", field.RawDescription);
        Assert.Equal(1, field.Position);
        Assert.True(field.Active);
        Assert.False(field.System);
        Assert.Equal(@"^[a-zA-Z0-9]+$", field.RegexpForValidation);
        Assert.Equal(new DateTime(2024, 1, 1), field.CreatedAt);
        Assert.Equal(new DateTime(2024, 1, 2), field.UpdatedAt);
    }

    [Fact]
    public void CustomFieldMetaData_WithNullDescription_ShouldAllowNull()
    {
        // Arrange
        var field = new CustomFieldMetaData();

        // Act
        field.Description = null;
        field.RawDescription = null;

        // Assert
        Assert.Null(field.Description);
        Assert.Null(field.RawDescription);
    }

    [Fact]
    public void CustomFieldMetaData_WithEmptyStrings_ShouldSetEmptyStrings()
    {
        // Arrange
        var field = new CustomFieldMetaData();

        // Act
        field.Url = string.Empty;
        field.Type = string.Empty;
        field.Key = string.Empty;
        field.Title = string.Empty;
        field.RawTitle = string.Empty;

        // Assert
        Assert.Equal(string.Empty, field.Url);
        Assert.Equal(string.Empty, field.Type);
        Assert.Equal(string.Empty, field.Key);
        Assert.Equal(string.Empty, field.Title);
        Assert.Equal(string.Empty, field.RawTitle);
    }

    [Fact]
    public void CustomFieldMetaData_DefaultValues_ShouldHaveExpectedDefaults()
    {
        // Arrange
        var field = new CustomFieldMetaData();

        // Assert
        Assert.Equal(string.Empty, field.Url);
        Assert.Equal(0L, field.Id);
        Assert.Equal(string.Empty, field.Type);
        Assert.Equal(string.Empty, field.Key);
        Assert.Equal(string.Empty, field.Title);
        Assert.Null(field.Description);
        Assert.Equal(string.Empty, field.RawTitle);
        Assert.Null(field.RawDescription);
        Assert.Equal(0, field.Position);
        Assert.False(field.Active);
        Assert.False(field.System);
        Assert.Null(field.RegexpForValidation);
        Assert.Equal(default(DateTime), field.CreatedAt);
        Assert.Equal(default(DateTime), field.UpdatedAt);
        Assert.Empty(field.CustomFieldOptions);
    }

    [Fact]
    public void CustomFieldMetaData_WithCustomFieldOptions_ShouldPopulateList()
    {
        // Arrange
        var field = new CustomFieldMetaData();
        var options = new List<CustomFieldOption>
        {
            new() { Id = 1L, Name = "Option 1", RawName = "Option 1 (raw)", Value = "value1" },
            new() { Id = 2L, Name = "Option 2", RawName = "Option 2 (raw)", Value = "value2" }
        };

        // Act
        field.CustomFieldOptions = options;

        // Assert
        Assert.Equal(2, field.CustomFieldOptions.Count);
        Assert.Equal(1L, field.CustomFieldOptions[0].Id);
        Assert.Equal("Option 1", field.CustomFieldOptions[0].Name);
        Assert.Equal("Option 1 (raw)", field.CustomFieldOptions[0].RawName);
        Assert.Equal("value1", field.CustomFieldOptions[0].Value);
        Assert.Equal(2L, field.CustomFieldOptions[1].Id);
        Assert.Equal("Option 2", field.CustomFieldOptions[1].Name);
        Assert.Equal("Option 2 (raw)", field.CustomFieldOptions[1].RawName);
        Assert.Equal("value2", field.CustomFieldOptions[1].Value);
    }

    [Fact]
    public void CustomFieldMetaData_WithEmptyCustomFieldOptions_ShouldInitializeEmptyList()
    {
        // Arrange
        var field = new CustomFieldMetaData();

        // Act
        field.CustomFieldOptions = new List<CustomFieldOption>();

        // Assert
        Assert.Empty(field.CustomFieldOptions);
    }

    [Fact]
    public void CustomFieldMetaData_WithZeroId_ShouldAllowZero()
    {
        // Arrange
        var field = new CustomFieldMetaData();

        // Act
        field.Id = 0L;

        // Assert
        Assert.Equal(0L, field.Id);
    }

    [Fact]
    public void CustomFieldMetaData_WithNegativePosition_ShouldAllowNegative()
    {
        // Arrange
        var field = new CustomFieldMetaData();

        // Act
        field.Position = -1;

        // Assert
        Assert.Equal(-1, field.Position);
    }

    [Fact]
    public void CustomFieldMetaData_WithNegativePosition_ShouldAllowNegativeValue()
    {
        // Arrange
        var field = new CustomFieldMetaData();

        // Act
        field.Position = -5;

        // Assert
        Assert.Equal(-5, field.Position);
    }

    [Fact]
    public void CustomFieldMetaData_WithMaxPosition_ShouldAllowMaxValue()
    {
        // Arrange
        var field = new CustomFieldMetaData();

        // Act
        field.Position = int.MaxValue;

        // Assert
        Assert.Equal(int.MaxValue, field.Position);
    }

    [Fact]
    public void CustomFieldMetaData_WithSystemTrue_ShouldMarkAsSystemField()
    {
        // Arrange
        var field = new CustomFieldMetaData();

        // Act
        field.System = true;

        // Assert
        Assert.True(field.System);
    }

    [Fact]
    public void CustomFieldMetaData_WithActiveFalse_ShouldMarkAsInactive()
    {
        // Arrange
        var field = new CustomFieldMetaData();

        // Act
        field.Active = false;

        // Assert
        Assert.False(field.Active);
    }

    [Fact]
    public void CustomFieldMetaData_WithRegexpForValidation_ShouldStoreRegex()
    {
        // Arrange
        var field = new CustomFieldMetaData();

        // Act
        field.RegexpForValidation = @"^\d{3}-\d{2}-\d{4}$";

        // Assert
        Assert.Equal(@"^\d{3}-\d{2}-\d{4}$", field.RegexpForValidation);
    }

    [Fact]
    public void CustomFieldMetaData_WithUtcDateTime_ShouldStoreUtcTime()
    {
        // Arrange
        var field = new CustomFieldMetaData();
        var utcTime = DateTime.UtcNow;

        // Act
        field.CreatedAt = utcTime;
        field.UpdatedAt = utcTime;

        // Assert
        Assert.Equal(utcTime, field.CreatedAt);
        Assert.Equal(utcTime, field.UpdatedAt);
    }

    [Fact]
    public void CustomFieldMetaData_WithDifferentTimeZones_ShouldStoreTime()
    {
        // Arrange
        var field = new CustomFieldMetaData();
        var localTime = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Local);

        // Act
        field.CreatedAt = localTime;

        // Assert
        Assert.Equal(localTime, field.CreatedAt);
        Assert.Equal(DateTimeKind.Local, field.CreatedAt.Kind);
    }

    [Fact]
    public void CustomFieldMetaData_WithSpecialCharactersInTitle_ShouldStoreSpecialChars()
    {
        // Arrange
        var field = new CustomFieldMetaData();

        // Act
        field.Title = "Field with special chars: @#$%^&*()";

        // Assert
        Assert.Equal("Field with special chars: @#$%^&*()", field.Title);
    }

    [Fact]
    public void CustomFieldMetaData_WithUnicodeInDescription_ShouldStoreUnicode()
    {
        // Arrange
        var field = new CustomFieldMetaData();

        // Act
        field.Description = "Field with unicode: 你好世界 🌍";

        // Assert
        Assert.Equal("Field with unicode: 你好世界 🌍", field.Description);
    }

    [Fact]
    public void CustomFieldMetaData_WithLargeId_ShouldAllowLargeValue()
    {
        // Arrange
        var field = new CustomFieldMetaData();

        // Act
        field.Id = long.MaxValue;

        // Assert
        Assert.Equal(long.MaxValue, field.Id);
    }

    [Fact]
    public void CustomFieldMetaData_WithLargePosition_ShouldAllowLargeValue()
    {
        // Arrange
        var field = new CustomFieldMetaData();

        // Act
        field.Position = int.MaxValue;

        // Assert
        Assert.Equal(int.MaxValue, field.Position);
    }

    [Fact]
    public void CustomFieldMetaData_WithMultipleCustomFieldOptions_ShouldHandleMultiple()
    {
        // Arrange
        var field = new CustomFieldMetaData();
        var options = new List<CustomFieldOption>
        {
            new() { Id = 1L, Name = "Option 1", RawName = "Option 1 (raw)", Value = "value1" },
            new() { Id = 2L, Name = "Option 2", RawName = "Option 2 (raw)", Value = "value2" },
            new() { Id = 3L, Name = "Option 3", RawName = "Option 3 (raw)", Value = "value3" }
        };

        // Act
        field.CustomFieldOptions = options;

        // Assert
        Assert.Equal(3, field.CustomFieldOptions.Count);
        Assert.Equal("value1", field.CustomFieldOptions[0].Value);
        Assert.Equal("value2", field.CustomFieldOptions[1].Value);
        Assert.Equal("value3", field.CustomFieldOptions[2].Value);
    }

    [Fact]
    public void CustomFieldMetaData_WithNullCustomFieldOptions_ShouldAllowNull()
    {
        // Arrange
        var field = new CustomFieldMetaData();

        // Act
        field.CustomFieldOptions = null;

        // Assert
        Assert.Null(field.CustomFieldOptions);
    }

    [Fact]
    public void CustomFieldMetaData_WithSpecialCharactersInRegexp_ShouldStoreSpecialChars()
    {
        // Arrange
        var field = new CustomFieldMetaData();

        // Act
        field.RegexpForValidation = @"^[a-zA-Z0-9_\-\.]+$";

        // Assert
        Assert.Equal(@"^[a-zA-Z0-9_\-\.]+$", field.RegexpForValidation);
    }

    [Fact]
    public void CustomFieldMetaData_WithWhitespaceInTitle_ShouldStoreWhitespace()
    {
        // Arrange
        var field = new CustomFieldMetaData();

        // Act
        field.Title = "   Title with spaces   ";

        // Assert
        Assert.Equal("   Title with spaces   ", field.Title);
    }

    [Fact]
    public void CustomFieldMetaData_WithZeroPosition_ShouldAllowZero()
    {
        // Arrange
        var field = new CustomFieldMetaData();

        // Act
        field.Position = 0;

        // Assert
        Assert.Equal(0, field.Position);
    }

    [Fact]
    public void CustomFieldMetaData_WithDifferentCreatedAtTimes_ShouldStoreDifferentTimes()
    {
        // Arrange
        var field = new CustomFieldMetaData();

        // Act
        field.CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0);
        field.UpdatedAt = new DateTime(2024, 1, 2, 0, 0, 0);

        // Assert
        Assert.Equal(new DateTime(2024, 1, 1), field.CreatedAt);
        Assert.Equal(new DateTime(2024, 1, 2), field.UpdatedAt);
    }

    [Fact]
    public void CustomFieldMetaData_WithMinDateTime_ShouldAllowMinValue()
    {
        // Arrange
        var field = new CustomFieldMetaData();

        // Act
        field.CreatedAt = DateTime.MinValue;

        // Assert
        Assert.Equal(DateTime.MinValue, field.CreatedAt);
    }

    [Fact]
    public void CustomFieldMetaData_WithMaxDateTime_ShouldAllowMaxValue()
    {
        // Arrange
        var field = new CustomFieldMetaData();

        // Act
        field.CreatedAt = DateTime.MaxValue;

        // Assert
        Assert.Equal(DateTime.MaxValue, field.CreatedAt);
    }

    [Fact]
    public void CustomFieldMetaData_WithEmptyCustomFieldOptionsList_ShouldInitialize()
    {
        // Arrange
        var field = new CustomFieldMetaData();

        // Act
        field.CustomFieldOptions = new List<CustomFieldOption>();

        // Assert
        Assert.Empty(field.CustomFieldOptions);
        Assert.NotNull(field.CustomFieldOptions);
    }

    [Fact]
    public void CustomFieldMetaData_WithSingleCustomFieldOption_ShouldHandleSingle()
    {
        // Arrange
        var field = new CustomFieldMetaData();
        var options = new List<CustomFieldOption>
        {
            new() { Id = 1L, Name = "Single Option", RawName = "Single Option (raw)", Value = "single" }
        };

        // Act
        field.CustomFieldOptions = options;

        // Assert
        Assert.Single(field.CustomFieldOptions);
        Assert.Equal("single", field.CustomFieldOptions[0].Value);
    }

    #endregion

    #region CustomFieldOption Tests

    [Fact]
    public void CustomFieldOption_WithAllProperties_ShouldSetAllProperties()
    {
        // Arrange
        var option = new CustomFieldOption();

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
    public void CustomFieldOption_WithEmptyStrings_ShouldSetEmptyStrings()
    {
        // Arrange
        var option = new CustomFieldOption();

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
    public void CustomFieldOption_DefaultValues_ShouldHaveExpectedDefaults()
    {
        // Arrange
        var option = new CustomFieldOption();

        // Assert
        Assert.Equal(0L, option.Id);
        Assert.Equal(string.Empty, option.Name);
        Assert.Equal(string.Empty, option.RawName);
        Assert.Equal(string.Empty, option.Value);
    }

    [Fact]
    public void CustomFieldOption_WithZeroId_ShouldAllowZero()
    {
        // Arrange
        var option = new CustomFieldOption();

        // Act
        option.Id = 0L;

        // Assert
        Assert.Equal(0L, option.Id);
    }

    [Fact]
    public void CustomFieldOption_WithSpecialCharactersInName_ShouldStoreSpecialChars()
    {
        // Arrange
        var option = new CustomFieldOption();

        // Act
        option.Name = "Option with: /?\\|<>()[]{}";

        // Assert
        Assert.Equal("Option with: /?\\|<>()[]{}", option.Name);
    }

    [Fact]
    public void CustomFieldOption_WithUnicodeInValue_ShouldStoreUnicode()
    {
        // Arrange
        var option = new CustomFieldOption();

        // Act
        option.Value = "value with unicode: 你好";

        // Assert
        Assert.Equal("value with unicode: 你好", option.Value);
    }

    [Fact]
    public void CustomFieldOption_WithNullName_ShouldAllowNull()
    {
        // Arrange
        var option = new CustomFieldOption();

        // Act
        option.Name = null;
        option.RawName = null;

        // Assert
        Assert.Null(option.Name);
        Assert.Null(option.RawName);
    }

    [Fact]
    public void CustomFieldOption_WithEmptyValue_ShouldSetEmpty()
    {
        // Arrange
        var option = new CustomFieldOption();

        // Act
        option.Value = string.Empty;

        // Assert
        Assert.Equal(string.Empty, option.Value);
    }

    [Fact]
    public void CustomFieldOption_WithWhitespaceInName_ShouldStoreWhitespace()
    {
        // Arrange
        var option = new CustomFieldOption();

        // Act
        option.Name = "   Option with spaces   ";

        // Assert
        Assert.Equal("   Option with spaces   ", option.Name);
    }

    [Fact]
    public void CustomFieldOption_WithNumberInName_ShouldStoreNumber()
    {
        // Arrange
        var option = new CustomFieldOption();

        // Act
        option.Name = "Option123";

        // Assert
        Assert.Equal("Option123", option.Name);
    }

    [Fact]
    public void CustomFieldOption_WithLargeId_ShouldAllowLargeValue()
    {
        // Arrange
        var option = new CustomFieldOption();

        // Act
        option.Id = long.MaxValue;

        // Assert
        Assert.Equal(long.MaxValue, option.Id);
    }

    [Fact]
    public void CustomFieldOption_WithSpecialCharactersInValue_ShouldStoreSpecialChars()
    {
        // Arrange
        var option = new CustomFieldOption();

        // Act
        option.Value = "value with special: @#$%^&*()";

        // Assert
        Assert.Equal("value with special: @#$%^&*()", option.Value);
    }

    [Fact]
    public void CustomFieldOption_WithUnicodeInName_ShouldStoreUnicode()
    {
        // Arrange
        var option = new CustomFieldOption();

        // Act
        option.Name = "选项名称";

        // Assert
        Assert.Equal("选项名称", option.Name);
    }

    [Fact]
    public void CustomFieldOption_WithEmptyRawName_ShouldSetEmpty()
    {
        // Arrange
        var option = new CustomFieldOption();

        // Act
        option.RawName = string.Empty;

        // Assert
        Assert.Equal(string.Empty, option.RawName);
    }

    [Fact]
    public void CustomFieldOption_WithWhitespaceInValue_ShouldStoreWhitespace()
    {
        // Arrange
        var option = new CustomFieldOption();

        // Act
        option.Value = "   value with spaces   ";

        // Assert
        Assert.Equal("   value with spaces   ", option.Value);
    }

    [Fact]
    public void CustomFieldOption_WithZeroValue_ShouldAllowZero()
    {
        // Arrange
        var option = new CustomFieldOption();

        // Act
        option.Value = "0";

        // Assert
        Assert.Equal("0", option.Value);
    }

    [Fact]
    public void CustomFieldOption_WithMinDateTime_ShouldAllowMinValue()
    {
        // Arrange
        var option = new CustomFieldOption();

        // Act
        option.Id = long.MinValue;

        // Assert
        Assert.Equal(long.MinValue, option.Id);
    }

    [Fact]
    public void CustomFieldOption_WithMaxDateTime_ShouldAllowMaxValue()
    {
        // Arrange
        var option = new CustomFieldOption();

        // Act
        option.Id = long.MaxValue;

        // Assert
        Assert.Equal(long.MaxValue, option.Id);
    }

    [Fact]
    public void CustomFieldOption_WithSpecialCharactersInRawName_ShouldStoreSpecialChars()
    {
        // Arrange
        var option = new CustomFieldOption();

        // Act
        option.RawName = "Option with: /?\\|<>()[]{} (raw)";

        // Assert
        Assert.Equal("Option with: /?\\|<>()[]{} (raw)", option.RawName);
    }

    [Fact]
    public void CustomFieldOption_WithUnicodeInRawName_ShouldStoreUnicode()
    {
        // Arrange
        var option = new CustomFieldOption();

        // Act
        option.RawName = "选项名称 (raw)";

        // Assert
        Assert.Equal("选项名称 (raw)", option.RawName);
    }

    [Fact]
    public void CustomFieldOption_WithEmptyName_ShouldSetEmpty()
    {
        // Arrange
        var option = new CustomFieldOption();

        // Act
        option.Name = string.Empty;

        // Assert
        Assert.Equal(string.Empty, option.Name);
    }

    [Fact]
    public void CustomFieldOption_WithWhitespaceInRawName_ShouldStoreWhitespace()
    {
        // Arrange
        var option = new CustomFieldOption();

        // Act
        option.RawName = "   Option with spaces (raw)   ";

        // Assert
        Assert.Equal("   Option with spaces (raw)   ", option.RawName);
    }

    [Fact]
    public void CustomFieldOption_WithZeroValue_ShouldAllowZeroString()
    {
        // Arrange
        var option = new CustomFieldOption();

        // Act
        option.Value = "0";

        // Assert
        Assert.Equal("0", option.Value);
    }

    #endregion
}