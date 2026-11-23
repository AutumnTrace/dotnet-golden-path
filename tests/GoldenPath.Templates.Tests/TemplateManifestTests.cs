using System.Text.Json;

namespace GoldenPath.Templates.Tests;

/// <summary>Guards against drift between template.json, the tests and the README.</summary>
public class TemplateManifestTests
{
    private static readonly JsonElement Manifest = JsonDocument
        .Parse(File.ReadAllText(Path.Combine(TemplateFixture.TemplateDirectory, ".template.config", "template.json")))
        .RootElement;

    [Fact]
    public void The_template_is_invoked_as_goldenpath_api()
    {
        Assert.Equal("goldenpath-api", Manifest.GetProperty("shortName").GetString());
        Assert.Equal("Company.Service", Manifest.GetProperty("sourceName").GetString());
    }

    [Fact]
    public void The_tests_cover_every_choice_the_template_offers()
    {
        var offered = ChoiceParameters().ToDictionary(p => p.Name, p => p.Choices.Order(StringComparer.Ordinal).ToArray());

        Assert.Equal(offered.Keys.Order(StringComparer.Ordinal), TemplateOptions.Choices.Keys.Order(StringComparer.Ordinal));
        foreach (var (name, choices) in offered)
        {
            Assert.Equal(choices, TemplateOptions.Choices[name].Order(StringComparer.Ordinal));
        }
    }

    [Fact]
    public void Every_option_and_choice_has_a_description()
    {
        foreach (var (name, symbol) in ChoiceParameters().Select(p => (p.Name, p.Symbol)))
        {
            Assert.False(string.IsNullOrWhiteSpace(symbol.GetProperty("description").GetString()), name);
            Assert.All(symbol.GetProperty("choices").EnumerateArray(), choice =>
                Assert.False(string.IsNullOrWhiteSpace(choice.GetProperty("description").GetString()), $"{name}={choice}"));
        }
    }

    [Fact]
    public void The_readme_documents_every_option_and_choice()
    {
        var readme = File.ReadAllText(Path.Combine(TemplateFixture.RepositoryRoot, "README.md"));

        foreach (var (name, choices) in ChoiceParameters().Select(p => (p.Name, p.Choices)))
        {
            Assert.Contains($"`--{name}`", readme, StringComparison.Ordinal);
            Assert.All(choices, choice => Assert.Contains($"`{choice}`", readme, StringComparison.Ordinal));
        }
    }

    private static IEnumerable<(string Name, JsonElement Symbol, string[] Choices)> ChoiceParameters() =>
        Manifest.GetProperty("symbols").EnumerateObject()
            .Where(symbol => symbol.Value.TryGetProperty("datatype", out var type) && type.GetString() == "choice")
            .Select(symbol => (
                symbol.Name,
                symbol.Value,
                symbol.Value.GetProperty("choices").EnumerateArray().Select(c => c.GetProperty("choice").GetString()!).ToArray()));
}
