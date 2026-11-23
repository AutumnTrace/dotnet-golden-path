using System.Text.Json;
using System.Xml.Linq;

namespace GoldenPath.Templates.Tests;

/// <summary>
/// Fast checks on the generated files for every option combination. Building and testing each
/// combination happens in CI (see .github/workflows/ci.yml); these tests catch template mistakes in seconds.
/// </summary>
[Collection(TemplateCollection.Name)]
public class InstantiationTests(TemplateFixture template)
{
    public static TheoryData<string> Combinations() => [.. TemplateOptions.All().Select(options => options.ToString())];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [MemberData(nameof(Combinations))]
    public async Task No_template_markers_or_placeholder_names_survive(string arguments)
    {
        var service = await GenerateAsync(arguments);

        foreach (var marker in new[] { "Company.Service", "company-service", "#if (", "#endif", "<!--#", "//#" })
        {
            Assert.True(service.FilesContaining(marker).Count == 0, $"'{marker}' left in: {string.Join(", ", service.FilesContaining(marker))}");
        }

        Assert.DoesNotContain(service.Files, file => file.Contains("Company.Service", StringComparison.Ordinal));
        Assert.DoesNotContain(service.Files, file => file.StartsWith(".template.config", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Combinations))]
    public async Task Every_json_file_is_valid_json(string arguments)
    {
        var service = await GenerateAsync(arguments);

        foreach (var file in service.Files.Where(f => f.EndsWith(".json", StringComparison.Ordinal)))
        {
            var exception = Record.Exception(() => JsonDocument.Parse(service.Read(file)));
            Assert.True(exception is null, $"{file}: {exception?.Message}");
        }
    }

    [Theory]
    [MemberData(nameof(Combinations))]
    public async Task The_solution_lists_exactly_the_generated_projects(string arguments)
    {
        var options = Parse(arguments);
        var service = await GenerateAsync(arguments);

        var solution = XDocument.Parse(service.Read($"{options.ServiceName}.slnx"));
        var listed = solution.Descendants("Project").Select(p => (string)p.Attribute("Path")!).Order(StringComparer.Ordinal);
        var projects = service.Files.Where(f => f.EndsWith(".csproj", StringComparison.Ordinal)).Order(StringComparer.Ordinal);

        Assert.Equal(projects, listed);
    }

    [Theory]
    [MemberData(nameof(Combinations))]
    public async Task Exporter_code_matches_the_observability_option(string arguments)
    {
        var options = Parse(arguments);
        var service = await GenerateAsync(arguments);

        Assert.Equal(options.UsesOtlp, service.FilesContaining("UseOtlpExporter").Count > 0);
        Assert.Equal(options.UsesOtlp, service.FilesContaining("OpenTelemetry.Exporter.OpenTelemetryProtocol").Count > 0);
        Assert.Equal(options.UsesAppInsights, service.FilesContaining("UseAzureMonitorExporter").Count > 0);
        Assert.Equal(options.UsesAppInsights, service.FilesContaining("Azure.Monitor.OpenTelemetry.Exporter").Count > 0);
    }

    [Theory]
    [MemberData(nameof(Combinations))]
    public async Task Auth_code_exists_only_with_the_jwt_option(string arguments)
    {
        var options = Parse(arguments);
        var service = await GenerateAsync(arguments);

        Assert.Equal(options.UsesJwt, service.Exists($"src/{options.ServiceName}.Api/Infrastructure/Security/AuthExtensions.cs"));
        Assert.Equal(options.UsesJwt, service.FilesContaining("Microsoft.AspNetCore.Authentication.JwtBearer").Count > 0);
        Assert.Equal(options.UsesJwt, service.FilesContaining("RequireAuthorization").Count > 0);
        Assert.Equal(options.UsesJwt, service.FilesContaining("\"Authentication\"").Count > 0);
    }

    [Theory]
    [InlineData("Orders", "Orders.Api", "orders")]
    [InlineData("Wallet.Gateway", "Wallet.Gateway.Api", "wallet-gateway")]
    [InlineData("PaymentRouting", "PaymentRouting.Api", "payment-routing")]
    public async Task The_service_name_flows_into_projects_namespaces_and_the_kebab_case_slug(string name, string project, string slug)
    {
        var service = new GeneratedService(await template.InstantiateAsync(name, [], Ct));

        Assert.True(service.Exists($"src/{project}/{project}.csproj"));
        Assert.Contains($"namespace {project}.Features.Orders;", service.Read($"src/{project}/Features/Orders/OrdersModule.cs"), StringComparison.Ordinal);
        Assert.Contains($"DefaultServiceName = \"{slug}\"", service.Read($"src/{project}/Infrastructure/Observability/Telemetry.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Each_instantiation_gets_its_own_user_secrets_id()
    {
        var first = new GeneratedService(await template.InstantiateAsync("Orders", [], Ct));
        var second = new GeneratedService(await template.InstantiateAsync("Orders", [], Ct));

        static string SecretsId(GeneratedService service) =>
            XDocument.Parse(service.Read("src/Orders.Api/Orders.Api.csproj")).Descendants("UserSecretsId").Single().Value;

        Assert.NotEqual(SecretsId(first), SecretsId(second));
        Assert.NotEqual("6f0d7c1e-3b9a-4c55-9e2f-1a8b7d4c2e90", SecretsId(first));
    }

    private static TemplateOptions Parse(string arguments) =>
        TemplateOptions.All().Single(options => options.ToString() == arguments);

    private async Task<GeneratedService> GenerateAsync(string arguments)
    {
        var options = Parse(arguments);
        return new GeneratedService(await template.InstantiateAsync(options.ServiceName, options.ToArguments(), Ct));
    }
}
