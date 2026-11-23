using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Company.Service.ApiTests;

/// <summary>
/// Approval testing in 40 lines. The approved file lives in <c>Snapshots/</c> next to the test and is reviewed like
/// code. On a mismatch the test writes <c>*.received.json</c> beside it and fails; to accept an intended change,
/// run the tests with <c>UPDATE_SNAPSHOTS=1</c> (or copy the received file over the approved one) and commit.
/// </summary>
internal static class Snapshot
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public static void MatchJson(string json, string name, [CallerFilePath] string testFile = "")
    {
        var actual = Normalize(json);
        var directory = Path.Combine(Path.GetDirectoryName(testFile)!, "Snapshots");
        var approvedPath = Path.Combine(directory, $"{name}.approved.json");
        var receivedPath = Path.Combine(directory, $"{name}.received.json");

        if (Environment.GetEnvironmentVariable("UPDATE_SNAPSHOTS") == "1")
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(approvedPath, actual);
            File.Delete(receivedPath);
            return;
        }

        var approved = File.Exists(approvedPath) ? Normalize(File.ReadAllText(approvedPath)) : null;
        if (approved == actual)
        {
            File.Delete(receivedPath);
            return;
        }

        Directory.CreateDirectory(directory);
        File.WriteAllText(receivedPath, actual);
        Assert.Fail(approved is null
            ? $"No approved snapshot yet. Review {receivedPath} and rerun with UPDATE_SNAPSHOTS=1 to approve it."
            : $"{name} changed. First difference: {FirstDifference(approved, actual)}. Review {receivedPath}; if the change is intended, rerun with UPDATE_SNAPSHOTS=1.");
    }

    /// <summary>Stable formatting and line endings, so only real changes count.</summary>
    private static string Normalize(string json) =>
        JsonNode.Parse(json)!.ToJsonString(Indented).ReplaceLineEndings("\n") + "\n";

    private static string FirstDifference(string approved, string actual)
    {
        var expectedLines = approved.Split('\n');
        var actualLines = actual.Split('\n');
        for (var i = 0; i < Math.Max(expectedLines.Length, actualLines.Length); i++)
        {
            var expected = i < expectedLines.Length ? expectedLines[i] : "<end>";
            var got = i < actualLines.Length ? actualLines[i] : "<end>";
            if (expected != got)
            {
                return $"line {i + 1}: expected `{expected.Trim()}`, got `{got.Trim()}`";
            }
        }

        return "none";
    }
}
