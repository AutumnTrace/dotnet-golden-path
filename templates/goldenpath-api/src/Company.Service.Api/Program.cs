using Company.Service.Api.Features.Orders;
using Company.Service.Api.Infrastructure.Api;
using Company.Service.Api.Infrastructure.Health;
using Company.Service.Api.Infrastructure.Observability;
using Serilog;
using Serilog.Formatting.Compact;

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.AddObservability();

    builder.Services.AddSingleton(TimeProvider.System);
    builder.Services.AddApiDefaults();
    builder.Services.AddHealthDefaults();

    // Feature slices: one line each.
    builder.Services.AddOrdersFeature();

    var app = builder.Build();

    // Request logging wraps everything so it records the final status code, after error handling.
    app.UseRequestLogging();
    app.UseExceptionHandler();
    app.UseStatusCodePages();

    app.MapHealthEndpoints();
    app.MapApiDocuments();
    app.MapOrdersEndpoints();

    await app.RunAsync();
    return 0;
}
catch (Exception exception) when (exception is not HostAbortedException)
{
    // Startup failures (invalid options, unreachable config) still leave one structured log line behind.
    await using var fatal = new LoggerConfiguration().WriteTo.Console(new RenderedCompactJsonFormatter()).CreateLogger();
    fatal.Fatal(exception, "Service terminated unexpectedly");
    return 1;
}

/// <summary>Entry point; public so the API tests can host it with <c>WebApplicationFactory</c>.</summary>
public partial class Program;
