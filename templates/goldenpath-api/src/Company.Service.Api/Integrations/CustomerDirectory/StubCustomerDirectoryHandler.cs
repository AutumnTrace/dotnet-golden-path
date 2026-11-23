using System.Net;
using System.Net.Http.Json;

namespace Company.Service.Api.Integrations.CustomerDirectory;

/// <summary>
/// Stand-in for the real directory when <c>CustomerDirectory:UseStub</c> is true. Customer ids starting with
/// <c>blocked-</c> are blocked, ids starting with <c>unknown-</c> do not exist, everyone else is active.
/// </summary>
internal sealed class StubCustomerDirectoryHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var id = Uri.UnescapeDataString(request.RequestUri?.Segments.LastOrDefault() ?? string.Empty);

        if (id.StartsWith("unknown-", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        var status = id.StartsWith("blocked-", StringComparison.OrdinalIgnoreCase) ? "blocked" : "active";
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { id, status }),
        });
    }
}
