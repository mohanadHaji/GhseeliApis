namespace GhseeliApis.Persistence;

public sealed class CustomerSchemaOptions
{
    public const string SectionName = "CustomerSchema";
    public const string OwnedDefaultSchema = "dbo";

    public string DefaultSchema { get; init; } = string.Empty;
}
