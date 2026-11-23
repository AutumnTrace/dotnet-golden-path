using Company.Service.Testing;
using Microsoft.AspNetCore.Hosting;
#if (UseJwt)
using Microsoft.AspNetCore.Mvc.Testing;
#endif
using Microsoft.Extensions.Options;

namespace Company.Service.ApiTests;

/// <summary>Bad configuration must stop the service at startup, not surface on the first unlucky request.</summary>
public class StartupValidationTests
{
    [Theory]
    [InlineData("CustomerDirectory:BaseAddress", "", "BaseAddress")]
    [InlineData("RateLimiting:PermitLimit", "0", "PermitLimit")]
    [InlineData("Shutdown:DrainDelay", "00:00:30", "Shutdown:DrainDelay")]
    [InlineData("Observability:TraceSampleRatio", "1.5", "TraceSampleRatio")]
    public void Invalid_settings_fail_fast_with_a_message_that_names_the_setting(string key, string value, string expected)
    {
        using var factory = new MisconfiguredFactory(key, value);

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        var validation = Assert.IsType<OptionsValidationException>(Unwrap(exception));
        Assert.Contains(expected, validation.Message, StringComparison.Ordinal);
    }

#if (UseJwt)
    [Fact]
    public void Production_refuses_to_start_without_a_way_to_validate_tokens()
    {
        using var factory = new NoSigningKeyFactory("Production");

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        var validation = Assert.IsType<OptionsValidationException>(Unwrap(exception));
        Assert.Contains("Authentication:Schemes:Bearer", validation.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Development_starts_without_keys_so_a_fresh_clone_runs()
    {
        using var factory = new NoSigningKeyFactory("Development");
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);

        Assert.True(response.IsSuccessStatusCode);
    }

#endif
    private static Exception Unwrap(Exception exception) =>
        exception is AggregateException { InnerExceptions.Count: 1 } aggregate ? Unwrap(aggregate.InnerExceptions[0]) : exception;

    private sealed class MisconfiguredFactory(string key, string value) : ServiceFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting(key, value);
        }
    }
#if (UseJwt)

    /// <summary>The real app with no test signing key, as it would start on a fresh machine.</summary>
    private sealed class NoSigningKeyFactory(string environment) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(environment);
            builder.UseSetting("Serilog:MinimumLevel:Default", "Warning");
            builder.UseSetting("CustomerDirectory:UseStub", "true");
            builder.UseSetting("Shutdown:DrainDelay", "00:00:00");
#if (UsePostgres)
            builder.UseSetting("Database:Provider", "InMemory");
#endif
#if (UseKafka)
            builder.UseSetting("Messaging:Provider", "InMemory");
#endif
        }
    }
#endif
}
