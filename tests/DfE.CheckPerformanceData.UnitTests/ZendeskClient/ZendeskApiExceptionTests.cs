using Xunit;
using DfE.CheckPerformanceData.Application.ZendeskClient;

namespace DfE.CheckPerformanceData.UnitTests.ZendeskClient;

/// <summary>
/// Unit tests for ZendeskApiException class.
/// </summary>
public class ZendeskApiExceptionTests
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
        Assert.Null(exception.HttpStatusCode);
        Assert.Null(exception.Operation);
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
        Assert.Null(exception.HttpStatusCode);
        Assert.Null(exception.Operation);
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

    #region Constructor with HttpStatusCode and Operation Tests

    [Fact]
    public void Constructor_WithMessageAndHttpStatusCodeAndOperation_ShouldSetAllProperties()
    {
        // Arrange
        const string message = "Test exception message";
        const int httpStatusCode = 404;
        const string operation = "GetTicket";

        // Act
        var exception = new ZendeskApiException(message, httpStatusCode, operation);

        // Assert
        Assert.Equal(message, exception.Message);
        Assert.Equal(httpStatusCode, exception.HttpStatusCode);
        Assert.Equal(operation, exception.Operation);
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public void Constructor_WithMessageAndHttpStatusCodeAndOperation_ShouldSetHttpStatusCode()
    {
        // Arrange
        const string message = "Test exception message";
        const int httpStatusCode = 500;

        // Act
        var exception = new ZendeskApiException(message, httpStatusCode, "TestOperation");

        // Assert
        Assert.Equal(httpStatusCode, exception.HttpStatusCode);
    }

    [Fact]
    public void Constructor_WithMessageAndHttpStatusCodeAndOperation_ShouldSetOperation()
    {
        // Arrange
        const string message = "Test exception message";
        const string operation = "CreateTicket";

        // Act
        var exception = new ZendeskApiException(message, null, operation);

        // Assert
        Assert.Equal(operation, exception.Operation);
    }

    [Fact]
    public void Constructor_WithMessageAndHttpStatusCodeAndOperation_ShouldSetNullHttpStatusCode()
    {
        // Arrange
        const string message = "Test exception message";
        int? httpStatusCode = null;

        // Act
        var exception = new ZendeskApiException(message, httpStatusCode, "TestOperation");

        // Assert
        Assert.Null(exception.HttpStatusCode);
    }

    #endregion

    #region Constructor with All Parameters Tests

    [Fact]
    public void Constructor_WithMessageAndHttpStatusCodeAndOperationAndInnerException_ShouldSetAllProperties()
    {
        // Arrange
        const string message = "Test exception message";
        const int httpStatusCode = 401;
        const string operation = "Authenticate";
        var innerException = new InvalidOperationException("Inner exception");

        // Act
        var exception = new ZendeskApiException(message, httpStatusCode, operation, innerException);

        // Assert
        Assert.Equal(message, exception.Message);
        Assert.Equal(httpStatusCode, exception.HttpStatusCode);
        Assert.Equal(operation, exception.Operation);
        Assert.NotNull(exception.InnerException);
        Assert.Equal("Inner exception", exception.InnerException.Message);
    }

    [Fact]
    public void Constructor_WithMessageAndHttpStatusCodeAndOperationAndInnerException_ShouldPreserveInnerException()
    {
        // Arrange
        const string message = "Test exception message";
        const int httpStatusCode = 403;
        const string operation = "UpdateTicket";
        var innerException = new UnauthorizedAccessException("Unauthorized");

        // Act
        var exception = new ZendeskApiException(message, httpStatusCode, operation, innerException);

        // Assert
        Assert.NotNull(exception.InnerException);
        Assert.Equal("Unauthorized", exception.InnerException.Message);
    }

    [Fact]
    public void Constructor_WithMessageAndHttpStatusCodeAndOperationAndInnerException_ShouldSetInnerException()
    {
        // Arrange
        const string message = "Test exception message";
        const int httpStatusCode = 429;
        const string operation = "RateLimited";
        var innerException = new TimeoutException("Timeout");

        // Act
        var exception = new ZendeskApiException(message, httpStatusCode, operation, innerException);

        // Assert
        Assert.NotNull(exception.InnerException);
        Assert.IsType<TimeoutException>(exception.InnerException);
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

    [Fact]
    public void Constructor_WithZeroHttpStatusCode_ShouldSetZero()
    {
        // Arrange
        const string message = "Test message";
        const int httpStatusCode = 0;

        // Act
        var exception = new ZendeskApiException(message, httpStatusCode, "TestOperation");

        // Assert
        Assert.Equal(0, exception.HttpStatusCode);
    }

    [Fact]
    public void Constructor_WithMaxHttpStatusCode_ShouldSetMax()
    {
        // Arrange
        const string message = "Test message";
        const int httpStatusCode = 999;

        // Act
        var exception = new ZendeskApiException(message, httpStatusCode, "TestOperation");

        // Assert
        Assert.Equal(999, exception.HttpStatusCode);
    }

    [Fact]
    public void Constructor_WithEmptyOperation_ShouldSetEmptyString()
    {
        // Arrange
        const string message = "Test message";
        const string operation = "";

        // Act
        var exception = new ZendeskApiException(message, 404, operation);

        // Assert
        Assert.Equal("", exception.Operation);
    }

    [Fact]
    public void Constructor_WithSpecialCharactersInOperation_ShouldStoreSpecialChars()
    {
        // Arrange
        const string message = "Test message";
        const string operation = "Operation: /?\\|<>()[]{}";

        // Act
        var exception = new ZendeskApiException(message, 404, operation);

        // Assert
        Assert.Equal("Operation: /?\\|<>()[]{}", exception.Operation);
    }

    [Fact]
    public void Constructor_WithUnicodeInOperation_ShouldStoreUnicode()
    {
        // Arrange
        const string message = "Test message";
        const string operation = "操作: 测试";

        // Act
        var exception = new ZendeskApiException(message, 404, operation);

        // Assert
        Assert.Equal("操作: 测试", exception.Operation);
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
    public void ShouldInheritFromExceptionWithAllParameters()
    {
        // Arrange
        const string message = "Test message";
        const int httpStatusCode = 404;
        const string operation = "TestOperation";
        var innerException = new InvalidOperationException("Inner");

        // Act
        var exception = new ZendeskApiException(message, httpStatusCode, operation, innerException);

        // Assert
        Assert.IsAssignableFrom<Exception>(exception);
    }

    [Fact]
    public void ShouldHaveMessageProperty()
    {
        // Arrange
        const string message = "Test message";
        const int httpStatusCode = 404;
        const string operation = "TestOperation";
        var innerException = new InvalidOperationException("Inner");

        // Act
        var exception = new ZendeskApiException(message, httpStatusCode, operation, innerException);

        // Assert
        Assert.NotNull(exception);
        Assert.Equal(message, exception.Message);
    }

    [Fact]
    public void ShouldHaveHttpStatusCodeProperty()
    {
        // Arrange
        const string message = "Test message";
        const int httpStatusCode = 404;
        const string operation = "TestOperation";
        var innerException = new InvalidOperationException("Inner");

        // Act
        var exception = new ZendeskApiException(message, httpStatusCode, operation, innerException);

        // Assert
        Assert.Equal(httpStatusCode, exception.HttpStatusCode);
    }

    [Fact]
    public void ShouldHaveOperationProperty()
    {
        // Arrange
        const string message = "Test message";
        const int httpStatusCode = 404;
        const string operation = "TestOperation";
        var innerException = new InvalidOperationException("Inner");

        // Act
        var exception = new ZendeskApiException(message, httpStatusCode, operation, innerException);

        // Assert
        Assert.Equal(operation, exception.Operation);
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