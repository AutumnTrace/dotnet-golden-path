using Company.Service.Api.Features.Orders;
using Company.Service.Api.Infrastructure.Api;
using Company.Service.Api.Infrastructure.Health;
using Company.Service.Api.Infrastructure.Lifecycle;
#if (UseKafka)
using Company.Service.Api.Infrastructure.Messaging;
#endif
using Company.Service.Api.Infrastructure.Observability;
#if (UsePostgres)
using Company.Service.Api.Infrastructure.Persistence;
#endif
using Company.Service.Api.Infrastructure.RateLimiting;
#if (UseJwt)
using Company.Service.Api.Infrastructure.Security;
#endif
using Company.Service.Api.Integrations.CustomerDirectory;
using Serilog;
using Serilog.Formatting.Compact;

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.AddObservability();

    // Platform: the same in every service on the golden path.
    builder.Services.AddSingleton(TimeProvider.System);
    builder.Services.AddApiDefaults();
    builder.Services.AddHealthDefaults();
    builder.Services.AddGracefulShutdown();
    builder.Services.AddRateLimitingDefaults();
#if (UseJwt)
    builder.Services.AddJwtAuthentication();
#endif

#if (UsePostgres)
    builder.Services.AddPersistence(builder.Configuration);
#endif
#if (UseKafka)
    builder.Services.AddMessaging(builder.Configuration);
#endif

    // Downstream services this one calls.
    builder.Services.AddCustomerDirectory(builder.Configuration);

    // Feature slices: one line each.
    builder.Services.AddOrdersFeature(builder.Configuration);

    var app = builder.Build();

    // Request logging wraps everything so it records the final status code, after error handling.
    app.UseRequestLogging();
    app.UseExceptionHandler();
    app.UseStatusCodePages();
#if (UseJwt)
    app.UseAuthentication();
    app.UseAuthorization();
#endif
    app.UseRateLimiter();

    app.MapHealthEndpoints();
    app.MapApiDocuments();
#if (UseJwt)
    app.MapDevelopmentTokens();
#endif
    app.MapOrdersEndpoints();

    await app.RunAsync();
}
catch (Exception exception) when (exception is not HostAbortedException)
{
    // Startup failures (invalid options, bad configuration) leave one structured log line behind,
    // then the process still fails with a non-zero exit code so the orchestrator notices.
    await using (var fatal = new LoggerConfiguration().WriteTo.Console(new RenderedCompactJsonFormatter()).CreateLogger())
    {
        fatal.Fatal(exception, "Service terminated unexpectedly");
    }

    throw;
}

/// <summary>Entry point; public so the API tests can host it with <c>WebApplicationFactory</c>.</summary>
public partial class Program;
