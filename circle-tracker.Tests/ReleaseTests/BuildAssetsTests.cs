using FluentAssertions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace CircleTracker.Tests.ReleaseTests;

public class BuildAssetsTests
{
    private static readonly string[] ExpectedShippedAssets = ["assets/sectionpass.wav", "assets/ct.ico"];
    private static readonly string[] ExpectedRids = ["linux-x64", "win-x64", "osx-x64", "osx-arm64"];
    private const string PreviewAsset = "assets/circletrackerlazer.png";

    private static string FindRepositoryRoot()
    {
        string? dir = AppContext.BaseDirectory;

        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir, "circle-tracker.csproj")))
                return dir;

            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException("Repository root not found");
    }

    private static XDocument LoadCsproj(string root)
    {
        return XDocument.Load(Path.Combine(root, "circle-tracker.csproj"));
    }

    private static HashSet<string> ReadCsprojShippedAssets(string root)
    {
        return new HashSet<string>(LoadCsproj(root)
            .Descendants("None")
            .Where(e => (e.Attribute("Update")?.Value ?? "").StartsWith("assets/", StringComparison.Ordinal)
                && e.Element("CopyToOutputDirectory")?.Value == "PreserveNewest")
            .Select(e => e.Attribute("Update")!.Value.Replace('\\', '/')),
            StringComparer.Ordinal);
    }

    private static string ReadCsprojCopySetting(string root, string asset)
    {
        return LoadCsproj(root)
            .Descendants("None")
            .Where(e => e.Attribute("Update")?.Value == asset)
            .Select(e => e.Element("CopyToOutputDirectory")?.Value ?? "")
            .FirstOrDefault() ?? "";
    }

    private static string ReadCsprojApplicationIcon(string root)
    {
        return LoadCsproj(root).Descendants("ApplicationIcon").Select(e => e.Value).FirstOrDefault() ?? "";
    }

    private static HashSet<string> ReadShellShippedAssets(string root)
    {
        string text = File.ReadAllText(Path.Combine(root, "build-release.sh"));

        return new HashSet<string>(text.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.StartsWith("test -f \"$dir/assets/", StringComparison.Ordinal))
            .Select(l => Regex.Match(l, "test -f\\s+\"([^\"]+)\"").Groups[1].Value.Replace("$dir/", ""))
            .Select(p => p.Replace('\\', '/')),
            StringComparer.Ordinal);
    }

    private static HashSet<string> ReadWorkflowShippedAssets(string root)
    {
        string text = File.ReadAllText(Path.Combine(root, ".github", "workflows", "release.yml"));

        return new HashSet<string>(text.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.StartsWith("test -f ./publish/", StringComparison.Ordinal) && l.Contains("/assets/", StringComparison.Ordinal))
            .Select(l => l.Replace("test -f ", "").Trim()),
            StringComparer.Ordinal);
    }

    private static HashSet<string> ReadShellRids(string root)
    {
        string text = File.ReadAllText(Path.Combine(root, "build-release.sh"));

        return new HashSet<string>(text.Split('\n')
            .Where(l => l.Contains("dotnet publish", StringComparison.Ordinal))
            .Select(l => Regex.Match(l, @"-r\s+([A-Za-z0-9-]+)").Groups[1].Value)
            .Where(r => r.Length > 0),
            StringComparer.Ordinal);
    }

    private static HashSet<string> ReadWorkflowRids(string root)
    {
        string text = File.ReadAllText(Path.Combine(root, ".github", "workflows", "release.yml"));

        return new HashSet<string>(text.Split('\n')
            .Where(l => l.Contains("dotnet publish", StringComparison.Ordinal))
            .Select(l => Regex.Match(l, @"-r\s+([A-Za-z0-9-]+)").Groups[1].Value)
            .Where(r => r.Length > 0),
            StringComparer.Ordinal);
    }

    private static List<string> FindMissingPreviewExclusions(string root)
    {
        var missing = new List<string>();

        if (ReadCsprojCopySetting(root, PreviewAsset) != "Never")
            missing.Add("csproj");

        string shell = File.ReadAllText(Path.Combine(root, "build-release.sh"));

        if (!shell.Contains("test ! -f \"$dir/assets/circletrackerlazer.png\"", StringComparison.Ordinal))
            missing.Add("build-release.sh");

        string workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "release.yml"));

        foreach (string rid in ExpectedRids)
        {
            if (!workflow.Contains($"test ! -f ./publish/{rid}/assets/circletrackerlazer.png", StringComparison.Ordinal))
                missing.Add($"release.yml:{rid}");
        }

        return missing;
    }

    [Fact]
    public void BuildAssets_WhenCsprojRead_ShipsExpectedAssetSet()
    {
        string root = FindRepositoryRoot();

        var shipped = ReadCsprojShippedAssets(root);

        shipped.Should().BeEquivalentTo(ExpectedShippedAssets);
    }

    [Fact]
    public void BuildAssets_WhenShellScriptRead_MatchesExpectedAssetSet()
    {
        string root = FindRepositoryRoot();

        var shipped = ReadShellShippedAssets(root);

        shipped.Should().BeEquivalentTo(ExpectedShippedAssets);
    }

    [Fact]
    public void BuildAssets_WhenWorkflowRead_MatchesExpectedAssetSet()
    {
        string root = FindRepositoryRoot();
        var expected = new HashSet<string>(ExpectedRids
            .SelectMany(rid => ExpectedShippedAssets.Select(a => $"./publish/{rid}/{a}")),
            StringComparer.Ordinal);

        var shipped = ReadWorkflowShippedAssets(root);

        shipped.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void BuildAssets_WhenPreviewExclusionsRead_HasNoGaps()
    {
        string root = FindRepositoryRoot();

        var missing = FindMissingPreviewExclusions(root);

        missing.Should().BeEmpty();
    }

    [Fact]
    public void BuildAssets_WhenIconReferenceRead_PointsToShippedAsset()
    {
        string root = FindRepositoryRoot();

        string icon = ReadCsprojApplicationIcon(root);

        icon.Should().Be("assets/ct.ico");
    }

    [Fact]
    public void BuildAssets_WhenRootChecked_HasNoDuplicateIcon()
    {
        string root = FindRepositoryRoot();

        File.Exists(Path.Combine(root, "ct.ico")).Should().BeFalse();
    }

    [Fact]
    public void BuildAssets_WhenRidsCompared_AgreeAcrossScripts()
    {
        string root = FindRepositoryRoot();

        var shellRids = ReadShellRids(root);
        var workflowRids = ReadWorkflowRids(root);

        shellRids.Should().BeEquivalentTo(workflowRids);
    }

    [Fact]
    public void BuildAssets_WhenRidsRead_MatchExpectedSet()
    {
        string root = FindRepositoryRoot();

        var shellRids = ReadShellRids(root);

        shellRids.Should().BeEquivalentTo(ExpectedRids);
    }

    [Fact]
    public void BuildAssets_WhenDevTreeChecked_ContainsShippedAssets()
    {
        string root = FindRepositoryRoot();

        var present = ExpectedShippedAssets.Where(a => File.Exists(Path.Combine(root, a.Replace('/', Path.DirectorySeparatorChar))));

        present.Should().BeEquivalentTo(ExpectedShippedAssets);
    }
}
