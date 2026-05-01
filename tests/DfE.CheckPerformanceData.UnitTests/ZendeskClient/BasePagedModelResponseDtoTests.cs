using System;
using Xunit;
using DfE.CheckPerformanceData.Application.ZendeskClient;

namespace DfE.CheckPerformanceData.UnitTests.ZendeskClient;

/// <summary>
/// Unit tests for BasePagedModelResponseDto class.
/// </summary>
public class BasePagedModelResponseDtoTests
{
    #region Constructor Tests

    [Fact]
    public void BasePagedModelResponseDto_DefaultConstructor_ShouldInitializeProperties()
    {
        // Arrange & Act
        var dto = new BasePagedModelResponseDto();

        // Assert
        Assert.Null(dto.NextPage);
        Assert.Null(dto.PreviousPage);
        Assert.Null(dto.Count);
    }

    [Fact]
    public void BasePagedModelResponseDto_ParameterlessConstructor_ShouldInitializeProperties()
    {
        // Arrange & Act
        var dto = new BasePagedModelResponseDto();

        // Assert
        Assert.Null(dto.NextPage);
        Assert.Null(dto.PreviousPage);
        Assert.Null(dto.Count);
    }

    #endregion

    #region NextPage Property Tests

    [Fact]
    public void BasePagedModelResponseDto_WithNextPage_ShouldSetNextPage()
    {
        // Arrange
        var dto = new BasePagedModelResponseDto();
        var nextPage = new Uri("https://example.com/next");

        // Act
        dto.NextPage = nextPage;

        // Assert
        Assert.Equal(nextPage, dto.NextPage);
    }

    [Fact]
    public void BasePagedModelResponseDto_WithNullNextPage_ShouldAllowNull()
    {
        // Arrange
        var dto = new BasePagedModelResponseDto();
        dto.NextPage = new Uri("https://example.com/next");

        // Act
        dto.NextPage = null;

        // Assert
        Assert.Null(dto.NextPage);
    }

    [Fact]
    public void BasePagedModelResponseDto_WithMultipleNextPageAssignments_ShouldUpdateValue()
    {
        // Arrange
        var dto = new BasePagedModelResponseDto();
        var nextPage1 = new Uri("https://example.com/next1");
        var nextPage2 = new Uri("https://example.com/next2");

        // Act
        dto.NextPage = nextPage1;
        dto.NextPage = nextPage2;

        // Assert
        Assert.Equal(nextPage2, dto.NextPage);
    }

    #endregion

    #region PreviousPage Property Tests

    [Fact]
    public void BasePagedModelResponseDto_WithPreviousPage_ShouldSetPreviousPage()
    {
        // Arrange
        var dto = new BasePagedModelResponseDto();
        var previousPage = new Uri("https://example.com/previous");

        // Act
        dto.PreviousPage = previousPage;

        // Assert
        Assert.Equal(previousPage, dto.PreviousPage);
    }

    [Fact]
    public void BasePagedModelResponseDto_WithNullPreviousPage_ShouldAllowNull()
    {
        // Arrange
        var dto = new BasePagedModelResponseDto();
        dto.PreviousPage = new Uri("https://example.com/previous");

        // Act
        dto.PreviousPage = null;

        // Assert
        Assert.Null(dto.PreviousPage);
    }

    [Fact]
    public void BasePagedModelResponseDto_WithMultiplePreviousPageAssignments_ShouldUpdateValue()
    {
        // Arrange
        var dto = new BasePagedModelResponseDto();
        var previousPage1 = new Uri("https://example.com/previous1");
        var previousPage2 = new Uri("https://example.com/previous2");

        // Act
        dto.PreviousPage = previousPage1;
        dto.PreviousPage = previousPage2;

        // Assert
        Assert.Equal(previousPage2, dto.PreviousPage);
    }

    #endregion

    #region Count Property Tests

    [Fact]
    public void BasePagedModelResponseDto_WithCount_ShouldSetCount()
    {
        // Arrange
        var dto = new BasePagedModelResponseDto();

        // Act
        dto.Count = 100;

        // Assert
        Assert.Equal(100, dto.Count);
    }

    [Fact]
    public void BasePagedModelResponseDto_WithZeroCount_ShouldAllowZero()
    {
        // Arrange
        var dto = new BasePagedModelResponseDto();

        // Act
        dto.Count = 0;

        // Assert
        Assert.Equal(0, dto.Count);
    }

    [Fact]
    public void BasePagedModelResponseDto_WithNegativeCount_ShouldAllowNegative()
    {
        // Arrange
        var dto = new BasePagedModelResponseDto();

        // Act
        dto.Count = -50;

        // Assert
        Assert.Equal(-50, dto.Count);
    }

    [Fact]
    public void BasePagedModelResponseDto_WithMaxCount_ShouldAllowMaxValue()
    {
        // Arrange
        var dto = new BasePagedModelResponseDto();

        // Act
        dto.Count = int.MaxValue;

        // Assert
        Assert.Equal(int.MaxValue, dto.Count);
    }

    [Fact]
    public void BasePagedModelResponseDto_WithNullCount_ShouldAllowNull()
    {
        // Arrange
        var dto = new BasePagedModelResponseDto();
        dto.Count = 100;

        // Act
        dto.Count = null;

        // Assert
        Assert.Null(dto.Count);
    }

    [Fact]
    public void BasePagedModelResponseDto_WithMultipleCountAssignments_ShouldUpdateValue()
    {
        // Arrange
        var dto = new BasePagedModelResponseDto();

        // Act
        dto.Count = 50;
        dto.Count = 200;
        dto.Count = 75;

        // Assert
        Assert.Equal(75, dto.Count);
    }

    #endregion

    #region Combined Property Tests

    [Fact]
    public void BasePagedModelResponseDto_WithAllProperties_ShouldSetAllProperties()
    {
        // Arrange
        var dto = new BasePagedModelResponseDto();
        var nextPage = new Uri("https://example.com/next");
        var previousPage = new Uri("https://example.com/previous");

        // Act
        dto.NextPage = nextPage;
        dto.PreviousPage = previousPage;
        dto.Count = 150;

        // Assert
        Assert.Equal(nextPage, dto.NextPage);
        Assert.Equal(previousPage, dto.PreviousPage);
        Assert.Equal(150, dto.Count);
    }

    [Fact]
    public void BasePagedModelResponseDto_WithAllPropertiesNull_ShouldSetAllPropertiesToNull()
    {
        // Arrange
        var dto = new BasePagedModelResponseDto();
        dto.NextPage = new Uri("https://example.com/next");
        dto.PreviousPage = new Uri("https://example.com/previous");
        dto.Count = 100;

        // Act
        dto.NextPage = null;
        dto.PreviousPage = null;
        dto.Count = null;

        // Assert
        Assert.Null(dto.NextPage);
        Assert.Null(dto.PreviousPage);
        Assert.Null(dto.Count);
    }

    [Fact]
    public void BasePagedModelResponseDto_WithSameNextAndPreviousPage_ShouldAllowSameUri()
    {
        // Arrange
        var dto = new BasePagedModelResponseDto();
        var samePage = new Uri("https://example.com/same");

        // Act
        dto.NextPage = samePage;
        dto.PreviousPage = samePage;

        // Assert
        Assert.Equal(samePage, dto.NextPage);
        Assert.Equal(samePage, dto.PreviousPage);
    }

    #endregion

    #region ToString Tests

    [Fact]
    public void BasePagedModelResponseDto_ToString_ShouldReturnExpectedFormat()
    {
        // Arrange
        var dto = new BasePagedModelResponseDto
        {
            NextPage = new Uri("https://example.com/next"),
            PreviousPage = new Uri("https://example.com/previous"),
            Count = 100
        };

        // Act
        var result = dto.ToString();

        // Assert
        Assert.NotNull(result);
        Assert.Contains("BasePagedModelResponseDto", result);
    }

    [Fact]
    public void BasePagedModelResponseDto_ToString_WithNullValues_ShouldReturnExpectedFormat()
    {
        // Arrange
        var dto = new BasePagedModelResponseDto();

        // Act
        var result = dto.ToString();

        // Assert
        Assert.NotNull(result);
        Assert.Contains("BasePagedModelResponseDto", result);
    }

    #endregion
}