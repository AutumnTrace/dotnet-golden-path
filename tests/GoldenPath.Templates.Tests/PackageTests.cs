using System.Diagnostics;
using System.IO.Compression;

namespace GoldenPath.Templates.Tests;

/// <summary>
/// The instantiation tests install the template from its folder; users install the .nupkg. This checks the package
/// carries exactly the template folder, file for file, including dot-files and files without an extension.
/// </summary>
public sealed class PackageTests : IDisposable
{
    private readonly string _output = Path.Combine(Path.GetTempPath(), "goldenpath-pack", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task The_package_contains_exactly_the_template_files_at_the_same_paths()
    {
        var project = Path.Combine(TemplateFixture.RepositoryRoot, "src", "GoldenPath.Templates", "GoldenPath.Templates.csproj");
        await PackAsync(project);

        var package = Directory.GetFiles(_output, "GoldenPath.Templates.*.nupkg").Single();
        using var archive = ZipFile.OpenRead(package);
        var packaged = archive.Entries
            .Select(entry => entry.FullName)
            .Where(name => name.StartsWith("content/", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToList();

        var templates = Path.Combine(TemplateFixture.RepositoryRoot, "templates");
        var expected = Directory.EnumerateFiles(templates, "*", SearchOption.AllDirectories)
            .Select(path => "content/" + Path.GetRelativePath(templates, path).Replace('\\', '/'))
            .Where(name => !name.Contains("/bin/", StringComparison.Ordinal) && !name.Contains("/obj/", StringComparison.Ordinal))
            .Where(name => !name.Contains(".received.", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(expected, packaged);
        Assert.Contains("content/goldenpath-api/Dockerfile", packaged);
        Assert.Contains("content/goldenpath-api/.template.config/template.json", packaged);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_output, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

    private async Task PackAsync(string project)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in new[] { "pack", project, "-c", "Release", "-o", _output, "--nologo" })
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        Assert.True(process.ExitCode == 0, $"dotnet pack failed:\n{await stdout}\n{await stderr}");
    }
}
