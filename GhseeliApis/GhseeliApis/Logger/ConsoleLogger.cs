using GhseeliApis.Logger.Interfaces;

namespace GhseeliApis.Logger;

/// <summary>
/// Console implementation of the logger interface
/// </summary>
public class ConsoleLogger : IAppLogger
{
    /// <summary>
    /// Logs an informational message to the console
    /// </summary>
    /// <param name="message">The message to log</param>
    public void LogInfo(string message)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"[INFO] {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} - {message}");
        Console.ResetColor();
    }

    /// <summary>
    /// Logs a warning message to the console
    /// </summary>
    /// <param name="message">The warning message to log</param>
    public void LogWarning(string message)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"[WARNING] {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} - {message}");
        Console.ResetColor();
    }

    /// <summary>
    /// Logs an error message to the console
    /// </summary>
    /// <param name="message">The error message to log</param>
    public void LogError(string message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[ERROR] {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} - {message}");
        Console.ResetColor();
    }

    /// <summary>
    /// Logs an error message with exception details to the console
    /// </summary>
    /// <param name="message">The error message to log</param>
    /// <param name="exception">The exception to log</param>
    public void LogError(string message, Exception exception)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[ERROR] {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} - {message}");
        Console.WriteLine($"Exception: {exception.GetType().Name}");
        Console.WriteLine($"Message: {exception.Message}");
        Console.WriteLine($"StackTrace: {exception.StackTrace}");
        Console.ResetColor();
    }
}
