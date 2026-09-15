using Ghseeli.IntegrationContracts.DataPartitioning;

namespace Ghseeli.BusinessApi.DataPartitioning;

public interface IBusinessDataPartitionContext
{
    string Partition { get; }
    bool IsDemo { get; }
    bool IsAssigned { get; }
    void SetTrustedPartition(string partition);
}

public sealed class BusinessDataPartitionContext : IBusinessDataPartitionContext
{
    private string? _partition;

    public string Partition => _partition ?? DataPartitionNames.Production;
    public bool IsDemo => DataPartitionNames.IsDemo(Partition);
    public bool IsAssigned => _partition is not null;

    public void SetTrustedPartition(string partition)
    {
        if (!DataPartitionNames.IsSupported(partition))
        {
            throw new InvalidOperationException($"Unsupported trusted data partition '{partition}'.");
        }

        if (_partition is not null &&
            !string.Equals(_partition, partition, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"The request data partition is already '{_partition}' and cannot change to '{partition}'.");
        }

        _partition = partition;
    }
}
