namespace Ghseeli.BusinessApi.Services;

public class BusinessConflictException : InvalidOperationException
{
    public BusinessConflictException(
        string message,
        IReadOnlyDictionary<string, string[]>? errors = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Errors = errors ?? new Dictionary<string, string[]>();
    }

    public IReadOnlyDictionary<string, string[]> Errors { get; }

    public static BusinessConflictException ForField(
        string field,
        string message,
        Exception? innerException = null)
    {
        return new BusinessConflictException(message, new Dictionary<string, string[]>
        {
            [field] = [message]
        }, innerException);
    }
}
