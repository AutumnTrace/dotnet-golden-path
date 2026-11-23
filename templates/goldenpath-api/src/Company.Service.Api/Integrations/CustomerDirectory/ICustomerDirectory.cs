namespace Company.Service.Api.Integrations.CustomerDirectory;

/// <summary>What the customer directory says about a customer, including "could not ask".</summary>
public enum CustomerStatus
{
    Active,
    Blocked,
    NotFound,

    /// <summary>The directory did not answer in time, even after retries, or its circuit is open.</summary>
    Unavailable,
}

/// <summary>Port to the customer directory, a downstream service owned by another team.</summary>
public interface ICustomerDirectory
{
    Task<CustomerStatus> GetStatusAsync(string customerId, CancellationToken cancellationToken);
}
