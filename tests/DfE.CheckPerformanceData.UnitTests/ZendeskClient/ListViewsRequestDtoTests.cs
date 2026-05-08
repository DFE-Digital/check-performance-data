using System;
using Xunit;
using DfE.CheckPerformanceData.Application.ZendeskClient;

namespace DfE.CheckPerformanceData.UnitTests.ZendeskClient;

/// <summary>
/// Unit tests for ListViewsRequestDto class.
/// </summary>
public class ListViewsRequestDtoTests
{
    [Fact]
    public void ListViewsRequestDto_DefaultConstructor_ShouldInitializeAllProperties()
    {
        // Arrange & Act
        var dto = new ListViewsRequestDto();

        // Assert
        Assert.Null(dto.Access);
        Assert.Null(dto.Active);
        Assert.Null(dto.GroupId);
        Assert.Null(dto.Sort);
        Assert.Null(dto.SortBy);
        Assert.Null(dto.SortOrder);
        Assert.Null(dto.Page);
        Assert.Null(dto.PerPage);
    }

    [Fact]
    public void ListViewsRequestDto_WithAllProperties_ShouldSetAllProperties()
    {
        // Arrange
        var dto = new ListViewsRequestDto();

        // Act
        dto.Access = "shared";
        dto.Active = true;
        dto.GroupId = 123;
        dto.Sort = "created_at";
        dto.SortBy = "alphabetical";
        dto.SortOrder = "asc";
        dto.Page = 1;
        dto.PerPage = 50;

        // Assert
        Assert.Equal("shared", dto.Access);
        Assert.True(dto.Active);
        Assert.Equal(123, dto.GroupId);
        Assert.Equal("created_at", dto.Sort);
        Assert.Equal("alphabetical", dto.SortBy);
        Assert.Equal("asc", dto.SortOrder);
        Assert.Equal(1, dto.Page);
        Assert.Equal(50, dto.PerPage);
    }

    [Fact]
    public void ListViewsRequestDto_WithNullAccess_ShouldAllowNull()
    {
        // Arrange
        var dto = new ListViewsRequestDto();

        // Act
        dto.Access = null;

        // Assert
        Assert.Null(dto.Access);
    }

    [Fact]
    public void ListViewsRequestDto_WithNullActive_ShouldAllowNull()
    {
        // Arrange
        var dto = new ListViewsRequestDto();

        // Act
        dto.Active = null;

        // Assert
        Assert.Null(dto.Active);
    }

    [Fact]
    public void ListViewsRequestDto_WithNullGroupId_ShouldAllowNull()
    {
        // Arrange
        var dto = new ListViewsRequestDto();

        // Act
        dto.GroupId = null;

        // Assert
        Assert.Null(dto.GroupId);
    }

    [Fact]
    public void ListViewsRequestDto_WithNullSort_ShouldAllowNull()
    {
        // Arrange
        var dto = new ListViewsRequestDto();

        // Act
        dto.Sort = null;

        // Assert
        Assert.Null(dto.Sort);
    }

    [Fact]
    public void ListViewsRequestDto_WithNullSortBy_ShouldAllowNull()
    {
        // Arrange
        var dto = new ListViewsRequestDto();

        // Act
        dto.SortBy = null;

        // Assert
        Assert.Null(dto.SortBy);
    }

    [Fact]
    public void ListViewsRequestDto_WithNullSortOrder_ShouldAllowNull()
    {
        // Arrange
        var dto = new ListViewsRequestDto();

        // Act
        dto.SortOrder = null;

        // Assert
        Assert.Null(dto.SortOrder);
    }

    [Fact]
    public void ListViewsRequestDto_WithNullPage_ShouldAllowNull()
    {
        // Arrange
        var dto = new ListViewsRequestDto();

        // Act
        dto.Page = null;

        // Assert
        Assert.Null(dto.Page);
    }

    [Fact]
    public void ListViewsRequestDto_WithNullPerPage_ShouldAllowNull()
    {
        // Arrange
        var dto = new ListViewsRequestDto();

        // Act
        dto.PerPage = null;

        // Assert
        Assert.Null(dto.PerPage);
    }

    [Fact]
    public void ListViewsRequestDto_WithZeroGroupId_ShouldAllowZero()
    {
        // Arrange
        var dto = new ListViewsRequestDto();

        // Act
        dto.GroupId = 0;

        // Assert
        Assert.Equal(0, dto.GroupId);
    }

    [Fact]
    public void ListViewsRequestDto_WithZeroPage_ShouldAllowZero()
    {
        // Arrange
        var dto = new ListViewsRequestDto();

        // Act
        dto.Page = 0;

        // Assert
        Assert.Equal(0, dto.Page);
    }

    [Fact]
    public void ListViewsRequestDto_WithZeroPerPage_ShouldAllowZero()
    {
        // Arrange
        var dto = new ListViewsRequestDto();

        // Act
        dto.PerPage = 0;

        // Assert
        Assert.Equal(0, dto.PerPage);
    }

    [Fact]
    public void ListViewsRequestDto_WithNegativeGroupId_ShouldAllowNegative()
    {
        // Arrange
        var dto = new ListViewsRequestDto();

        // Act
        dto.GroupId = -1;

        // Assert
        Assert.Equal(-1, dto.GroupId);
    }

    [Fact]
    public void ListViewsRequestDto_WithNegativePage_ShouldAllowNegative()
    {
        // Arrange
        var dto = new ListViewsRequestDto();

        // Act
        dto.Page = -1;

        // Assert
        Assert.Equal(-1, dto.Page);
    }

    [Fact]
    public void ListViewsRequestDto_WithNegativePerPage_ShouldAllowNegative()
    {
        // Arrange
        var dto = new ListViewsRequestDto();

        // Act
        dto.PerPage = -1;

        // Assert
        Assert.Equal(-1, dto.PerPage);
    }

    [Fact]
    public void ListViewsRequestDto_WithMaxGroupId_ShouldAllowMaxValue()
    {
        // Arrange
        var dto = new ListViewsRequestDto();

        // Act
        dto.GroupId = int.MaxValue;

        // Assert
        Assert.Equal(int.MaxValue, dto.GroupId);
    }

    [Fact]
    public void ListViewsRequestDto_WithMaxPage_ShouldAllowMaxValue()
    {
        // Arrange
        var dto = new ListViewsRequestDto();

        // Act
        dto.Page = int.MaxValue;

        // Assert
        Assert.Equal(int.MaxValue, dto.Page);
    }

    [Fact]
    public void ListViewsRequestDto_WithMaxPerPage_ShouldAllowMaxValue()
    {
        // Arrange
        var dto = new ListViewsRequestDto();

        // Act
        dto.PerPage = int.MaxValue;

        // Assert
        Assert.Equal(int.MaxValue, dto.PerPage);
    }

    [Fact]
    public void ListViewsRequestDto_WithSpecialCharactersInAccess_ShouldStoreSpecialChars()
    {
        // Arrange
        var dto = new ListViewsRequestDto();

        // Act
        dto.Access = "shared@account#1";

        // Assert
        Assert.Equal("shared@account#1", dto.Access);
    }

    [Fact]
    public void ListViewsRequestDto_WithUnicodeInSortBy_ShouldStoreUnicode()
    {
        // Arrange
        var dto = new ListViewsRequestDto();

        // Act
        dto.SortBy = "alphabetical排序";

        // Assert
        Assert.Equal("alphabetical排序", dto.SortBy);
    }

    [Fact]
    public void ListViewsRequestDto_WithWhitespaceInAccess_ShouldStoreWhitespace()
    {
        // Arrange
        var dto = new ListViewsRequestDto();

        // Act
        dto.Access = "   shared   ";

        // Assert
        Assert.Equal("   shared   ", dto.Access);
    }

    [Fact]
    public void ListViewsRequestDto_WithEmptyStringInAccess_ShouldSetEmptyString()
    {
        // Arrange
        var dto = new ListViewsRequestDto();

        // Act
        dto.Access = string.Empty;

        // Assert
        Assert.Equal(string.Empty, dto.Access);
    }

    [Fact]
    public void ListViewsRequestDto_WithEmptyStringInSort_ShouldSetEmptyString()
    {
        // Arrange
        var dto = new ListViewsRequestDto();

        // Act
        dto.Sort = string.Empty;

        // Assert
        Assert.Equal(string.Empty, dto.Sort);
    }

    [Fact]
    public void ListViewsRequestDto_WithEmptyStringInSortBy_ShouldSetEmptyString()
    {
        // Arrange
        var dto = new ListViewsRequestDto();

        // Act
        dto.SortBy = string.Empty;

        // Assert
        Assert.Equal(string.Empty, dto.SortBy);
    }

    [Fact]
    public void ListViewsRequestDto_WithEmptyStringInSortOrder_ShouldSetEmptyString()
    {
        // Arrange
        var dto = new ListViewsRequestDto();

        // Act
        dto.SortOrder = string.Empty;

        // Assert
        Assert.Equal(string.Empty, dto.SortOrder);
    }

    [Fact]
    public void ListViewsRequestDto_WithTrueActive_ShouldSetTrue()
    {
        // Arrange
        var dto = new ListViewsRequestDto();

        // Act
        dto.Active = true;

        // Assert
        Assert.True(dto.Active);
    }

    [Fact]
    public void ListViewsRequestDto_WithFalseActive_ShouldSetFalse()
    {
        // Arrange
        var dto = new ListViewsRequestDto();

        // Act
        dto.Active = false;

        // Assert
        Assert.False(dto.Active);
    }

    [Fact]
    public void ListViewsRequestDto_WithMultipleAssignments_ShouldUpdateValues()
    {
        // Arrange
        var dto = new ListViewsRequestDto();

        // Act
        dto.Access = "personal";
        dto.Active = false;
        dto.GroupId = 100;
        dto.Sort = "updated_at";
        dto.SortBy = "created_at";
        dto.SortOrder = "desc";
        dto.Page = 5;
        dto.PerPage = 25;

        // Assert
        Assert.Equal("personal", dto.Access);
        Assert.False(dto.Active);
        Assert.Equal(100, dto.GroupId);
        Assert.Equal("updated_at", dto.Sort);
        Assert.Equal("created_at", dto.SortBy);
        Assert.Equal("desc", dto.SortOrder);
        Assert.Equal(5, dto.Page);
        Assert.Equal(25, dto.PerPage);
    }

    [Fact]
    public void ListViewsRequestDto_WithHyphenPrefixInSort_ShouldStoreHyphen()
    {
        // Arrange
        var dto = new ListViewsRequestDto();

        // Act
        dto.Sort = "-created_at";

        // Assert
        Assert.Equal("-created_at", dto.Sort);
    }

    [Fact]
    public void ListViewsRequestDto_WithDifferentSortOrderValues_ShouldStoreValues()
    {
        // Arrange
        var dto = new ListViewsRequestDto();

        // Act
        dto.SortOrder = "asc";
        dto.SortOrder = "desc";

        // Assert
        Assert.Equal("desc", dto.SortOrder);
    }

    [Fact]
    public void ListViewsRequestDto_WithZeroPageAndPerPage_ShouldAllowZero()
    {
        // Arrange
        var dto = new ListViewsRequestDto();

        // Act
        dto.Page = 0;
        dto.PerPage = 0;

        // Assert
        Assert.Equal(0, dto.Page);
        Assert.Equal(0, dto.PerPage);
    }

    [Fact]
    public void ListViewsRequestDto_WithLargePageAndPerPage_ShouldAllowLargeValues()
    {
        // Arrange
        var dto = new ListViewsRequestDto();

        // Act
        dto.Page = 9999;
        dto.PerPage = 1000;

        // Assert
        Assert.Equal(9999, dto.Page);
        Assert.Equal(1000, dto.PerPage);
    }

    [Fact]
    public void ListViewsRequestDto_WithNullProperties_ShouldInitializeNull()
    {
        // Arrange
        var dto = new ListViewsRequestDto();

        // Assert
        Assert.Null(dto.Access);
        Assert.Null(dto.Active);
        Assert.Null(dto.GroupId);
        Assert.Null(dto.Sort);
        Assert.Null(dto.SortBy);
        Assert.Null(dto.SortOrder);
        Assert.Null(dto.Page);
        Assert.Null(dto.PerPage);
    }

    [Fact]
    public void ListViewsRequestDto_WithAllPropertiesSet_ShouldHaveAllValues()
    {
        // Arrange
        var dto = new ListViewsRequestDto();

        // Act
        dto.Access = "account";
        dto.Active = true;
        dto.GroupId = 42;
        dto.Sort = "position";
        dto.SortBy = "position";
        dto.SortOrder = "asc";
        dto.Page = 2;
        dto.PerPage = 100;

        // Assert
        Assert.Equal("account", dto.Access);
        Assert.True(dto.Active);
        Assert.Equal(42, dto.GroupId);
        Assert.Equal("position", dto.Sort);
        Assert.Equal("position", dto.SortBy);
        Assert.Equal("asc", dto.SortOrder);
        Assert.Equal(2, dto.Page);
        Assert.Equal(100, dto.PerPage);
    }
}