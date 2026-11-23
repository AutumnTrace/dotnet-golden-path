using ArchUnitNET.Domain;
using ArchUnitNET.Fluent;
using ArchUnitNET.Loader;
using ArchUnitNET.xUnitV3;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace Company.Service.ArchitectureTests;

/// <summary>
/// The structure the handbook describes, enforced on every build. When one of these fails, the fix is
/// usually to move code, not to change the rule; if the rule is wrong, change it in a PR with an ADR.
/// </summary>
public class ArchitectureTests
{
    private const string Root = "Company.Service.Api";
    private const string Features = $"{Root}.Features";
    private const string Infrastructure = $"{Root}.Infrastructure";
    private const string Integrations = $"{Root}.Integrations";

    private static readonly Architecture Architecture =
        new ArchLoader().LoadAssemblies(typeof(Program).Assembly).Build();

    private static IEnumerable<IType> ServiceTypes =>
        Architecture.Types.Where(type => IsIn(type.Namespace.FullName, Root));

    [Fact]
    public void Feature_slices_do_not_depend_on_each_other()
    {
        var slices = ServiceTypes.Select(type => SliceOf(type.Namespace.FullName)).OfType<string>().Distinct().ToList();
        Assert.NotEmpty(slices);

        var violations = ServiceTypes
            .Where(type => SliceOf(type.Namespace.FullName) is not null)
            .SelectMany(type => type.Dependencies
                .Select(dependency => (From: type, To: dependency.Target))
                .Where(pair => SliceOf(pair.To.Namespace.FullName) is { } target && target != SliceOf(pair.From.Namespace.FullName)))
            .Select(pair => $"{pair.From.FullName} -> {pair.To.FullName}")
            .Distinct()
            .ToList();

        Assert.True(violations.Count == 0, "Slices must talk through messages or Infrastructure, not each other:\n" + string.Join("\n", violations));
    }

    [Fact]
    public void Infrastructure_knows_nothing_about_features_or_integrations() =>
        Types().That().ResideInNamespaceOrBelow(Infrastructure)
            .Should().NotDependOnAnyTypesThat().ResideInNamespaceOrBelow(Features)
            .AndShould().NotDependOnAnyTypesThat().ResideInNamespaceOrBelow(Integrations)
            .Because("shared building blocks must stay reusable by every slice")
            .Check(Architecture);

    [Fact]
    public void Integrations_with_other_services_know_nothing_about_features() =>
        Types().That().ResideInNamespaceOrBelow(Integrations)
            .Should().NotDependOnAnyTypesThat().ResideInNamespaceOrBelow(Features)
            .Because("a downstream client describes the other service, not how a slice uses it")
            .Check(Architecture);

    [Fact]
    public void Domain_models_depend_only_on_the_base_library_and_the_error_types()
    {
        var domainTypes = ServiceTypes.Where(type => IsDomain(type.Namespace.FullName)).ToList();
        Assert.NotEmpty(domainTypes);

        var violations = domainTypes
            .SelectMany(type => type.Dependencies
                .Select(dependency => dependency.Target.Namespace.FullName)
                .Where(ns => !IsDomain(ns) && ns != $"{Infrastructure}.Errors" && !IsIn(ns, "System"))
                .Select(ns => $"{type.FullName} -> {ns}"))
            .Distinct()
            .ToList();

        Assert.True(violations.Count == 0, "Domain code must not know about HTTP, databases or brokers:\n" + string.Join("\n", violations));
    }

    [Fact]
    public void Handlers_and_endpoints_are_internal_details_of_their_slice() =>
        Classes().That().ResideInNamespaceOrBelow(Features)
            .And().HaveNameEndingWith("Handler").Or().HaveNameEndingWith("Endpoint")
            .Should().NotBePublic()
            .Check(Architecture);
#if (UsePostgres)

    [Fact]
    public void Only_persistence_code_touches_EF_Core() =>
        AssertOnlyUsedFrom("Microsoft.EntityFrameworkCore", "*.Persistence namespaces", ns =>
            IsIn(ns, $"{Infrastructure}.Persistence") || (IsIn(ns, Features) && ns.EndsWith(".Persistence", StringComparison.Ordinal)));
#endif
#if (UseKafka)

    [Fact]
    public void Only_the_messaging_infrastructure_touches_the_Kafka_client() =>
        AssertOnlyUsedFrom("Confluent.Kafka", "Infrastructure.Messaging", ns => ns == $"{Infrastructure}.Messaging");
#endif

    [Fact]
    public void Only_the_customer_directory_integration_makes_outbound_http_calls() =>
        AssertOnlyUsedFrom("System.Net.Http", "Integrations.CustomerDirectory", ns => ns == $"{Integrations}.CustomerDirectory");

    /// <summary>
    /// "Only these namespaces may use that library", with a positive control: if nothing used the library at all,
    /// the rule would pass without checking anything, so the test also requires at least one legitimate user.
    /// </summary>
    private static void AssertOnlyUsedFrom(string library, string allowedDescription, Func<string, bool> allowed)
    {
        var users = ServiceTypes
            .Where(type => type.Dependencies.Any(dependency => IsIn(dependency.Target.Namespace.FullName, library)))
            .ToList();

        Assert.True(users.Count > 0, $"No type uses {library}, so this rule would pass vacuously.");
        var offenders = users.Where(type => !allowed(type.Namespace.FullName)).Select(type => type.FullName).ToList();
        Assert.True(offenders.Count == 0, $"Only {allowedDescription} may use {library}, but these do: {string.Join(", ", offenders)}");
    }

    /// <summary>The slice a namespace belongs to (<c>Orders</c> for <c>...Features.Orders.Domain</c>), or null.</summary>
    private static string? SliceOf(string ns) =>
        IsIn(ns, Features) && ns.Length > Features.Length ? ns[(Features.Length + 1)..].Split('.')[0] : null;

    private static bool IsDomain(string ns) =>
        SliceOf(ns) is { } slice && ns == $"{Features}.{slice}.Domain";

    private static bool IsIn(string ns, string parent) =>
        ns == parent || ns.StartsWith(parent + ".", StringComparison.Ordinal);
}
