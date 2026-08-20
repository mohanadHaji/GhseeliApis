namespace Ghseeli.BusinessApi.Services;

public class AvailabilityValidationException : InvalidOperationException
{
    public AvailabilityValidationException(
        string message,
        IReadOnlyDictionary<string, string[]>? errors = null)
        : base(message)
    {
        Errors = errors ?? new Dictionary<string, string[]>();
    }

    public IReadOnlyDictionary<string, string[]> Errors { get; }

    public static AvailabilityValidationException ForField(
        string field,
        string message)
    {
        return new AvailabilityValidationException(message, new Dictionary<string, string[]>
        {
            [field] = [message]
        });
    }
}
