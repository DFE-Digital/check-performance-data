using System;
using System.Collections.Generic;
using Xunit;
using DfE.CheckPerformanceData.Application.ZendeskClient;

namespace DfE.CheckPerformanceData.UnitTests.ZendeskClient;

/// <summary>
/// Unit tests for RequestQueryExtensions class.
/// </summary>
public class RequestQueryExtensionsDtoTests
{
    #region ToQueryDictionary for ListViewsRequestDto Tests

    [Fact]
    public void ToQueryDictionary_WithListViewsRequestDto_ShouldReturnEmptyDictionaryWhenAllNull()
    {
        // Arrange
        var request = new ListViewsRequestDto();

        // Act
        var result = request.ToQueryDictionary();

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void ToQueryDictionary_WithListViewsRequestDto_ShouldIncludeAllNonNullProperties()
    {
        // Arrange
        var request = new ListViewsRequestDto
        {
            Access = "shared",
            Active = true,
            GroupId = 123,
            Sort = "created_at",
            SortBy = "alphabetical",
            SortOrder = "asc",
            Page = 1,
            PerPage = 50
        };

        // Act
        var result = request.ToQueryDictionary();

        // Assert
        Assert.Equal("shared", result["access"]);
        Assert.True((bool)result["active"]);
        Assert.Equal(123, result["group_id"]);
        Assert.Equal("created_at", result["sort"]);
        Assert.Equal("alphabetical", result["sort_by"]);
        Assert.Equal("asc", result["sort_order"]);
        Assert.Equal(1, result["page"]);
        Assert.Equal(50, result["per_page"]);
    }

    [Fact]
    public void ToQueryDictionary_WithListViewsRequestDto_ShouldIncludeNonNullProperties()
    {
        // Arrange
        var request = new ListViewsRequestDto
        {
            Access = "shared",
            Active = true,
            GroupId = 123,
            Sort = "created_at",
            SortBy = "alphabetical",
            SortOrder = "asc",
            Page = 1,
            PerPage = 50
        };

        // Act
        var result = request.ToQueryDictionary();

        // Assert
        Assert.Contains("access", result.Keys);
        Assert.Contains("active", result.Keys);
        Assert.Contains("group_id", result.Keys);
        Assert.Contains("sort", result.Keys);
        Assert.Contains("sort_by", result.Keys);
        Assert.Contains("sort_order", result.Keys);
        Assert.Contains("page", result.Keys);
        Assert.Contains("per_page", result.Keys);
    }

    [Fact]
    public void ToQueryDictionary_WithListViewsRequestDto_ShouldHandleEmptyStringValues()
    {
        // Arrange
        var request = new ListViewsRequestDto
        {
            Access = string.Empty,
            Active = true,
            GroupId = 123,
            Sort = string.Empty,
            SortBy = string.Empty,
            SortOrder = string.Empty,
            Page = 1,
            PerPage = 50
        };

        // Act
        var result = request.ToQueryDictionary();

        // Assert
        Assert.DoesNotContain("access", result.Keys);
        Assert.DoesNotContain("sort", result.Keys);
        Assert.DoesNotContain("sort_by", result.Keys);
        Assert.DoesNotContain("sort_order", result.Keys);
        Assert.Contains("active", result.Keys);
        Assert.Contains("group_id", result.Keys);
        Assert.Contains("page", result.Keys);
        Assert.Contains("per_page", result.Keys);
    }

    [Fact]
    public void ToQueryDictionary_WithListViewsRequestDto_ShouldHandleZeroIntValues()
    {
        // Arrange
        var request = new ListViewsRequestDto
        {
            Access = "shared",
            Active = true,
            GroupId = 0,
            Sort = "created_at",
            SortBy = "alphabetical",
            SortOrder = "asc",
            Page = 0,
            PerPage = 0
        };

        // Act
        var result = request.ToQueryDictionary();

        // Assert
        Assert.Contains("group_id", result.Keys);
        Assert.Contains("page", result.Keys);
        Assert.Contains("per_page", result.Keys);
        Assert.Equal(0, result["group_id"]);
        Assert.Equal(0, result["page"]);
        Assert.Equal(0, result["per_page"]);
    }

    [Fact]
    public void ToQueryDictionary_WithListViewsRequestDto_ShouldHandleFalseBooleanValue()
    {
        // Arrange
        var request = new ListViewsRequestDto
        {
            Access = "shared",
            Active = false,
            GroupId = 123,
            Sort = "created_at",
            SortBy = "alphabetical",
            SortOrder = "asc",
            Page = 1,
            PerPage = 50
        };

        // Act
        var result = request.ToQueryDictionary();

        // Assert
        Assert.Contains("active", result.Keys);
        Assert.False((bool)result["active"]);
    }

    [Fact]
    public void ToQueryDictionary_WithListViewsRequestDto_ShouldHandleNullIntValues()
    {
        // Arrange
        var request = new ListViewsRequestDto
        {
            Access = "shared",
            Active = true,
            GroupId = null,
            Sort = "created_at",
            SortBy = "alphabetical",
            SortOrder = "asc",
            Page = null,
            PerPage = null
        };

        // Act
        var result = request.ToQueryDictionary();

        // Assert
        Assert.DoesNotContain("group_id", result.Keys);
        Assert.DoesNotContain("page", result.Keys);
        Assert.DoesNotContain("per_page", result.Keys);
    }

    [Fact]
    public void ToQueryDictionary_WithListViewsRequestDto_ShouldHandleSpecialCharactersInStrings()
    {
        // Arrange
        var request = new ListViewsRequestDto
        {
            Access = "shared@account#1",
            Active = true,
            GroupId = 123,
            Sort = "created_at@2024",
            SortBy = "alphabetical排序",
            SortOrder = "asc",
            Page = 1,
            PerPage = 50
        };

        // Act
        var result = request.ToQueryDictionary();

        // Assert
        Assert.Equal("shared@account#1", result["access"]);
        Assert.Equal("created_at@2024", result["sort"]);
        Assert.Equal("alphabetical排序", result["sort_by"]);
    }

    [Fact]
    public void ToQueryDictionary_WithListViewsRequestDto_ShouldHandleWhitespaceInStrings()
    {
        // Arrange
        var request = new ListViewsRequestDto
        {
            Access = "   shared   ",
            Active = true,
            GroupId = 123,
            Sort = "   created_at   ",
            SortBy = "   alphabetical   ",
            SortOrder = "   asc   ",
            Page = 1,
            PerPage = 50
        };

        // Act
        var result = request.ToQueryDictionary();

        // Assert
        Assert.Equal("   shared   ", result["access"]);
        Assert.Equal("   created_at   ", result["sort"]);
        Assert.Equal("   alphabetical   ", result["sort_by"]);
        Assert.Equal("   asc   ", result["sort_order"]);
    }

    [Fact]
    public void ToQueryDictionary_WithListViewsRequestDto_ShouldHandleHyphenPrefixInSort()
    {
        // Arrange
        var request = new ListViewsRequestDto
        {
            Access = "shared",
            Active = true,
            GroupId = 123,
            Sort = "-created_at",
            SortBy = "alphabetical",
            SortOrder = "asc",
            Page = 1,
            PerPage = 50
        };

        // Act
        var result = request.ToQueryDictionary();

        // Assert
        Assert.Equal("-created_at", result["sort"]);
    }

    [Fact]
    public void ToQueryDictionary_WithListViewsRequestDto_ShouldHandleLargeIntValues()
    {
        // Arrange
        var request = new ListViewsRequestDto
        {
            Access = "shared",
            Active = true,
            GroupId = int.MaxValue,
            Sort = "created_at",
            SortBy = "alphabetical",
            SortOrder = "asc",
            Page = int.MaxValue,
            PerPage = int.MaxValue
        };

        // Act
        var result = request.ToQueryDictionary();

        // Assert
        Assert.Equal(int.MaxValue, result["group_id"]);
        Assert.Equal(int.MaxValue, result["page"]);
        Assert.Equal(int.MaxValue, result["per_page"]);
    }

    [Fact]
    public void ToQueryDictionary_WithListViewsRequestDto_ShouldHandleNegativeIntValues()
    {
        // Arrange
        var request = new ListViewsRequestDto
        {
            Access = "shared",
            Active = true,
            GroupId = -1,
            Sort = "created_at",
            SortBy = "alphabetical",
            SortOrder = "asc",
            Page = -1,
            PerPage = -1
        };

        // Act
        var result = request.ToQueryDictionary();

        // Assert
        Assert.Equal(-1, result["group_id"]);
        Assert.Equal(-1, result["page"]);
        Assert.Equal(-1, result["per_page"]);
    }

    [Fact]
    public void ToQueryDictionary_WithListViewsRequestDto_ShouldHandleMultipleAssignments()
    {
        // Arrange
        var request = new ListViewsRequestDto();

        // Act
        request.Access = "personal";
        request.Active = false;
        request.GroupId = 100;
        request.Sort = "updated_at";
        request.SortBy = "created_at";
        request.SortOrder = "desc";
        request.Page = 5;
        request.PerPage = 25;

        var result = request.ToQueryDictionary();

        // Assert
        Assert.Equal("personal", result["access"]);
        Assert.False((bool)result["active"]);
        Assert.Equal(100, result["group_id"]);
        Assert.Equal("updated_at", result["sort"]);
        Assert.Equal("created_at", result["sort_by"]);
        Assert.Equal("desc", result["sort_order"]);
        Assert.Equal(5, result["page"]);
        Assert.Equal(25, result["per_page"]);
    }

    #endregion

    #region ToQueryDictionary for ListViewTicketsRequestDto Tests

    [Fact]
    public void ToQueryDictionary_WithListViewTicketsRequestDto_ShouldReturnEmptyDictionaryWhenAllNull()
    {
        // Arrange
        var request = new ListViewTicketsRequestDto();

        // Act
        var result = request.ToQueryDictionary();

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void ToQueryDictionary_WithListViewTicketsRequestDto_ShouldIncludeAllNonNullProperties()
    {
        // Arrange
        var request = new ListViewTicketsRequestDto
        {
            Page = 1,
            PerPage = 50,
            SortBy = "created_at",
            SortOrder = "desc"
        };

        // Act
        var result = request.ToQueryDictionary();

        // Assert
        Assert.Equal(1, result["page"]);
        Assert.Equal(50, result["per_page"]);
        Assert.Equal("created_at", result["sort_by"]);
        Assert.Equal("desc", result["sort_order"]);
    }

    [Fact]
    public void ToQueryDictionary_WithListViewTicketsRequestDto_ShouldExcludeNullProperties()
    {
        // Arrange
        var request = new ListViewTicketsRequestDto();

        // Act
        var result = request.ToQueryDictionary();

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void ToQueryDictionary_WithListViewTicketsRequestDto_ShouldHandleEmptyStringValues()
    {
        // Arrange
        var request = new ListViewTicketsRequestDto
        {
            Page = 1,
            PerPage = 50,
            SortBy = string.Empty,
            SortOrder = string.Empty
        };

        // Act
        var result = request.ToQueryDictionary();

        // Assert
        Assert.DoesNotContain("sort_by", result.Keys);
        Assert.DoesNotContain("sort_order", result.Keys);
        Assert.Contains("page", result.Keys);
        Assert.Contains("per_page", result.Keys);
    }

    [Fact]
    public void ToQueryDictionary_WithListViewTicketsRequestDto_ShouldHandleZeroIntValues()
    {
        // Arrange
        var request = new ListViewTicketsRequestDto
        {
            Page = 0,
            PerPage = 0,
            SortBy = "created_at",
            SortOrder = "desc"
        };

        // Act
        var result = request.ToQueryDictionary();

        // Assert
        Assert.Contains("page", result.Keys);
        Assert.Contains("per_page", result.Keys);
        Assert.Equal(0, result["page"]);
        Assert.Equal(0, result["per_page"]);
    }

    [Fact]
    public void ToQueryDictionary_WithListViewTicketsRequestDto_ShouldHandleNullIntValues()
    {
        // Arrange
        var request = new ListViewTicketsRequestDto
        {
            Page = null,
            PerPage = null,
            SortBy = "created_at",
            SortOrder = "desc"
        };

        // Act
        var result = request.ToQueryDictionary();

        // Assert
        Assert.DoesNotContain("page", result.Keys);
        Assert.DoesNotContain("per_page", result.Keys);
    }

    [Fact]
    public void ToQueryDictionary_WithListViewTicketsRequestDto_ShouldHandleSpecialCharactersInStrings()
    {
        // Arrange
        var request = new ListViewTicketsRequestDto
        {
            Page = 1,
            PerPage = 50,
            SortBy = "created_at@2024",
            SortOrder = "descendente"
        };

        // Act
        var result = request.ToQueryDictionary();

        // Assert
        Assert.Equal("created_at@2024", result["sort_by"]);
        Assert.Equal("descendente", result["sort_order"]);
    }

    [Fact]
    public void ToQueryDictionary_WithListViewTicketsRequestDto_ShouldHandleWhitespaceInStrings()
    {
        // Arrange
        var request = new ListViewTicketsRequestDto
        {
            Page = 1,
            PerPage = 50,
            SortBy = "   created_at   ",
            SortOrder = "   desc   "
        };

        // Act
        var result = request.ToQueryDictionary();

        // Assert
        Assert.Equal("   created_at   ", result["sort_by"]);
        Assert.Equal("   desc   ", result["sort_order"]);
    }

    [Fact]
    public void ToQueryDictionary_WithListViewTicketsRequestDto_ShouldHandleHyphenPrefixInSortBy()
    {
        // Arrange
        var request = new ListViewTicketsRequestDto
        {
            Page = 1,
            PerPage = 50,
            SortBy = "-created_at",
            SortOrder = "asc"
        };

        // Act
        var result = request.ToQueryDictionary();

        // Assert
        Assert.Equal("-created_at", result["sort_by"]);
    }

    [Fact]
    public void ToQueryDictionary_WithListViewTicketsRequestDto_ShouldHandleLargeIntValues()
    {
        // Arrange
        var request = new ListViewTicketsRequestDto
        {
            Page = int.MaxValue,
            PerPage = int.MaxValue,
            SortBy = "created_at",
            SortOrder = "desc"
        };

        // Act
        var result = request.ToQueryDictionary();

        // Assert
        Assert.Equal(int.MaxValue, result["page"]);
        Assert.Equal(int.MaxValue, result["per_page"]);
    }

    [Fact]
    public void ToQueryDictionary_WithListViewTicketsRequestDto_ShouldHandleNegativeIntValues()
    {
        // Arrange
        var request = new ListViewTicketsRequestDto
        {
            Page = -1,
            PerPage = -1,
            SortBy = "created_at",
            SortOrder = "desc"
        };

        // Act
        var result = request.ToQueryDictionary();

        // Assert
        Assert.Equal(-1, result["page"]);
        Assert.Equal(-1, result["per_page"]);
    }

    [Fact]
    public void ToQueryDictionary_WithListViewTicketsRequestDto_ShouldHandleMultipleAssignments()
    {
        // Arrange
        var request = new ListViewTicketsRequestDto();

        // Act
        request.Page = 2;
        request.PerPage = 25;
        request.SortBy = "updated_at";
        request.SortOrder = "asc";

        var result = request.ToQueryDictionary();

        // Assert
        Assert.Equal(2, result["page"]);
        Assert.Equal(25, result["per_page"]);
        Assert.Equal("updated_at", result["sort_by"]);
        Assert.Equal("asc", result["sort_order"]);
    }

    [Fact]
    public void ToQueryDictionary_WithListViewTicketsRequestDto_ShouldHandleOnlyPageAndPerPage()
    {
        // Arrange
        var request = new ListViewTicketsRequestDto
        {
            Page = 3,
            PerPage = 75
        };

        // Act
        var result = request.ToQueryDictionary();

        // Assert
        Assert.Equal(3, result["page"]);
        Assert.Equal(75, result["per_page"]);
        Assert.DoesNotContain("sort_by", result.Keys);
        Assert.DoesNotContain("sort_order", result.Keys);
    }

    [Fact]
    public void ToQueryDictionary_WithListViewTicketsRequestDto_ShouldHandleOnlySortByAndSortOrder()
    {
        // Arrange
        var request = new ListViewTicketsRequestDto
        {
            SortBy = "alphabetical",
            SortOrder = "asc"
        };

        // Act
        var result = request.ToQueryDictionary();

        // Assert
        Assert.DoesNotContain("page", result.Keys);
        Assert.DoesNotContain("per_page", result.Keys);
        Assert.Equal("alphabetical", result["sort_by"]);
        Assert.Equal("asc", result["sort_order"]);
    }

    #endregion

    #region Edge Cases and Validation Tests

    [Fact]
    public void ToQueryDictionary_WithListViewsRequestDto_ShouldHandleNullAccessAndActive()
    {
        // Arrange
        var request = new ListViewsRequestDto
        {
            Access = null,
            Active = null,
            GroupId = 123,
            Sort = "created_at",
            SortBy = "alphabetical",
            SortOrder = "asc",
            Page = 1,
            PerPage = 50
        };

        // Act
        var result = request.ToQueryDictionary();

        // Assert
        Assert.DoesNotContain("access", result.Keys);
        Assert.DoesNotContain("active", result.Keys);
    }

    [Fact]
    public void ToQueryDictionary_WithListViewTicketsRequestDto_ShouldHandleNullPageAndPerPage()
    {
        // Arrange
        var request = new ListViewTicketsRequestDto
        {
            Page = null,
            PerPage = null,
            SortBy = "created_at",
            SortOrder = "desc"
        };

        // Act
        var result = request.ToQueryDictionary();

        // Assert
        Assert.DoesNotContain("page", result.Keys);
        Assert.DoesNotContain("per_page", result.Keys);
    }

    [Fact]
    public void ToQueryDictionary_WithListViewsRequestDto_ShouldHandleAllNullProperties()
    {
        // Arrange
        var request = new ListViewsRequestDto();

        // Act
        var result = request.ToQueryDictionary();

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void ToQueryDictionary_WithListViewTicketsRequestDto_ShouldHandleAllNullProperties()
    {
        // Arrange
        var request = new ListViewTicketsRequestDto();

        // Act
        var result = request.ToQueryDictionary();

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void ToQueryDictionary_WithListViewsRequestDto_ShouldHandleDifferentSortOrderValues()
    {
        // Arrange
        var request = new ListViewsRequestDto
        {
            SortOrder = "asc"
        };

        // Act
        var result = request.ToQueryDictionary();

        // Assert
        Assert.Equal("asc", result["sort_order"]);
    }

    [Fact]
    public void ToQueryDictionary_WithListViewTicketsRequestDto_ShouldHandleDifferentSortOrderValues()
    {
        // Arrange
        var request = new ListViewTicketsRequestDto
        {
            SortOrder = "desc"
        };

        // Act
        var result = request.ToQueryDictionary();

        // Assert
        Assert.Equal("desc", result["sort_order"]);
    }

    #endregion
}