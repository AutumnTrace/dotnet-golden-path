using Asp.Versioning;
using Company.Service.Api.Infrastructure.RateLimiting;
#if (UseJwt)
using Company.Service.Api.Infrastructure.Security;
#endif

namespace Company.Service.Api.Features.Orders;

/// <summary>The slice's front door: everything Orders needs is registered and mapped here.</summary>
public static class OrdersModule
{
    public static IServiceCollection AddOrdersFeature(this IServiceCollection services)
    {
        services.AddSingleton<IOrderStore, InMemoryOrderStore>();
        services.AddSingleton<OrdersMetrics>();
        services.AddScoped<PlaceOrderHandler>();
        services.AddScoped<CancelOrderHandler>();
        return services;
    }

    public static IEndpointRouteBuilder MapOrdersEndpoints(this IEndpointRouteBuilder app)
    {
        var orders = app.NewVersionedApi("Orders")
            .MapGroup("/v{version:apiVersion}/orders")
            .HasApiVersion(new ApiVersion(1, 0))
            .RequireRateLimiting(RateLimitingExtensions.PerClientPolicy)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .WithTags("Orders");

#if (UseJwt)
        orders.MapPlaceOrder().RequireAuthorization(AuthPolicies.OrdersWrite);
        orders.MapGetOrder().RequireAuthorization(AuthPolicies.OrdersRead);
        orders.MapListOrders().RequireAuthorization(AuthPolicies.OrdersRead);
        orders.MapCancelOrder().RequireAuthorization(AuthPolicies.OrdersWrite);
#else
        orders.MapPlaceOrder();
        orders.MapGetOrder();
        orders.MapListOrders();
        orders.MapCancelOrder();
#endif
        return app;
    }
}
