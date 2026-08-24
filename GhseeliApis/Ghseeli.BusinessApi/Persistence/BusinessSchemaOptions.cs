namespace Ghseeli.BusinessApi.Persistence;

public sealed class BusinessSchemaOptions
{
    public const string SectionName = "BusinessSchema";
    public const string OwnedDefaultSchema = "dbo";
    public const string MigrationsHistoryTable = "__EFMigrationsHistory";

    public string DefaultSchema { get; init; } = string.Empty;
}
