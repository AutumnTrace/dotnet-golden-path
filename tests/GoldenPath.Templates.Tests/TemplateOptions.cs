namespace GoldenPath.Templates.Tests;

/// <summary>One combination of template options, as passed to <c>dotnet new goldenpath-api</c>.</summary>
public sealed record TemplateOptions(string Observability)
{
    /// <summary>Every choice of every option. <see cref="TemplateManifestTests"/> keeps this in sync with template.json.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> Choices = new Dictionary<string, string[]>
    {
        ["observability"] = ["otlp", "appinsights", "both"],
    };

    public bool UsesOtlp => Observability is "otlp" or "both";

    public bool UsesAppInsights => Observability is "appinsights" or "both";

    /// <summary>The full cartesian product: the template promises to compile in every one of these.</summary>
    public static IEnumerable<TemplateOptions> All() =>
        from observability in Choices["observability"]
        select new TemplateOptions(observability);

    public IEnumerable<string> ToArguments() => ["--observability", Observability];

    /// <summary>A short, file-system-safe service name that encodes the combination.</summary>
    public string ServiceName => $"Svc{Capitalize(Observability)}";

    public override string ToString() => string.Join(' ', ToArguments());

    private static string Capitalize(string value) => char.ToUpperInvariant(value[0]) + value[1..];
}
