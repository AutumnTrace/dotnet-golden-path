using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;

namespace Company.Service.Api.Integrations.CustomerDirectory;

public sealed class CustomerDirectoryOptions
{
    public const string SectionName = "CustomerDirectory";

    [Required]
    public Uri? BaseAddress { get; set; }

    /// <summary>
    /// Answer from an in-process stub instead of the network (local runs and demos only). The stub sits at the
    /// HTTP boundary, so the client and the whole resilience pipeline still run.
    /// </summary>
    public bool UseStub { get; set; }
}

public static class CustomerDirectoryExtensions
{
    public static IServiceCollection AddCustomerDirectory(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection(CustomerDirectoryOptions.SectionName);
        services.AddOptions<CustomerDirectoryOptions>()
            .Bind(section)
            .ValidateDataAnnotations()
            .Validate(options => options.BaseAddress?.IsAbsoluteUri == true, "CustomerDirectory:BaseAddress must be an absolute URI.")
            .ValidateOnStart();

        var client = services.AddHttpClient<ICustomerDirectory, CustomerDirectoryClient>((provider, http) =>
            http.BaseAddress = provider.GetRequiredService<IOptions<CustomerDirectoryOptions>>().Value.BaseAddress);

        // Standard pipeline, outermost first: rate limiter, total timeout, retry, circuit breaker, attempt timeout.
        // The numbers come from configuration (CustomerDirectory:Resilience); see the handbook for why these defaults.
        client.AddStandardResilienceHandler().Configure(section.GetSection("Resilience"));

        if (section.GetValue<bool>(nameof(CustomerDirectoryOptions.UseStub)))
        {
            client.ConfigurePrimaryHttpMessageHandler(() => new StubCustomerDirectoryHandler());
        }

        return services;
    }
}
