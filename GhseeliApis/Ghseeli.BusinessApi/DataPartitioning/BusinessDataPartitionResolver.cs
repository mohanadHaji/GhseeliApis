using Ghseeli.IntegrationContracts.DataPartitioning;

namespace Ghseeli.BusinessApi.DataPartitioning;

public interface IBusinessDataPartitionResolver
{
    void SetForTrustedDemoEmail(string? email);
}

public sealed class BusinessDataPartitionResolver : IBusinessDataPartitionResolver
{
    private readonly IConfiguration _configuration;
    private readonly IBusinessDataPartitionContext _context;

    public BusinessDataPartitionResolver(
        IConfiguration configuration,
        IBusinessDataPartitionContext context)
    {
        _configuration = configuration;
        _context = context;
    }

    public void SetForTrustedDemoEmail(string? email)
    {
        var demoEmails = _configuration
            .GetSection("DemoData:BusinessEmails")
            .Get<string[]>() ?? [];
        var partition = demoEmails.Any(value =>
            string.Equals(value?.Trim(), email?.Trim(), StringComparison.OrdinalIgnoreCase))
            ? DataPartitionNames.Demo
            : DataPartitionNames.Production;
        _context.SetTrustedPartition(partition);
    }
}
