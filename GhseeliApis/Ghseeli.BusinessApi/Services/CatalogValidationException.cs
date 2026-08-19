namespace Ghseeli.BusinessApi.Services;

public class CatalogValidationException : InvalidOperationException
{
    public CatalogValidationException(
        string message,
        IReadOnlyDictionary<string, string[]>? errors = null)
        : base(message)
    {
        Errors = errors ?? new Dictionary<string, string[]>();
    }

    public IReadOnlyDictionary<string, string[]> Errors { get; }

    public static CatalogValidationException ForField(string field, string message)
    {
        return new CatalogValidationException(message, new Dictionary<string, string[]>
        {
            [field] = [message]
        });
    }
}
