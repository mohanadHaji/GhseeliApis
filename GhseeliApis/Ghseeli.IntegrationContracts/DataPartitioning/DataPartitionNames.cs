namespace Ghseeli.IntegrationContracts.DataPartitioning;

public static class DataPartitionNames
{
    public const string Production = "Production";
    public const string Demo = "Demo";
    public const string ClaimType = "ghseeli_data_partition";
    public const string QueryParameter = "dataPartition";

    public static bool IsSupported(string? value) =>
        string.Equals(value, Production, StringComparison.Ordinal) ||
        string.Equals(value, Demo, StringComparison.Ordinal);

    public static bool IsDemo(string? value) =>
        string.Equals(value, Demo, StringComparison.Ordinal);
}
