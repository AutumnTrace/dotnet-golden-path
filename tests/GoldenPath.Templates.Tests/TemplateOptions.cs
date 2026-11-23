namespace GoldenPath.Templates.Tests;

/// <summary>One combination of template options, as passed to <c>dotnet new goldenpath-api</c>.</summary>
public sealed record TemplateOptions(string Database, string Messaging, string Observability, string Auth)
{
    /// <summary>Every choice of every option. <see cref="TemplateManifestTests"/> keeps this in sync with template.json.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> Choices = new Dictionary<string, string[]>
    {
        ["database"] = ["postgres", "none"],
        ["messaging"] = ["kafka", "none"],
        ["observability"] = ["otlp", "appinsights", "both"],
        ["auth"] = ["jwt", "none"],
    };

    public bool UsesPostgres => Database == "postgres";

    public bool UsesKafka => Messaging == "kafka";

    public bool UsesOtlp => Observability is "otlp" or "both";

    public bool UsesAppInsights => Observability is "appinsights" or "both";

    public bool UsesJwt => Auth == "jwt";

    public bool HasIntegrationTests => UsesPostgres || UsesKafka;

    /// <summary>The full cartesian product: the template promises to compile in every one of these.</summary>
    public static IEnumerable<TemplateOptions> All() =>
        from database in Choices["database"]
        from messaging in Choices["messaging"]
        from observability in Choices["observability"]
        from auth in Choices["auth"]
        select new TemplateOptions(database, messaging, observability, auth);

    public IEnumerable<string> ToArguments() =>
        ["--database", Database, "--messaging", Messaging, "--observability", Observability, "--auth", Auth];

    /// <summary>A short, file-system-safe service name that encodes the combination, e.g. <c>SvcPkOj</c>.</summary>
    public string ServiceName => $"Svc{Initial(Database)}{Initial(Messaging)}{Initial(Observability)}{Initial(Auth)}";

    public override string ToString() => string.Join(' ', ToArguments());

    private static string Initial(string value) => value switch
    {
        "none" => "x",
        "appinsights" => "A",
        "both" => "B",
        _ => char.ToUpperInvariant(value[0]).ToString(),
    };
}
