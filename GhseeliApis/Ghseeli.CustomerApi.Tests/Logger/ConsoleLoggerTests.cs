using FluentAssertions;
using Ghseeli.Common.Logging;
using Microsoft.Extensions.Logging;

namespace GhseeliApis.Tests.Logger;

/// <summary>
/// Unit tests for the shared ConsoleLogger wrapper.
/// </summary>
public class ConsoleLoggerTests
{
    [Fact]
    public void LogInfo_ForwardsInformationMessageToFrameworkLogger()
    {
        var sink = new TestLogger<ConsoleLogger>();
        var logger = new ConsoleLogger(sink);

        logger.LogInfo("Test info message");

        sink.Entries.Should().ContainSingle(entry =>
            entry.Level == LogLevel.Information &&
            entry.Message == "Test info message" &&
            entry.Exception == null);
    }

    [Fact]
    public void LogWarning_ForwardsWarningMessageToFrameworkLogger()
    {
        var sink = new TestLogger<ConsoleLogger>();
        var logger = new ConsoleLogger(sink);

        logger.LogWarning("Test warning message");

        sink.Entries.Should().ContainSingle(entry =>
            entry.Level == LogLevel.Warning &&
            entry.Message == "Test warning message");
    }

    [Fact]
    public void LogError_ForwardsErrorMessageAndExceptionToFrameworkLogger()
    {
        var sink = new TestLogger<ConsoleLogger>();
        var logger = new ConsoleLogger(sink);
        var exception = new InvalidOperationException("Test exception");

        logger.LogError("An error occurred", exception);

        sink.Entries.Should().ContainSingle(entry =>
            entry.Level == LogLevel.Error &&
            entry.Message == "An error occurred" &&
            entry.Exception == exception);
    }

    [Fact]
    public void LogError_WithNullMessage_UsesEmptyMessage()
    {
        var sink = new TestLogger<ConsoleLogger>();
        var logger = new ConsoleLogger(sink);

        logger.LogError(null!);

        sink.Entries.Should().ContainSingle(entry =>
            entry.Level == LogLevel.Error &&
            entry.Message == string.Empty);
    }

    [Fact]
    public void LogInfo_WithNullMessage_UsesEmptyMessage()
    {
        var sink = new TestLogger<ConsoleLogger>();
        var logger = new ConsoleLogger(sink);

        logger.LogInfo(null!);

        sink.Entries.Should().ContainSingle(entry =>
            entry.Level == LogLevel.Information &&
            entry.Message == string.Empty);
    }

    [Fact]
    public void LogInfo_WithEmptyMessage_ForwardsEmptyMessage()
    {
        var sink = new TestLogger<ConsoleLogger>();
        var logger = new ConsoleLogger(sink);

        logger.LogInfo(string.Empty);

        sink.Entries.Should().ContainSingle(entry =>
            entry.Level == LogLevel.Information &&
            entry.Message == string.Empty);
    }

    [Fact]
    public void LogInfo_WithSpecialCharacters_PreservesMessage()
    {
        var sink = new TestLogger<ConsoleLogger>();
        var logger = new ConsoleLogger(sink);
        const string message = "Special chars !@#$%^&*()";

        logger.LogInfo(message);

        sink.Entries.Should().ContainSingle(entry => entry.Message == message);
    }

    [Fact]
    public void LogInfo_WithLongMessage_PreservesMessage()
    {
        var sink = new TestLogger<ConsoleLogger>();
        var logger = new ConsoleLogger(sink);
        var message = new string('A', 1000);

        logger.LogInfo(message);

        sink.Entries.Should().ContainSingle(entry => entry.Message == message);
    }

    [Fact]
    public void LogInfo_WithMultilineMessage_PreservesMessage()
    {
        var sink = new TestLogger<ConsoleLogger>();
        var logger = new ConsoleLogger(sink);
        const string message = "Line 1\nLine 2\nLine 3";

        logger.LogInfo(message);

        sink.Entries.Should().ContainSingle(entry => entry.Message == message);
    }

    [Fact]
    public void LogWarning_WithNullMessage_UsesEmptyMessage()
    {
        var sink = new TestLogger<ConsoleLogger>();
        var logger = new ConsoleLogger(sink);

        logger.LogWarning(null!);

        sink.Entries.Should().ContainSingle(entry =>
            entry.Level == LogLevel.Warning &&
            entry.Message == string.Empty);
    }

    [Fact]
    public void LogWarning_WithEmptyMessage_ForwardsEmptyMessage()
    {
        var sink = new TestLogger<ConsoleLogger>();
        var logger = new ConsoleLogger(sink);

        logger.LogWarning(string.Empty);

        sink.Entries.Should().ContainSingle(entry =>
            entry.Level == LogLevel.Warning &&
            entry.Message == string.Empty);
    }

    [Fact]
    public void LogWarning_WithSpecialCharacters_PreservesMessage()
    {
        var sink = new TestLogger<ConsoleLogger>();
        var logger = new ConsoleLogger(sink);
        const string message = "Warning: !@#$%^&*()";

        logger.LogWarning(message);

        sink.Entries.Should().ContainSingle(entry => entry.Message == message);
    }

    [Fact]
    public void LogError_WithMessageOnly_ForwardsErrorWithoutException()
    {
        var sink = new TestLogger<ConsoleLogger>();
        var logger = new ConsoleLogger(sink);

        logger.LogError("Test error");

        sink.Entries.Should().ContainSingle(entry =>
            entry.Level == LogLevel.Error &&
            entry.Message == "Test error" &&
            entry.Exception == null);
    }

    [Fact]
    public void LogError_WithEmptyMessage_ForwardsEmptyMessage()
    {
        var sink = new TestLogger<ConsoleLogger>();
        var logger = new ConsoleLogger(sink);

        logger.LogError(string.Empty);

        sink.Entries.Should().ContainSingle(entry =>
            entry.Level == LogLevel.Error &&
            entry.Message == string.Empty);
    }

    [Fact]
    public void MultipleLogCalls_PreserveCallOrder()
    {
        var sink = new TestLogger<ConsoleLogger>();
        var logger = new ConsoleLogger(sink);

        logger.LogInfo("First");
        logger.LogWarning("Second");
        logger.LogError("Third");

        sink.Entries.Select(entry => entry.Message)
            .Should().ContainInOrder("First", "Second", "Third");
    }

    [Fact]
    public void LogError_WithNestedException_PreservesExceptionGraph()
    {
        var sink = new TestLogger<ConsoleLogger>();
        var logger = new ConsoleLogger(sink);
        var innerException = new ArgumentException("Inner exception");
        var outerException = new InvalidOperationException(
            "Outer exception",
            innerException);

        logger.LogError("Nested error", outerException);

        sink.Entries.Should().ContainSingle(entry =>
            entry.Exception == outerException &&
            entry.Exception.InnerException == innerException);
    }

    private sealed class TestLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull
        {
            return NullScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new LogEntry(logLevel, formatter(state, exception), exception));
        }

        public sealed record LogEntry(LogLevel Level, string Message, Exception? Exception);

        private sealed class NullScope : IDisposable
        {
            public static NullScope Instance { get; } = new();

            public void Dispose()
            {
            }
        }
    }
}
