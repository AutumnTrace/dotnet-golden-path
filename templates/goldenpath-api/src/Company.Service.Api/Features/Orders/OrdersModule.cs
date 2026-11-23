using Asp.Versioning;
#if (UsePostgres)
using Company.Service.Api.Features.Orders.Persistence;
using Company.Service.Api.Infrastructure.Persistence;
#endif
#if (UseKafka)
using Company.Service.Api.Infrastructure.Messaging;
#endif
using Company.Service.Api.Infrastructure.RateLimiting;
#if (UseJwt)
using Company.Service.Api.Infrastructure.Security;
#endif

namespace Company.Service.Api.Features.Orders;

/// <summary>The slice's front door: everything Orders needs is registered and mapped here.</summary>
public static class OrdersModule
{
    public static IServiceCollection AddOrdersFeature(this IServiceCollection services, IConfiguration configuration)
    {
#if (UsePostgres)
        if (DatabaseOptions.ProviderFrom(configuration) == DatabaseProvider.Postgres)
        {
            services.AddScoped<IOrderStore, EfOrderStore>();
        }
        else
        {
            services.AddSingleton<IOrderStore, InMemoryOrderStore>();
        }

#else
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddSingleton<IOrderStore, InMemoryOrderStore>();
#endif
        services.AddSingleton<OrdersMetrics>();
        services.AddScoped<PlaceOrderHandler>();
        services.AddScoped<CancelOrderHandler>();
#if (UseKafka)
        services.AddProducedTopic(OrderTopics.OrderEvents);
        services.AddMessageHandler<PaymentCaptured, PaymentCapturedHandler>(OrderTopics.PaymentEvents);
#endif
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
