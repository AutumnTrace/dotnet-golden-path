namespace GoldenPath.Templates.Tests;

/// <summary>Read-only view over an instantiated service, for assertions.</summary>
public sealed class GeneratedService(string root)
{
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".csproj", ".props", ".targets", ".slnx", ".json", ".md", ".yml", ".yaml", ".tpl", ".txt",
        ".editorconfig", ".gitignore", ".gitattributes", ".dockerignore", "",
    };

    public string Root { get; } = root;

    /// <summary>Paths relative to the root, with forward slashes.</summary>
    public IReadOnlyList<string> Files { get; } = [.. Directory
        .EnumerateFiles(root, "*", SearchOption.AllDirectories)
        .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
        .Order(StringComparer.Ordinal)];

    public IEnumerable<string> TextFiles => Files.Where(file => TextExtensions.Contains(Path.GetExtension(file)));

    public bool Exists(string relativePath) => Files.Contains(relativePath);

    public string Read(string relativePath) => File.ReadAllText(Path.Combine(Root, relativePath));

    /// <summary>Text files (relative paths) whose content contains <paramref name="text"/>.</summary>
    public IReadOnlyList<string> FilesContaining(string text) =>
        [.. TextFiles.Where(file => Read(file).Contains(text, StringComparison.Ordinal))];
}
