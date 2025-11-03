using FluentAssertions;
using GhseeliApis.Logger;
using System.Text;

namespace GhseeliApis.Tests.Logger;

/// <summary>
/// Unit tests for ConsoleLogger
/// </summary>
public class ConsoleLoggerTests : IDisposable
{
    private readonly StringWriter _stringWriter;
    private readonly TextWriter _originalOutput;
    private readonly ConsoleLogger _logger;

    public ConsoleLoggerTests()
    {
        // Redirect console output to capture it for testing
        _stringWriter = new StringWriter();
        _originalOutput = Console.Out;
        Console.SetOut(_stringWriter);
        
        _logger = new ConsoleLogger();
    }

    public void Dispose()
    {
        // Restore original console output
        Console.SetOut(_originalOutput);
        _stringWriter.Dispose();
    }

    private string GetConsoleOutput()
    {
        return _stringWriter.ToString();
    }

    #region LogInfo Tests

    [Fact]
    public void LogInfo_WritesToConsole()
    {
        // Arrange
        var message = "Test info message";

        // Act
        _logger.LogInfo(message);

        // Assert
        var output = GetConsoleOutput();
        output.Should().Contain("[INFO]");
        output.Should().Contain(message);
    }

    [Fact]
    public void LogInfo_IncludesTimestamp()
    {
        // Arrange
        var message = "Test message";

        // Act
        _logger.LogInfo(message);

        // Assert
        var output = GetConsoleOutput();
        output.Should().MatchRegex(@"\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}");
    }

    [Fact]
    public void LogInfo_HandlesEmptyMessage()
    {
        // Act
        _logger.LogInfo("");

        // Assert
        var output = GetConsoleOutput();
        output.Should().Contain("[INFO]");
    }

    [Fact]
    public void LogInfo_HandlesNullMessage()
    {
        // Act
        _logger.LogInfo(null!);

        // Assert
        var output = GetConsoleOutput();
        output.Should().Contain("[INFO]");
    }

    #endregion

    #region LogWarning Tests

    [Fact]
    public void LogWarning_WritesToConsole()
    {
        // Arrange
        var message = "Test warning message";

        // Act
        _logger.LogWarning(message);

        // Assert
        var output = GetConsoleOutput();
        output.Should().Contain("[WARNING]");
        output.Should().Contain(message);
    }

    [Fact]
    public void LogWarning_IncludesTimestamp()
    {
        // Arrange
        var message = "Test warning";

        // Act
        _logger.LogWarning(message);

        // Assert
        var output = GetConsoleOutput();
        output.Should().MatchRegex(@"\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}");
    }

    [Fact]
    public void LogWarning_HandlesSpecialCharacters()
    {
        // Arrange
        var message = "Warning: Special chars !@#$%^&*()";

        // Act
        _logger.LogWarning(message);

        // Assert
        var output = GetConsoleOutput();
        output.Should().Contain(message);
    }

    #endregion

    #region LogError Tests

    [Fact]
    public void LogError_WithMessageOnly_WritesToConsole()
    {
        // Arrange
        var message = "Test error message";

        // Act
        _logger.LogError(message);

        // Assert
        var output = GetConsoleOutput();
        output.Should().Contain("[ERROR]");
        output.Should().Contain(message);
    }

    [Fact]
    public void LogError_WithMessageOnly_IncludesTimestamp()
    {
        // Arrange
        var message = "Test error";

        // Act
        _logger.LogError(message);

        // Assert
        var output = GetConsoleOutput();
        output.Should().MatchRegex(@"\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}");
    }

    [Fact]
    public void LogError_WithException_WritesExceptionDetails()
    {
        // Arrange
        var message = "An error occurred";
        var exception = new InvalidOperationException("Test exception");

        // Act
        _logger.LogError(message, exception);

        // Assert
        var output = GetConsoleOutput();
        output.Should().Contain("[ERROR]");
        output.Should().Contain(message);
        output.Should().Contain("Exception:");
        output.Should().Contain("InvalidOperationException");
        output.Should().Contain("Message:");
        output.Should().Contain("Test exception");
    }

    [Fact]
    public void LogError_WithException_IncludesStackTrace()
    {
        // Arrange
        var message = "An error occurred";
        var exception = new Exception("Test exception");

        // Act
        _logger.LogError(message, exception);

        // Assert
        var output = GetConsoleOutput();
        output.Should().Contain("StackTrace:");
    }

    [Fact]
    public void LogError_WithNestedException_LogsInnerException()
    {
        // Arrange
        var message = "Nested error";
        var innerException = new ArgumentException("Inner exception");
        var outerException = new InvalidOperationException("Outer exception", innerException);

        // Act
        _logger.LogError(message, outerException);

        // Assert
        var output = GetConsoleOutput();
        output.Should().Contain("InvalidOperationException");
        output.Should().Contain("Outer exception");
    }

    #endregion

    #region Multiple Log Calls Tests

    [Fact]
    public void MultipleLogCalls_AllAppearInOutput()
    {
        // Act
        _logger.LogInfo("Info message");
        _logger.LogWarning("Warning message");
        _logger.LogError("Error message");

        // Assert
        var output = GetConsoleOutput();
        output.Should().Contain("[INFO]");
        output.Should().Contain("Info message");
        output.Should().Contain("[WARNING]");
        output.Should().Contain("Warning message");
        output.Should().Contain("[ERROR]");
        output.Should().Contain("Error message");
    }

    [Fact]
    public void MultipleLogCalls_MaintainOrder()
    {
        // Act
        _logger.LogInfo("First");
        _logger.LogWarning("Second");
        _logger.LogError("Third");

        // Assert
        var output = GetConsoleOutput();
        var firstIndex = output.IndexOf("First");
        var secondIndex = output.IndexOf("Second");
        var thirdIndex = output.IndexOf("Third");

        firstIndex.Should().BeLessThan(secondIndex);
        secondIndex.Should().BeLessThan(thirdIndex);
    }

    #endregion

    #region Long Message Tests

    [Fact]
    public void LogInfo_HandlesLongMessage()
    {
        // Arrange
        var longMessage = new string('A', 1000);

        // Act
        _logger.LogInfo(longMessage);

        // Assert
        var output = GetConsoleOutput();
        output.Should().Contain(longMessage);
    }

    [Fact]
    public void LogInfo_HandlesMultilineMessage()
    {
        // Arrange
        var multilineMessage = "Line 1\nLine 2\nLine 3";

        // Act
        _logger.LogInfo(multilineMessage);

        // Assert
        var output = GetConsoleOutput();
        output.Should().Contain("Line 1");
        output.Should().Contain("Line 2");
        output.Should().Contain("Line 3");
    }

    #endregion
}
