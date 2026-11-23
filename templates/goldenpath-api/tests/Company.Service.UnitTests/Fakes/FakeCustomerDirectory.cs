using Company.Service.Api.Integrations.CustomerDirectory;

namespace Company.Service.UnitTests.Fakes;

/// <summary>Every customer is active unless a test says otherwise.</summary>
internal sealed class FakeCustomerDirectory : ICustomerDirectory
{
    public Dictionary<string, CustomerStatus> Statuses { get; } = [];

    public int Calls { get; private set; }

    public Task<CustomerStatus> GetStatusAsync(string customerId, CancellationToken cancellationToken)
    {
        Calls++;
        return Task.FromResult(Statuses.GetValueOrDefault(customerId, CustomerStatus.Active));
    }
}
