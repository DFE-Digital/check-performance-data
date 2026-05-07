using Xunit;
using DfE.CheckPerformanceData.Application.ZendeskClient;

namespace DfE.CheckPerformanceData.UnitTests.ZendeskClient;

/// <summary>
/// Unit tests for ZendeskApiException class.
/// </summary>
public sealed class ZendeskApiExceptionTests
{
    #region Basic Constructor Tests

    [Fact]
    public void Constructor_WithMessage_ShouldSetMessage()
    {
        // Arrange
        const string message = "Test exception message";

        // Act
        var exception = new ZendeskApiException(message);

        // Assert
        Assert.Equal(message, exception.Message);
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public void Constructor_WithMessageAndInnerException_ShouldSetBoth()
    {
        // Arrange
        const string message = "Test exception message";
        var innerException = new InvalidOperationException("Inner exception");

        // Act
        var exception = new ZendeskApiException(message, innerException);

        // Assert
        Assert.Equal(message, exception.Message);
        Assert.Equal(innerException, exception.InnerException);
    }

    [Fact]
    public void Constructor_WithMessageAndInnerException_ShouldPreserveInnerException()
    {
        // Arrange
        const string message = "Test exception message";
        var innerException = new InvalidOperationException("Inner exception");

        // Act
        var exception = new ZendeskApiException(message, innerException);

        // Assert
        Assert.NotNull(exception.InnerException);
        Assert.Equal("Inner exception", exception.InnerException.Message);
    }

    #endregion

    #region Edge Cases and Special Values Tests

    [Fact]
    public void Constructor_WithEmptyMessage_ShouldSetEmptyMessage()
    {
        // Arrange
        const string message = "";

        // Act
        var exception = new ZendeskApiException(message);

        // Assert
        Assert.Equal("", exception.Message);
    }

    #endregion

    #region Inheritance and Type Tests

    [Fact]
    public void ShouldInheritFromException()
    {
        // Arrange
        const string message = "Test message";

        // Act
        var exception = new ZendeskApiException(message);

        // Assert
        Assert.IsAssignableFrom<Exception>(exception);
    }

    [Fact]
    public void ShouldInheritFromExceptionWithInnerException()
    {
        // Arrange
        const string message = "Test message";
        var innerException = new InvalidOperationException("Inner");

        // Act
        var exception = new ZendeskApiException(message, innerException);

        // Assert
        Assert.IsAssignableFrom<Exception>(exception);
    }

    [Fact]
    public void ShouldHaveMessageProperty()
    {
        // Arrange
        const string message = "Test message";

        // Act
        var exception = new ZendeskApiException(message);

        // Assert
        Assert.NotNull(exception);
        Assert.Equal(message, exception.Message);
    }

    #endregion

    #region ToString Tests

    [Fact]
    public void ToString_WithBasicConstructor_ShouldReturnMessage()
    {
        // Arrange
        const string message = "Test exception message";

        // Act
        var exception = new ZendeskApiException(message);
        string result = exception.ToString();

        // Assert
        Assert.Contains(message, result);
    }

    [Fact]
    public void ToString_WithInnerException_ShouldIncludeInnerException()
    {
        // Arrange
        const string message = "Test exception message";
        var innerException = new InvalidOperationException("Inner");

        // Act
        var exception = new ZendeskApiException(message, innerException);
        string result = exception.ToString();

        // Assert
        Assert.Contains(message, result);
        Assert.Contains("Inner", result);
    }

    #endregion
}
