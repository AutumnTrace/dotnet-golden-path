using Company.Service.Testing;

namespace Company.Service.ApiTests;

/// <summary>
/// The OpenAPI document is the service's public contract. Any change to it, intended or not, shows up as a diff
/// of <c>Snapshots/openapi-v1.approved.json</c> in code review.
/// </summary>
public class OpenApiContractTests(ServiceFactory factory) : IClassFixture<ServiceFactory>
{
    [Fact]
    public async Task The_v1_document_matches_the_approved_contract()
    {
        using var client = factory.CreateClient();

        var document = await client.GetStringAsync(new Uri("/openapi/v1.json", UriKind.Relative), TestContext.Current.CancellationToken);

        Snapshot.MatchJson(document, "openapi-v1");
    }
}
