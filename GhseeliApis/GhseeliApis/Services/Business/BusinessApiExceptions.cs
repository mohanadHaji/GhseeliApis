namespace GhseeliApis.Services.Business;

public abstract class BusinessApiException : Exception
{
    protected BusinessApiException(string message, string? correlationId, Exception? innerException = null)
        : base(message, innerException)
    {
        CorrelationId = correlationId;
    }

    public string? CorrelationId { get; }
}

public sealed class BusinessApiTimeoutException : BusinessApiException
{
    public BusinessApiTimeoutException(string message, string? correlationId, Exception? innerException = null)
        : base(message, correlationId, innerException)
    {
    }
}

public sealed class BusinessApiUnavailableException : BusinessApiException
{
    public BusinessApiUnavailableException(string message, string? correlationId, Exception? innerException = null)
        : base(message, correlationId, innerException)
    {
    }
}

public sealed class BusinessApiConfigurationException : BusinessApiException
{
    public BusinessApiConfigurationException(string message, string? correlationId, Exception? innerException = null)
        : base(message, correlationId, innerException)
    {
    }
}

public sealed class BusinessApiAuthenticationException : BusinessApiException
{
    public BusinessApiAuthenticationException(string message, string? correlationId, Exception? innerException = null)
        : base(message, correlationId, innerException)
    {
    }
}

public sealed class BusinessApiConflictException : BusinessApiException
{
    public BusinessApiConflictException(string message, string? correlationId, Exception? innerException = null)
        : base(message, correlationId, innerException)
    {
    }
}

public sealed class BusinessApiContractException : BusinessApiException
{
    public BusinessApiContractException(string message, string? correlationId, Exception? innerException = null)
        : base(message, correlationId, innerException)
    {
    }
}
