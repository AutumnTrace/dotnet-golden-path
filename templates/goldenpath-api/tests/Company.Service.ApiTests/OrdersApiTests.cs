using System.Net;
using System.Net.Http.Json;
using Company.Service.Api.Features.Orders;
using Company.Service.Api.Features.Orders.Domain;
using Company.Service.Testing;

namespace Company.Service.ApiTests;

public class OrdersApiTests(ServiceFactory factory) : IClassFixture<ServiceFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Placing_an_order_returns_201_with_a_location_and_the_priced_order()
    {
        using var response = await _client.PostAsJsonAsync("/v1/orders", ApiClient.NewOrder(), ApiClient.Json, Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var order = await response.Content.ReadFromJsonAsync<OrderResponse>(ApiClient.Json, Ct);
        Assert.NotNull(order);
        Assert.Equal($"/v1/orders/{order.Id}", response.Headers.Location?.ToString());
        Assert.Equal(OrderStatus.Placed, order.Status);
        Assert.Equal(2 * 1_250 + 499, order.TotalMinor);
        Assert.Equal("1.0", Assert.Single(response.Headers.GetValues("api-supported-versions")));
    }

    [Fact]
    public async Task A_placed_order_can_be_read_back()
    {
        var placed = await _client.PlaceOrderAsync(ApiClient.NewOrder(), Ct);

        var order = await _client.GetFromJsonAsync<OrderResponse>($"/v1/orders/{placed.Id}", ApiClient.Json, Ct);

        Assert.Equivalent(placed, order);
    }

    [Fact]
    public async Task An_unknown_order_is_a_404_problem_with_a_stable_code_and_a_trace_id()
    {
        var id = Guid.CreateVersion7();

        using var response = await _client.GetAsync($"/v1/orders/{id}", Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.ReadProblemAsync(Ct);
        Assert.Equal(404, problem.GetProperty("status").GetInt32());
        Assert.Equal("orders.not_found", problem.GetProperty("errorCode").GetString());
        Assert.Contains(id.ToString(), problem.GetProperty("detail").GetString(), StringComparison.Ordinal);
        Assert.Matches("^[0-9a-f]{32}$", problem.GetProperty("traceId").GetString());
    }

    [Fact]
    public async Task A_malformed_request_is_a_400_with_errors_per_field()
    {
        var request = new { customerId = "", currency = "eur", lines = new[] { new { sku = "SKU-1", quantity = 0, unitPriceMinor = 100 } } };

        using var response = await _client.PostAsJsonAsync("/v1/orders", request, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await response.ReadProblemAsync(Ct)).GetProperty("errors");
        Assert.True(errors.TryGetProperty("CustomerId", out _));
        Assert.True(errors.TryGetProperty("Currency", out _));
        Assert.True(errors.TryGetProperty("Lines[0].Quantity", out _));
    }

    [Fact]
    public async Task Unreadable_json_is_a_400_not_a_500()
    {
        using var content = new StringContent("{\"customerId\": 5", System.Text.Encoding.UTF8, "application/json");

        using var response = await _client.PostAsync("/v1/orders", content, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("request.malformed", (await response.ReadProblemAsync(Ct)).GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task A_business_rule_violation_is_a_422_with_the_rule_code()
    {
        var request = ApiClient.NewOrder() with { Lines = [new("SKU-1", 1, 100), new("sku-1", 1, 100)] };

        using var response = await _client.PostAsJsonAsync("/v1/orders", request, ApiClient.Json, Ct);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("orders.duplicate_sku", (await response.ReadProblemAsync(Ct)).GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task Cancelling_is_idempotent()
    {
        var placed = await _client.PlaceOrderAsync(ApiClient.NewOrder(), Ct);

        using var first = await _client.PostAsJsonAsync($"/v1/orders/{placed.Id}/cancel", new CancelOrderRequest("customer request"), Ct);
        using var second = await _client.PostAsJsonAsync($"/v1/orders/{placed.Id}/cancel", new CancelOrderRequest("client retry"), Ct);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var order = await second.Content.ReadFromJsonAsync<OrderResponse>(ApiClient.Json, Ct);
        Assert.Equal(OrderStatus.Cancelled, order!.Status);
        Assert.Equal("customer request", order.CancellationReason);
    }

    [Fact]
    public async Task Listing_pages_newest_first_with_a_cursor()
    {
        var customer = $"pager-{Guid.NewGuid():N}";
        var placed = new List<OrderResponse>();
        for (var i = 0; i < 3; i++)
        {
            placed.Add(await _client.PlaceOrderAsync(ApiClient.NewOrder(customer), Ct));
        }

        var first = await _client.GetFromJsonAsync<OrderPage>($"/v1/orders?customerId={customer}&limit=2", ApiClient.Json, Ct);
        var second = await _client.GetFromJsonAsync<OrderPage>($"/v1/orders?customerId={customer}&limit=2&after={first!.NextCursor}", ApiClient.Json, Ct);

        // Ids are UUIDv7: ordered by time to the millisecond, so assert the keyset contract rather than
        // the exact order of orders placed within the same millisecond.
        var all = first.Items.Concat(second!.Items).Select(o => o.Id).ToList();
        Assert.Equal(2, first.Items.Count);
        Assert.Single(second.Items);
        Assert.Null(second.NextCursor);
        Assert.Equal(placed.Select(o => o.Id).Order(), all.Order());
        Assert.Equal(all.OrderDescending(), all);
    }

    [Theory]
    [InlineData("/v1/orders?limit=0")]
    [InlineData("/v1/orders?limit=101")]
    [InlineData("/v1/orders?status=Shipped")]
    public async Task Bad_query_parameters_are_400s(string url)
    {
        using var response = await _client.GetAsync(url, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await response.ReadProblemAsync(Ct);
    }

    [Fact]
    public async Task An_unknown_api_version_is_not_routed()
    {
        using var response = await _client.GetAsync("/v2/orders", Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
