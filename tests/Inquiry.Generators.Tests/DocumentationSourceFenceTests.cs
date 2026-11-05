using System.Text.RegularExpressions;

namespace Inquiry.Generators.Tests;

/// <summary>
/// A <c>&lt;!-- source: path --&gt;</c> comment above a code fence in the docs publishes a repository file
/// that the provider integration tests compile and run. These checks keep each fence identical to its
/// file, so the published example is the code that ran.
/// </summary>
public sealed partial class DocumentationSourceFenceTests
{
    // Every top-level file in this folder is published on the row-locking page.
    private const string PublishedFolder = "tests/Inquiry.FeatureCatalog/Locking";

    [Fact]
    public void EverySourceFenceMatchesItsFile()
    {
        var root = RepositoryRoot();
        var fences = SourceFences(root);
        Assert.NotEmpty(fences);

        foreach (var fence in fences)
        {
            var path = Path.Combine(root, fence.Path);
            Assert.True(File.Exists(path), $"{fence.Page} publishes '{fence.Path}', which does not exist.");

            var expected = Normalize(File.ReadAllText(path));
            var actual = Normalize(fence.Code);
            if (expected != actual)
            {
                Assert.Fail($"{fence.Page} differs from '{fence.Path}' at line {FirstDifferentLine(expected, actual)}.");
            }
        }
    }

    [Fact]
    public void EveryPublishedFileHasExactlyOneFence()
    {
        var root = RepositoryRoot();
        var fenced = SourceFences(root).Select(static fence => fence.Path).ToList();
        var published = Directory.GetFiles(Path.Combine(root, PublishedFolder), "*.cs")
            .Select(file => Path.GetRelativePath(root, file).Replace('\\', '/'))
            .Order(StringComparer.Ordinal);

        Assert.Equal(fenced.Distinct(StringComparer.Ordinal).Count(), fenced.Count);
        Assert.Equal(published, fenced.Where(static path => path.StartsWith(PublishedFolder + "/", StringComparison.Ordinal)).Order(StringComparer.Ordinal));
    }

    private static List<SourceFence> SourceFences(string root)
    {
        var articles = Path.Combine(root, "docs", "site", "articles");
        var fences = new List<SourceFence>();
        foreach (var page in Directory.GetFiles(articles, "*.md", SearchOption.AllDirectories))
        {
            var text = Normalize(File.ReadAllText(page));
            foreach (Match match in SourceFenceRegex().Matches(text))
            {
                fences.Add(new SourceFence(
                    Path.GetRelativePath(root, page).Replace('\\', '/'),
                    match.Groups["path"].Value,
                    match.Groups["code"].Value));
            }
        }

        return fences;
    }

    private static string Normalize(string text) => text.TrimStart('﻿').Replace("\r\n", "\n", StringComparison.Ordinal);

    private static int FirstDifferentLine(string expected, string actual)
    {
        var expectedLines = expected.Split('\n');
        var actualLines = actual.Split('\n');
        var line = 0;
        while (line < expectedLines.Length && line < actualLines.Length && expectedLines[line] == actualLines[line])
        {
            line++;
        }

        return line + 1;
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Inquiry.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate the repository root.");
    }

    private sealed record SourceFence(string Page, string Path, string Code);

    [GeneratedRegex(@"^<!-- source: (?<path>[^ ]+) -->\n```[a-z]*\n(?<code>.*?)^```", RegexOptions.Multiline | RegexOptions.Singleline)]
    private static partial Regex SourceFenceRegex();
}
