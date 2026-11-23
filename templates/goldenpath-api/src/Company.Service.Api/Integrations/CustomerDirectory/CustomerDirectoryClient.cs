using System.Net;
using System.Net.Http.Json;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace Company.Service.Api.Integrations.CustomerDirectory;

/// <summary>
/// Typed HTTP client. Retries, timeouts and the circuit breaker live in the resilience pipeline around it
/// (see <see cref="CustomerDirectoryExtensions"/>), so this class only speaks the directory's protocol.
/// </summary>
internal sealed partial class CustomerDirectoryClient(HttpClient http, ILogger<CustomerDirectoryClient> logger) : ICustomerDirectory
{
    public async Task<CustomerStatus> GetStatusAsync(string customerId, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await http.GetAsync(new Uri($"customers/{Uri.EscapeDataString(customerId)}", UriKind.Relative), cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return CustomerStatus.NotFound;
            }

            response.EnsureSuccessStatusCode();
            var customer = await response.Content.ReadFromJsonAsync<CustomerDocument>(cancellationToken);
            return string.Equals(customer?.Status, "active", StringComparison.OrdinalIgnoreCase)
                ? CustomerStatus.Active
                : CustomerStatus.Blocked;
        }
        catch (Exception exception) when (exception is HttpRequestException or TimeoutRejectedException or BrokenCircuitException
                                          || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            // Expected failure modes of a remote call: report them as data, keep the stack trace in the log.
            LogUnavailable(logger, exception);
            return CustomerStatus.Unavailable;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Customer directory unavailable")]
    private static partial void LogUnavailable(ILogger logger, Exception exception);

    private sealed record CustomerDocument(string Id, string Status);
}
