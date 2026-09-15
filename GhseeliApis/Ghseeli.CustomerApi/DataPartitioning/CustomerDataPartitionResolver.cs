using Ghseeli.IntegrationContracts.DataPartitioning;

namespace GhseeliApis.DataPartitioning;

public interface ICustomerDataPartitionResolver
{
    void SetForTrustedDemoEmail(string? email);
}

public sealed class CustomerDataPartitionResolver : ICustomerDataPartitionResolver
{
    private readonly IConfiguration _configuration;
    private readonly ICustomerDataPartitionContext _context;

    public CustomerDataPartitionResolver(
        IConfiguration configuration,
        ICustomerDataPartitionContext context)
    {
        _configuration = configuration;
        _context = context;
    }

    public void SetForTrustedDemoEmail(string? email)
    {
        var demoEmails = _configuration
            .GetSection("DemoData:CustomerEmails")
            .Get<string[]>() ?? [];
        var partition = demoEmails.Any(value =>
            string.Equals(value?.Trim(), email?.Trim(), StringComparison.OrdinalIgnoreCase))
            ? DataPartitionNames.Demo
            : DataPartitionNames.Production;
        _context.SetTrustedPartition(partition);
    }
}
