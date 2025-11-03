namespace GhseeliApis.Logger.Interfaces;

/// <summary>
/// Interface for logging operations
/// </summary>
public interface IAppLogger
{
    /// <summary>
    /// Logs an informational message
    /// </summary>
    /// <param name="message">The message to log</param>
    void LogInfo(string message);

    /// <summary>
    /// Logs a warning message
    /// </summary>
    /// <param name="message">The warning message to log</param>
    void LogWarning(string message);

    /// <summary>
    /// Logs an error message
    /// </summary>
    /// <param name="message">The error message to log</param>
    void LogError(string message);

    /// <summary>
    /// Logs an error message with exception details
    /// </summary>
    /// <param name="message">The error message to log</param>
    /// <param name="exception">The exception to log</param>
    void LogError(string message, Exception exception);
}
