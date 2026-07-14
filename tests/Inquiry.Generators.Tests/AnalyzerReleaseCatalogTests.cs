using System.Reflection;
using System.Text.RegularExpressions;
using Inquiry.Generators.Diagnostics;
using Microsoft.CodeAnalysis;

namespace Inquiry.Generators.Tests;

public sealed class AnalyzerReleaseCatalogTests
{
    private static readonly string[] AnalyzerProjects =
    [
        "Inquiry.Generators.Shared",
        "Inquiry.Sqlite.Analyzer",
        "Inquiry.SqlServer.Analyzer",
        "Inquiry.PostgreSql.Analyzer",
        "Inquiry.MySql.Analyzer",
        "Inquiry.MariaDb.Analyzer",
        "Inquiry.Oracle.Analyzer",
    ];

    private static readonly string[] ReservedIds = ["INQ003", "INQ013", "INQ015", "INQ027"];

    [Fact]
    public void EveryActiveDescriptorHasOneUnshippedEntryOwnedByItsDeclaringAnalyzer()
    {
        var descriptors = AnalyzerAssemblies()
            .SelectMany(assembly => DescriptorFields(assembly)
                .Select(descriptor => new CatalogEntry(
                    descriptor.Id,
                    assembly.GetName().Name!,
                    descriptor.IsEnabledByDefault ? descriptor.DefaultSeverity.ToString() : "Disabled")))
            .OrderBy(static entry => entry.Id, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(descriptors.Length, descriptors.Select(static entry => entry.Id).Distinct(StringComparer.Ordinal).Count());

        var root = RepositoryRoot();
        var releases = AnalyzerProjects
            .SelectMany(project => ParseReleaseEntries(
                Path.Combine(root, "src", project, "AnalyzerReleases.Unshipped.md"),
                project))
            .OrderBy(static entry => entry.Id, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(descriptors, releases);
        Assert.Equal("Inquiry.SqlServer.Analyzer", Assert.Single(releases, static entry => entry.Id == "INQ076").Owner);
        Assert.All(AnalyzerProjects, project =>
        {
            var shipped = File.ReadAllText(Path.Combine(root, "src", project, "AnalyzerReleases.Shipped.md"));
            Assert.DoesNotMatch(@"\bINQ\d{3}\b", shipped);
        });
    }

    [Fact]
    public void RetiredIdsAreReservedSeparatelyAndNeverReused()
    {
        var root = RepositoryRoot();
        var reservedPath = Path.Combine(root, "src", "Inquiry.Generators.Shared", "AnalyzerReleases.Reserved.md");
        var reserved = Regex.Matches(File.ReadAllText(reservedPath), @"\bINQ\d{3}\b")
            .Select(static match => match.Value)
            .OrderBy(static id => id, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(ReservedIds, reserved);

        var active = AnalyzerAssemblies()
            .SelectMany(DescriptorFields)
            .Select(static descriptor => descriptor.Id)
            .OrderBy(static id => id, StringComparer.Ordinal)
            .ToArray();
        Assert.DoesNotContain(active, static id => ReservedIds.Contains(id, StringComparer.Ordinal));

        var expectedActive = Enumerable.Range(1, 82)
            .Select(static value => $"INQ{value:000}")
            .Except(ReservedIds, StringComparer.Ordinal)
            .OrderBy(static id => id, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(expectedActive, active);
    }

    private static Assembly[] AnalyzerAssemblies()
        =>
        [
            typeof(InquiryDiagnosticDescriptors).Assembly,
            typeof(global::Inquiry.Sqlite.Analyzer.InquirySqliteGenerator).Assembly,
            typeof(global::Inquiry.SqlServer.Analyzer.InquirySqlServerGenerator).Assembly,
            typeof(global::Inquiry.PostgreSql.Analyzer.InquiryPostgreSqlGenerator).Assembly,
            typeof(global::Inquiry.MySql.Analyzer.InquiryMySqlGenerator).Assembly,
            typeof(global::Inquiry.MariaDb.Analyzer.InquiryMariaDbGenerator).Assembly,
            typeof(global::Inquiry.Oracle.Analyzer.InquiryOracleGenerator).Assembly,
        ];

    private static IEnumerable<DiagnosticDescriptor> DescriptorFields(Assembly assembly)
        => assembly.GetTypes()
            .SelectMany(static type => type.GetFields(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Where(static field => field.FieldType == typeof(DiagnosticDescriptor))
            .Select(static field => (DiagnosticDescriptor)field.GetValue(null)!);

    private static IEnumerable<CatalogEntry> ParseReleaseEntries(string path, string owner)
    {
        foreach (var line in File.ReadLines(path))
        {
            var parts = line.Split('|').Select(static value => value.Trim()).ToArray();
            if (parts.Length >= 3 && Regex.IsMatch(parts[0], @"^INQ\d{3}$"))
                yield return new CatalogEntry(parts[0], owner, parts[2]);
        }
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Inquiry.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Could not locate the repository root.");
    }

    private sealed record CatalogEntry(string Id, string Owner, string Severity);
}
