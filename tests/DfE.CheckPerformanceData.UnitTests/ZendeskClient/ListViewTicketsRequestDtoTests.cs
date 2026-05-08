using System;
using Xunit;
using DfE.CheckPerformanceData.Application.ZendeskClient;

namespace DfE.CheckPerformanceData.UnitTests.ZendeskClient;

/// <summary>
/// Unit tests for ListViewTicketsRequestDto class.
/// </summary>
public class ListViewTicketsRequestDtoTests
{
    [Fact]
    public void ListViewTicketsRequestDto_DefaultConstructor_ShouldInitializeAllProperties()
    {
        // Arrange & Act
        var dto = new ListViewTicketsRequestDto();

        // Assert
        Assert.Null(dto.Page);
        Assert.Null(dto.PerPage);
        Assert.Null(dto.SortBy);
        Assert.Null(dto.SortOrder);
    }

    [Fact]
    public void ListViewTicketsRequestDto_WithAllProperties_ShouldSetAllProperties()
    {
        // Arrange
        var dto = new ListViewTicketsRequestDto();

        // Act
        dto.Page = 1;
        dto.PerPage = 50;
        dto.SortBy = "created_at";
        dto.SortOrder = "desc";

        // Assert
        Assert.Equal(1, dto.Page);
        Assert.Equal(50, dto.PerPage);
        Assert.Equal("created_at", dto.SortBy);
        Assert.Equal("desc", dto.SortOrder);
    }

    [Fact]
    public void ListViewTicketsRequestDto_WithNullPage_ShouldAllowNull()
    {
        // Arrange
        var dto = new ListViewTicketsRequestDto();

        // Act
        dto.Page = null;

        // Assert
        Assert.Null(dto.Page);
    }

    [Fact]
    public void ListViewTicketsRequestDto_WithNullPerPage_ShouldAllowNull()
    {
        // Arrange
        var dto = new ListViewTicketsRequestDto();

        // Act
        dto.PerPage = null;

        // Assert
        Assert.Null(dto.PerPage);
    }

    [Fact]
    public void ListViewTicketsRequestDto_WithNullSortBy_ShouldAllowNull()
    {
        // Arrange
        var dto = new ListViewTicketsRequestDto();

        // Act
        dto.SortBy = null;

        // Assert
        Assert.Null(dto.SortBy);
    }

    [Fact]
    public void ListViewTicketsRequestDto_WithNullSortOrder_ShouldAllowNull()
    {
        // Arrange
        var dto = new ListViewTicketsRequestDto();

        // Act
        dto.SortOrder = null;

        // Assert
        Assert.Null(dto.SortOrder);
    }

    [Fact]
    public void ListViewTicketsRequestDto_WithZeroPage_ShouldAllowZero()
    {
        // Arrange
        var dto = new ListViewTicketsRequestDto();

        // Act
        dto.Page = 0;

        // Assert
        Assert.Equal(0, dto.Page);
    }

    [Fact]
    public void ListViewTicketsRequestDto_WithZeroPerPage_ShouldAllowZero()
    {
        // Arrange
        var dto = new ListViewTicketsRequestDto();

        // Act
        dto.PerPage = 0;

        // Assert
        Assert.Equal(0, dto.PerPage);
    }

    [Fact]
    public void ListViewTicketsRequestDto_WithNegativePage_ShouldAllowNegative()
    {
        // Arrange
        var dto = new ListViewTicketsRequestDto();

        // Act
        dto.Page = -1;

        // Assert
        Assert.Equal(-1, dto.Page);
    }

    [Fact]
    public void ListViewTicketsRequestDto_WithNegativePerPage_ShouldAllowNegative()
    {
        // Arrange
        var dto = new ListViewTicketsRequestDto();

        // Act
        dto.PerPage = -1;

        // Assert
        Assert.Equal(-1, dto.PerPage);
    }

    [Fact]
    public void ListViewTicketsRequestDto_WithMaxPage_ShouldAllowMaxValue()
    {
        // Arrange
        var dto = new ListViewTicketsRequestDto();

        // Act
        dto.Page = int.MaxValue;

        // Assert
        Assert.Equal(int.MaxValue, dto.Page);
    }

    [Fact]
    public void ListViewTicketsRequestDto_WithMaxPerPage_ShouldAllowMaxValue()
    {
        // Arrange
        var dto = new ListViewTicketsRequestDto();

        // Act
        dto.PerPage = int.MaxValue;

        // Assert
        Assert.Equal(int.MaxValue, dto.PerPage);
    }

    [Fact]
    public void ListViewTicketsRequestDto_WithSpecialCharactersInSortBy_ShouldStoreSpecialChars()
    {
        // Arrange
        var dto = new ListViewTicketsRequestDto();

        // Act
        dto.SortBy = "created_at@2024";

        // Assert
        Assert.Equal("created_at@2024", dto.SortBy);
    }

    [Fact]
    public void ListViewTicketsRequestDto_WithUnicodeInSortOrder_ShouldStoreUnicode()
    {
        // Arrange
        var dto = new ListViewTicketsRequestDto();

        // Act
        dto.SortOrder = "descendente";

        // Assert
        Assert.Equal("descendente", dto.SortOrder);
    }

    [Fact]
    public void ListViewTicketsRequestDto_WithWhitespaceInSortBy_ShouldStoreWhitespace()
    {
        // Arrange
        var dto = new ListViewTicketsRequestDto();

        // Act
        dto.SortBy = "   created_at   ";

        // Assert
        Assert.Equal("   created_at   ", dto.SortBy);
    }

    [Fact]
    public void ListViewTicketsRequestDto_WithEmptyStringInSortBy_ShouldSetEmptyString()
    {
        // Arrange
        var dto = new ListViewTicketsRequestDto();

        // Act
        dto.SortBy = string.Empty;

        // Assert
        Assert.Equal(string.Empty, dto.SortBy);
    }

    [Fact]
    public void ListViewTicketsRequestDto_WithEmptyStringInSortOrder_ShouldSetEmptyString()
    {
        // Arrange
        var dto = new ListViewTicketsRequestDto();

        // Act
        dto.SortOrder = string.Empty;

        // Assert
        Assert.Equal(string.Empty, dto.SortOrder);
    }

    [Fact]
    public void ListViewTicketsRequestDto_WithTrueActive_ShouldSetTrue()
    {
        // Arrange
        var dto = new ListViewTicketsRequestDto();

        // Act
        dto.SortOrder = "asc";

        // Assert
        Assert.Equal("asc", dto.SortOrder);
    }

    [Fact]
    public void ListViewTicketsRequestDto_WithFalseActive_ShouldSetFalse()
    {
        // Arrange
        var dto = new ListViewTicketsRequestDto();

        // Act
        dto.SortOrder = "desc";

        // Assert
        Assert.Equal("desc", dto.SortOrder);
    }

    [Fact]
    public void ListViewTicketsRequestDto_WithMultipleAssignments_ShouldUpdateValues()
    {
        // Arrange
        var dto = new ListViewTicketsRequestDto();

        // Act
        dto.Page = 2;
        dto.PerPage = 25;
        dto.SortBy = "updated_at";
        dto.SortOrder = "asc";

        // Assert
        Assert.Equal(2, dto.Page);
        Assert.Equal(25, dto.PerPage);
        Assert.Equal("updated_at", dto.SortBy);
        Assert.Equal("asc", dto.SortOrder);
    }

    [Fact]
    public void ListViewTicketsRequestDto_WithHyphenPrefixInSortBy_ShouldStoreHyphen()
    {
        // Arrange
        var dto = new ListViewTicketsRequestDto();

        // Act
        dto.SortBy = "-created_at";

        // Assert
        Assert.Equal("-created_at", dto.SortBy);
    }

    [Fact]
    public void ListViewTicketsRequestDto_WithDifferentSortOrderValues_ShouldStoreValues()
    {
        // Arrange
        var dto = new ListViewTicketsRequestDto();

        // Act
        dto.SortOrder = "asc";
        dto.SortOrder = "desc";

        // Assert
        Assert.Equal("desc", dto.SortOrder);
    }

    [Fact]
    public void ListViewTicketsRequestDto_WithZeroPageAndPerPage_ShouldAllowZero()
    {
        // Arrange
        var dto = new ListViewTicketsRequestDto();

        // Act
        dto.Page = 0;
        dto.PerPage = 0;

        // Assert
        Assert.Equal(0, dto.Page);
        Assert.Equal(0, dto.PerPage);
    }

    [Fact]
    public void ListViewTicketsRequestDto_WithLargePageAndPerPage_ShouldAllowLargeValues()
    {
        // Arrange
        var dto = new ListViewTicketsRequestDto();

        // Act
        dto.Page = 9999;
        dto.PerPage = 1000;

        // Assert
        Assert.Equal(9999, dto.Page);
        Assert.Equal(1000, dto.PerPage);
    }

    [Fact]
    public void ListViewTicketsRequestDto_WithNullProperties_ShouldInitializeNull()
    {
        // Arrange
        var dto = new ListViewTicketsRequestDto();

        // Assert
        Assert.Null(dto.Page);
        Assert.Null(dto.PerPage);
        Assert.Null(dto.SortBy);
        Assert.Null(dto.SortOrder);
    }

    [Fact]
    public void ListViewTicketsRequestDto_WithAllPropertiesSet_ShouldHaveAllValues()
    {
        // Arrange
        var dto = new ListViewTicketsRequestDto();

        // Act
        dto.Page = 5;
        dto.PerPage = 100;
        dto.SortBy = "position";
        dto.SortOrder = "desc";

        // Assert
        Assert.Equal(5, dto.Page);
        Assert.Equal(100, dto.PerPage);
        Assert.Equal("position", dto.SortBy);
        Assert.Equal("desc", dto.SortOrder);
    }

    [Fact]
    public void ListViewTicketsRequestDto_WithOnlyPageAndPerPage_ShouldSetOnlyThose()
    {
        // Arrange
        var dto = new ListViewTicketsRequestDto();

        // Act
        dto.Page = 3;
        dto.PerPage = 75;

        // Assert
        Assert.Equal(3, dto.Page);
        Assert.Equal(75, dto.PerPage);
        Assert.Null(dto.SortBy);
        Assert.Null(dto.SortOrder);
    }

    [Fact]
    public void ListViewTicketsRequestDto_WithOnlySortByAndSortOrder_ShouldSetOnlyThose()
    {
        // Arrange
        var dto = new ListViewTicketsRequestDto();

        // Act
        dto.SortBy = "alphabetical";
        dto.SortOrder = "asc";

        // Assert
        Assert.Null(dto.Page);
        Assert.Null(dto.PerPage);
        Assert.Equal("alphabetical", dto.SortBy);
        Assert.Equal("asc", dto.SortOrder);
    }
}