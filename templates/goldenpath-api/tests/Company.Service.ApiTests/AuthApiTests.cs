using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Company.Service.Testing;
using Microsoft.IdentityModel.Tokens;

namespace Company.Service.ApiTests;

public class AuthApiTests(ServiceFactory factory) : IClassFixture<ServiceFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Without_a_token_the_api_answers_401_with_a_problem()
    {
        using var client = Client(token: null);

        using var response = await client.GetAsync("/v1/orders", Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Bearer", response.Headers.WwwAuthenticate.Single().Scheme);
        Assert.Equal(401, (await response.ReadProblemAsync(Ct)).GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task Read_scope_can_read_but_not_write()
    {
        using var client = Client(TestTokens.Create("reader", ["orders:read"]));

        using var read = await client.GetAsync("/v1/orders", Ct);
        using var write = await client.PostAsJsonAsync("/v1/orders", ApiClient.NewOrder(), ApiClient.Json, Ct);

        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
    }

    [Fact]
    public async Task Write_scope_alone_cannot_read()
    {
        using var client = Client(TestTokens.Create("writer", ["orders:write"]));

        using var write = await client.PostAsJsonAsync("/v1/orders", ApiClient.NewOrder(), ApiClient.Json, Ct);
        using var read = await client.GetAsync("/v1/orders", Ct);

        Assert.Equal(HttpStatusCode.Created, write.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, read.StatusCode);
    }

    [Fact]
    public async Task Expired_and_forged_tokens_are_rejected()
    {
        using var expired = Client(TestTokens.Create("late", ["orders:read"], expires: DateTime.UtcNow.AddMinutes(-10)));
        using var forged = Client(TestTokens.Create("mallory", ["orders:read"], key: new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32))));

        using var expiredResponse = await expired.GetAsync("/v1/orders", Ct);
        using var forgedResponse = await forged.GetAsync("/v1/orders", Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, expiredResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, forgedResponse.StatusCode);
    }

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    [InlineData("/openapi/v1.json")]
    public async Task Probes_and_the_api_document_stay_anonymous(string url)
    {
        using var client = Client(token: null);

        using var response = await client.GetAsync(url, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task The_api_document_declares_the_bearer_scheme_on_every_operation()
    {
        using var client = Client(token: null);

        var document = await client.GetFromJsonAsync<JsonElement>("/openapi/v1.json", Ct);

        Assert.Equal("bearer", document.GetProperty("components").GetProperty("securitySchemes").GetProperty("Bearer").GetProperty("scheme").GetString());
        var operations = document.GetProperty("paths").EnumerateObject().SelectMany(path => path.Value.EnumerateObject()).ToList();
        Assert.NotEmpty(operations);
        Assert.All(operations, operation => Assert.True(operation.Value.TryGetProperty("security", out _), operation.Name));
    }

    private HttpClient Client(string? token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = token is null ? null : new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
