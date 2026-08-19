using Microsoft.Extensions.Logging;

namespace Ghseeli.Common.Logging;

public class ConsoleLogger : IAppLogger
{
    private readonly ILogger<ConsoleLogger> _logger;

    public ConsoleLogger(ILogger<ConsoleLogger> logger)
    {
        _logger = logger;
    }

    public void LogInfo(string message)
    {
        _logger.LogInformation("{Message}", message ?? string.Empty);
    }

    public void LogWarning(string message)
    {
        _logger.LogWarning("{Message}", message ?? string.Empty);
    }

    public void LogError(string message)
    {
        _logger.LogError("{Message}", message ?? string.Empty);
    }

    public void LogError(string message, Exception exception)
    {
        _logger.LogError(exception, "{Message}", message ?? string.Empty);
    }
}
