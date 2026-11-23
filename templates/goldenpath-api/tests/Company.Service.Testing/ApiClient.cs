using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Company.Service.Api.Features.Orders;

namespace Company.Service.Testing;

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

    public static async Task<OrderResponse> GetOrderAsync(this HttpClient client, Guid id, CancellationToken cancellationToken) =>
        (await client.GetFromJsonAsync<OrderResponse>($"/v1/orders/{id}", Json, cancellationToken))!;

    /// <summary>Polls until <paramref name="condition"/> holds; for effects of asynchronous message handling.</summary>
    public static async Task<OrderResponse> WaitForOrderAsync(
        this HttpClient client,
        Guid id,
        Func<OrderResponse, bool> condition,
        CancellationToken cancellationToken,
        TimeSpan? timeout = null)
    {
        var deadline = DateTimeOffset.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
        while (true)
        {
            var order = await client.GetOrderAsync(id, cancellationToken);
            if (condition(order) || DateTimeOffset.UtcNow > deadline)
            {
                return order;
            }

            await Task.Delay(100, cancellationToken);
        }
    }

    public static async Task<JsonElement> ReadProblemAsync(this HttpResponseMessage response, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(response);

        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (mediaType != "application/problem+json")
        {
            throw new InvalidOperationException($"Expected an application/problem+json response, got '{mediaType}'.");
        }

        return await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
    }
}
