using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Company.Service.Api.Features.Orders;

namespace Company.Service.ApiTests;

/// <summary>Small helpers so tests read as API calls, not HTTP plumbing.</summary>
public static class ApiClient
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static PlaceOrderRequest NewOrder(string customerId = "cust-42") =>
        new(customerId, "EUR", [new("SKU-1", 2, 1_250), new("SKU-2", 1, 499)]);

    public static async Task<OrderResponse> PlaceOrderAsync(this HttpClient client, PlaceOrderRequest request, CancellationToken cancellationToken)
    {
        using var response = await client.PostAsJsonAsync("/v1/orders", request, Json, cancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<OrderResponse>(Json, cancellationToken))!;
    }

    public static async Task<JsonElement> ReadProblemAsync(this HttpResponseMessage response, CancellationToken cancellationToken)
    {
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        return await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
    }
}
