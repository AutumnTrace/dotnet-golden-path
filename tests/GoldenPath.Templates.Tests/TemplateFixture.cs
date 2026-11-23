using System.Diagnostics;

namespace GoldenPath.Templates.Tests;

/// <summary>
/// Installs the template from the repository folder into a throwaway hive, so tests never touch the
/// machine's real <c>dotnet new</c> configuration, and instantiates it into a scratch directory.
/// </summary>
public sealed class TemplateFixture : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "goldenpath-tests", Guid.NewGuid().ToString("N"));

    public static string RepositoryRoot { get; } = FindRepositoryRoot();

    public static string TemplateDirectory { get; } = Path.Combine(RepositoryRoot, "templates", "goldenpath-api");

    private string Hive => Path.Combine(_root, "hive");

    public async ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(_root);
        await DotnetAsync(["new", "install", TemplateDirectory, "--debug:custom-hive", Hive], _root, CancellationToken.None);
    }

    public async Task<string> InstantiateAsync(string name, IEnumerable<string> arguments, CancellationToken cancellationToken)
    {
        var output = Path.Combine(_root, "out", $"{name}-{Guid.NewGuid():N}"[..(name.Length + 9)]);
        await DotnetAsync(
            ["new", "goldenpath-api", "--debug:custom-hive", Hive, "-n", name, "-o", output, .. arguments],
            _root,
            cancellationToken);
        return output;
    }

    public ValueTask DisposeAsync()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Best effort: a virus scanner or indexer may still hold a file.
        }
        catch (UnauthorizedAccessException)
        {
        }

        return ValueTask.CompletedTask;
    }

    private static async Task DotnetAsync(IEnumerable<string> arguments, string workingDirectory, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        start.Environment["DOTNET_NOLOGO"] = "1";
        start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";

        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start dotnet.");
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"dotnet {string.Join(' ', start.ArgumentList)} failed with exit code {process.ExitCode}:\n{await stdout}\n{await stderr}");
        }
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GoldenPath.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Could not find GoldenPath.slnx above the test output directory.");
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class TemplateCollection : ICollectionFixture<TemplateFixture>
{
    public const string Name = "template";
}
