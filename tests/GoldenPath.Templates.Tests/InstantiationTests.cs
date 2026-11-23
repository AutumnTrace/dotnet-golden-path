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
    [MemberData(nameof(Combinations))]
    public async Task Persistence_code_exists_only_with_postgres(string arguments)
    {
        var options = Parse(arguments);
        var service = await GenerateAsync(arguments);
        var api = $"src/{options.ServiceName}.Api";

        Assert.Equal(options.UsesPostgres, service.Exists($"{api}/Infrastructure/Persistence/AppDbContext.cs"));
        Assert.Equal(options.UsesPostgres, service.Files.Any(f => f.StartsWith($"{api}/Infrastructure/Persistence/Migrations/", StringComparison.Ordinal)));
        Assert.Equal(options.UsesPostgres, service.Exists(".config/dotnet-tools.json"));
        Assert.Equal(options.UsesPostgres, service.FilesContaining("Npgsql").Count > 0);
        Assert.Equal(options.UsesPostgres, service.FilesContaining("\"ConnectionStrings\"").Count > 0);
    }

    [Theory]
    [MemberData(nameof(Combinations))]
    public async Task Messaging_code_exists_only_with_kafka(string arguments)
    {
        var options = Parse(arguments);
        var service = await GenerateAsync(arguments);
        var api = $"src/{options.ServiceName}.Api";

        Assert.Equal(options.UsesKafka, service.Exists($"{api}/Infrastructure/Messaging/KafkaConsumerService.cs"));
        Assert.Equal(options.UsesKafka, service.Exists($"{api}/Features/Orders/MarkOrderPaid.cs"));
        Assert.Equal(options.UsesKafka, service.FilesContaining("Confluent.Kafka").Count > 0);
        Assert.Equal(options.UsesKafka, service.FilesContaining("IMessagePublisher").Count > 0);
    }

    [Theory]
    [MemberData(nameof(Combinations))]
    public async Task Integration_tests_exist_exactly_when_there_is_infrastructure_and_are_all_tagged(string arguments)
    {
        var options = Parse(arguments);
        var service = await GenerateAsync(arguments);
        var project = $"tests/{options.ServiceName}.IntegrationTests";

        Assert.Equal(options.HasIntegrationTests, service.Exists($"{project}/{options.ServiceName}.IntegrationTests.csproj"));
        var testClasses = service.Files
            .Where(f => f.StartsWith(project, StringComparison.Ordinal) && f.EndsWith("Tests.cs", StringComparison.Ordinal))
            .ToList();
        Assert.Equal(options.HasIntegrationTests, testClasses.Count > 0);
        Assert.All(testClasses, file => Assert.Contains("[Trait(\"Category\", \"Integration\")]", service.Read(file), StringComparison.Ordinal));
        Assert.Equal(options.UsesPostgres, testClasses.Any(f => f.Contains("/Postgres/", StringComparison.Ordinal)));
        Assert.Equal(options.UsesKafka, testClasses.Any(f => f.Contains("/Kafka/", StringComparison.Ordinal)));
    }

    [Theory]
    [MemberData(nameof(Combinations))]
    public async Task Every_service_ships_architecture_tests_and_the_matching_api_contract_snapshot(string arguments)
    {
        var options = Parse(arguments);
        var service = await GenerateAsync(arguments);
        var snapshots = $"tests/{options.ServiceName}.ApiTests/Snapshots";

        Assert.True(service.Exists($"tests/{options.ServiceName}.ArchitectureTests/ArchitectureTests.cs"));
        Assert.Equal([$"{snapshots}/openapi-v1.approved.json"], service.Files.Where(f => f.StartsWith(snapshots, StringComparison.Ordinal)));
        Assert.Equal(options.UsesJwt, service.Read($"{snapshots}/openapi-v1.approved.json").Contains("\"securitySchemes\"", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Combinations))]
    public async Task The_ops_kit_is_named_after_the_service_and_matches_the_options(string arguments)
    {
        var options = Parse(arguments);
        var service = await GenerateAsync(arguments);
        var chart = Assert.Single(service.Files, f => f.StartsWith("deploy/helm/", StringComparison.Ordinal) && f.EndsWith("/Chart.yaml", StringComparison.Ordinal));
        var slug = chart.Split('/')[2];
        Assert.Matches("^[a-z0-9]+(-[a-z0-9]+)*$", slug);
        var compose = service.Read("docker-compose.yml");
        var values = service.Read($"deploy/helm/{slug}/values.yaml");

        Assert.True(service.Exists("Dockerfile"));
        Assert.True(service.Exists(".github/workflows/ci.yml"));
        Assert.Contains($"name: {slug}", service.Read(chart), StringComparison.Ordinal);
        Assert.Contains($"name: {slug}", compose, StringComparison.Ordinal);
        Assert.Contains($"ENTRYPOINT [\"dotnet\", \"{options.ServiceName}.Api.dll\"]", service.Read("Dockerfile"), StringComparison.Ordinal);

        Assert.Equal(options.UsesPostgres, compose.Contains("  postgres:", StringComparison.Ordinal));
        Assert.Equal(options.UsesKafka, compose.Contains("  kafka:", StringComparison.Ordinal));
        Assert.Equal(options.UsesOtlp, compose.Contains("  aspire-dashboard:", StringComparison.Ordinal));
        Assert.Equal(options.UsesJwt, compose.Contains("DevelopmentTokens", StringComparison.Ordinal));
        Assert.Equal(options.UsesPostgres, values.Contains("Database__Provider", StringComparison.Ordinal));
        Assert.Equal(options.UsesKafka, values.Contains("Messaging__BootstrapServers", StringComparison.Ordinal));
        Assert.Equal(options.HasIntegrationTests, service.Read(".github/workflows/ci.yml").Contains("Category=Integration", StringComparison.Ordinal));
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
        Assert.True(service.Exists($"deploy/helm/{slug}/values.yaml"));
        Assert.Contains($"name: {slug}", service.Read("docker-compose.yml"), StringComparison.Ordinal);
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

    /// <summary>Each combination is generated once per test run and shared by every check on it.</summary>
    private async Task<GeneratedService> GenerateAsync(string arguments)
    {
        var options = Parse(arguments);
        return new GeneratedService(await template.InstantiateOnceAsync(options.ServiceName, options.ToArguments(), Ct));
    }
}
