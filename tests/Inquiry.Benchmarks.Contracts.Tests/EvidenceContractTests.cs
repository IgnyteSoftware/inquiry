using System.Text.Json;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains.InProcess.Emit;
using Perfolizer.Horology;
using Perfolizer.Mathematics.OutlierDetection;
using Inquiry.Benchmarks.Contracts;
using Inquiry.Benchmarks.Contracts.Evidence;
using Json.Schema;
using System.Reflection;

namespace Inquiry.Benchmarks.Contracts.Tests;

public sealed class EvidenceContractTests
{
    [Fact]
    public void ProductionCollectorDerivesTheCompleteRealResolvedAssetUniverse()
    {
        var metadata = typeof(EvidenceContractTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .ToDictionary(static attribute => attribute.Key, static attribute => attribute.Value!, StringComparer.Ordinal);
        var projectAssetsPath = metadata["ProjectAssetsFile"];
        var selectedAssetsPath = metadata["SelectedAssetsManifest"];
        var runtimeIdentifier = metadata["RuntimeIdentifier"];
        var runtimeTfm = Environment.Version.Major == 8 ? "net8.0" : "net10.0";
        var repositoryRoot = FindRepositoryRoot();
        var nuGetPackageRoot = metadata["NuGetPackageRoot"];
        var userRoot = DeriveUserProfileRoot(nuGetPackageRoot);
        var roots = new List<SelectedAssetRoot>
        {
            new("repo", repositoryRoot),
            new("nuget", nuGetPackageRoot),
            new("dotnet", metadata["DotNetRoot"]),
            new("user", userRoot),
        };
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (Directory.Exists(programFilesX86)) roots.Add(new("programfilesx86", programFilesX86));
        var manifest = ResolvedDependencyManifestCollector.Collect(
            selectedAssetsPath, projectAssetsPath, "sqlite", BenchmarkSourceLane.DeveloperProject,
            runtimeTfm, runtimeIdentifier, roots);

        Assert.True(manifest.Assets.Count > 30,
            $"Expected the real restore graph, not a hand-picked dependency subset; found {manifest.Assets.Count} assets.");
        Assert.Equal(runtimeIdentifier, manifest.RuntimeIdentifier);
        Assert.Contains(manifest.Assets, static asset => asset.Kind == ResolvedAssetKind.CompilerReference);
        Assert.Contains(manifest.Assets, static asset => asset.Kind == ResolvedAssetKind.Runtime);
        Assert.Contains(manifest.Assets, static asset => asset.Kind == ResolvedAssetKind.Analyzer);
        Assert.Contains(manifest.Assets, static asset => asset.Kind == ResolvedAssetKind.HostAssembly);
        Assert.Contains(manifest.Assets, static asset => asset.Kind == ResolvedAssetKind.ProductAssembly);
        Assert.All(manifest.Assets, static asset => Assert.Matches("^[a-f0-9]{64}$", asset.Sha256));
        Assert.True(ResolvedDependencyManifestCollector.IsExact(manifest, selectedAssetsPath, projectAssetsPath, roots));
        var canonicalBytes = manifest.ToCanonicalJsonBytes();
        Assert.Equal(manifest.ContentSha256, Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(canonicalBytes)).ToLowerInvariant());
        var roundTrip = JsonSerializer.Deserialize<ResolvedDependencyManifest>(canonicalBytes, EvidenceJson.Options);
        Assert.NotNull(roundTrip);
        Assert.Equal(manifest.ContentSha256, roundTrip.ContentSha256);
        Assert.True(ResolvedDependencyManifestCollector.IsExact(roundTrip, selectedAssetsPath, projectAssetsPath, roots));

        var removed = manifest with { Assets = manifest.Assets.Skip(1).ToArray() };
        Assert.False(ResolvedDependencyManifestCollector.IsExact(removed, selectedAssetsPath, projectAssetsPath, roots));
        var substituted = manifest.Assets.ToArray();
        substituted[0] = substituted[0] with { Sha256 = new string('0', 64) };
        Assert.False(ResolvedDependencyManifestCollector.IsExact(
            manifest with { Assets = substituted }, selectedAssetsPath, projectAssetsPath, roots));
        Assert.False(ResolvedDependencyManifestCollector.IsExact(
            manifest with
            {
                Assets = manifest.Assets.Append(new ResolvedDependencyAsset(
                    "repo/unexpected.dll", ResolvedAssetKind.Runtime, "runtime:unexpected", new string('1', 64))).ToArray(),
            },
            selectedAssetsPath, projectAssetsPath, roots));

        static string FindRepositoryRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Inquiry.slnx")))
                directory = directory.Parent;
            return directory?.FullName ?? throw new InvalidOperationException("Could not locate repository root.");
        }
    }

    [Fact]
    public void NuGetProfileRootDerivationAcceptsOnlyTheCanonicalProfileLayout()
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var profileRoot = Path.Combine(Path.GetTempPath(), "inquiry-profile");

        Assert.True(string.Equals(
            Path.GetFullPath(profileRoot),
            DeriveUserProfileRoot(Path.Combine(profileRoot, ".nuget", "packages")),
            comparison));
        Assert.Throws<InvalidOperationException>(() =>
            DeriveUserProfileRoot(Path.Combine(profileRoot, "custom", "packages")));
        Assert.Throws<InvalidOperationException>(() =>
            DeriveUserProfileRoot(Path.Combine(profileRoot, ".nuget", "custom")));

        var differentlyCasedRoot = Path.Combine(profileRoot, ".NUGET", "PACKAGES");
        if (OperatingSystem.IsWindows())
            Assert.True(string.Equals(
                Path.GetFullPath(profileRoot), DeriveUserProfileRoot(differentlyCasedRoot), comparison));
        else
            Assert.Throws<InvalidOperationException>(() => DeriveUserProfileRoot(differentlyCasedRoot));

        var volumeRoot = Path.GetPathRoot(Path.GetFullPath(profileRoot))!;
        Assert.Throws<InvalidOperationException>(() =>
            DeriveUserProfileRoot(Path.Combine(volumeRoot, ".nuget", "packages")));
    }

    [Fact]
    public void SelectedAssetCollectorRejectsEscapesHeaderDriftAndCaseInsensitiveCollisions()
    {
        var temporary = Path.Combine(Path.GetTempPath(), $"inquiry-selected-assets-{Guid.NewGuid():N}");
        var root = Path.Combine(temporary, "approved");
        Directory.CreateDirectory(root);
        try
        {
            string Asset(string name)
            {
                var path = Path.Combine(root, name);
                File.WriteAllText(path, name);
                return path;
            }

            var compiler = Asset("compiler.dll");
            var runtime = Asset("runtime.dll");
            var analyzer = Asset("Inquiry.Sqlite.Analyzer.dll");
            var generated = Asset("SelectedAssetProbe.g.cs");
            var host = Asset("Inquiry.Benchmarks.Probe.dll");
            var coreProduct = Asset("Inquiry.dll");
            var providerProduct = Asset("Inquiry.Sqlite.dll");
            var upperCase = Asset("Case.dll");
            var lowerCase = Asset("case.dll");
            var outside = Path.Combine(temporary, "outside.cs");
            File.WriteAllText(outside, "// outside");
            var projectAssets = Path.Combine(temporary, "project.assets.json");
            File.WriteAllText(projectAssets, "{}");
            var manifest = Path.Combine(temporary, "selected-assets.tsv");
            var header = $"{ResolvedDependencyManifestCollector.EmittedSchemaVersion}\tsqlite\tDeveloperProject\tnet8.0\tlinux-x64";
            string[] required =
            [
                $"CompilerReference\tcompiler:framework\t{compiler}",
                $"Runtime\truntime:host\t{runtime}",
                $"Analyzer\tanalyzer:host\t{analyzer}",
                $"GeneratedSource\tgenerated:probe\t{generated}",
                $"HostAssembly\tbenchmark-host\t{host}",
                $"ProductAssembly\tinquiry-product\t{coreProduct}",
                $"ProductAssembly\tprovider-product\t{providerProduct}",
            ];
            var roots = new[] { new SelectedAssetRoot("repo", root) };

            File.WriteAllLines(manifest, [header, .. required, $"GeneratedSource\tgenerated:outside\t{outside}"]);
            Assert.Throws<InvalidDataException>(() => ResolvedDependencyManifestCollector.Collect(
                manifest, projectAssets, "sqlite", BenchmarkSourceLane.DeveloperProject,
                "net8.0", "linux-x64", roots));

            File.WriteAllLines(manifest,
                [header, .. required, $"Runtime\truntime:case-upper\t{upperCase}", $"Runtime\truntime:case-lower\t{lowerCase}"]);
            Assert.Contains("collides case-insensitively", Assert.Throws<InvalidDataException>(() =>
                ResolvedDependencyManifestCollector.Collect(manifest, projectAssets, "sqlite",
                    BenchmarkSourceLane.DeveloperProject, "net8.0", "linux-x64", roots)).Message,
                StringComparison.Ordinal);

            File.WriteAllLines(manifest, [header.Replace("net8.0", "net10.0", StringComparison.Ordinal), .. required]);
            Assert.Throws<InvalidDataException>(() => ResolvedDependencyManifestCollector.Collect(
                manifest, projectAssets, "sqlite", BenchmarkSourceLane.DeveloperProject,
                "net8.0", "linux-x64", roots));
        }
        finally
        {
            if (Directory.Exists(temporary)) Directory.Delete(temporary, recursive: true);
        }
    }

    [Fact]
    public void SelectedAssetCollectorRequiresTheExactProviderAnalyzer()
    {
        var temporary = Path.Combine(Path.GetTempPath(), $"inquiry-provider-analyzer-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporary);
        try
        {
            string Asset(string name)
            {
                var path = Path.Combine(temporary, name);
                File.WriteAllText(path, name);
                return path;
            }

            var manifest = Path.Combine(temporary, "selected-assets.tsv");
            File.WriteAllLines(manifest,
            [
                $"{ResolvedDependencyManifestCollector.EmittedSchemaVersion}\tsqlite\tDeveloperProject\tnet8.0\tlinux-x64",
                $"CompilerReference\tcompiler:framework\t{Asset("compiler.dll")}",
                $"Runtime\truntime:host\t{Asset("runtime.dll")}",
                $"Analyzer\tanalyzer:shared\t{Asset("Inquiry.Generators.Shared.dll")}",
                $"GeneratedSource\tgenerated:probe\t{Asset("SelectedAssetProbe.g.cs")}",
                $"HostAssembly\tbenchmark-host\t{Asset("Inquiry.Benchmarks.Probe.dll")}",
                $"ProductAssembly\tinquiry-product\t{Asset("Inquiry.dll")}",
                $"ProductAssembly\tprovider-product\t{Asset("Inquiry.Sqlite.dll")}",
            ]);
            var projectAssets = Path.Combine(temporary, "project.assets.json");
            File.WriteAllText(projectAssets, "{}");

            var exception = Assert.Throws<InvalidDataException>(() => ResolvedDependencyManifestCollector.Collect(
                manifest, projectAssets, "sqlite", BenchmarkSourceLane.DeveloperProject,
                "net8.0", "linux-x64", [new SelectedAssetRoot("repo", temporary)]));

            Assert.Contains("provider analyzer", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(temporary)) Directory.Delete(temporary, recursive: true);
        }
    }

    [Fact]
    public void ResolvedDependencyIdentityIsStableAcrossPhysicalRootRelocation()
    {
        var temporary = Path.Combine(Path.GetTempPath(), $"inquiry-relocation-{Guid.NewGuid():N}");
        try
        {
            var first = Collect(Path.Combine(temporary, "first"));
            var second = Collect(Path.Combine(temporary, "second"));

            Assert.Equal(first.ProjectAssetsSha256, second.ProjectAssetsSha256);
            Assert.Equal(first.SelectedAssetsManifestSha256, second.SelectedAssetsManifestSha256);
            Assert.Equal(first.ContentSha256, second.ContentSha256);
            Assert.Equal(first.ToCanonicalJsonBytes(), second.ToCanonicalJsonBytes());

            ResolvedDependencyManifest Collect(string root)
            {
                Directory.CreateDirectory(root);
                string Asset(string relative, string content)
                {
                    var path = Path.Combine(root, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    File.WriteAllText(path, content);
                    return path;
                }

                var assets = new[]
                {
                    (ResolvedAssetKind.CompilerReference, "compiler:framework", Asset("ref/compiler.dll", "compiler")),
                    (ResolvedAssetKind.Runtime, "runtime:framework", Asset("runtime/runtime.dll", "runtime")),
                    (ResolvedAssetKind.Analyzer, "analyzer:provider", Asset("analyzers/Inquiry.Sqlite.Analyzer.dll", "analyzer")),
                    (ResolvedAssetKind.GeneratedSource, "generated:probe", Asset("generated/Probe.g.cs", "generated")),
                    (ResolvedAssetKind.HostAssembly, "benchmark-host", Asset("host/Inquiry.Benchmarks.Probe.dll", "host")),
                    (ResolvedAssetKind.ProductAssembly, "inquiry-product", Asset("product/Inquiry.dll", "core")),
                    (ResolvedAssetKind.ProductAssembly, "provider-product", Asset("product/Inquiry.Sqlite.dll", "provider")),
                };
                var selected = Path.Combine(root, "selected-assets.tsv");
                File.WriteAllLines(selected,
                [
                    $"{ResolvedDependencyManifestCollector.EmittedSchemaVersion}\tsqlite\tDeveloperProject\tnet8.0\tlinux-x64",
                    .. assets.Select(static asset => $"{asset.Item1}\t{asset.Item2}\t{asset.Item3}"),
                ]);
                var projectAssets = Path.Combine(root, "project.assets.json");
                File.WriteAllText(projectAssets, JsonSerializer.Serialize(new Dictionary<string, object>
                {
                    [Path.Combine(root, "obj")] = new { project = Path.Combine(root, "Inquiry.csproj") },
                    ["packageFolders"] = new Dictionary<string, object> { [Path.Combine(root, "packages")] = new { } },
                }));
                return ResolvedDependencyManifestCollector.Collect(selected, projectAssets, "sqlite",
                    BenchmarkSourceLane.DeveloperProject, "net8.0", "linux-x64",
                    [new SelectedAssetRoot("repo", root)]);
            }
        }
        finally
        {
            if (Directory.Exists(temporary)) Directory.Delete(temporary, recursive: true);
        }
    }

    [Fact]
    public void PackageAndSourceModesHaveDistinctIdentityAndOnlyPackageCanBeReleaseEvidence()
    {
        var source = TestData.ProjectSource();
        var package = TestData.PackageSource();

        Assert.NotEqual(source.IdentityHash, package.IdentityHash);
        Assert.False(source.ReleaseEligible);
        Assert.True(package.ReleaseEligible);
        Assert.Contains(EvidenceValidator.Validate(TestData.Envelope(source) with { Authoritative = true }),
            static error => error.Code == "source-mode");
    }

    [Fact]
    public void SourceManifestClaimsOnlyWiredProjectProviderTfmLanes()
    {
        Assert.Equal(new[]
        {
            "Inquiry", "Inquiry.Sqlite", "Inquiry.SqlServer", "Inquiry.PostgreSql", "Inquiry.MySql",
            "Inquiry.MariaDb", "Inquiry.Oracle", "Inquiry.Interceptors", "Inquiry.Testing",
        }, SourceArtifactManifestCatalog.PackageIds);
        Assert.Equal(12, SourceArtifactManifestCatalog.Manifests.Count);
        Assert.All(SourceArtifactManifestCatalog.Manifests,
            static manifest => Assert.Equal(BenchmarkSourceLane.DeveloperProject, manifest.Lane));
        Assert.Throws<ArgumentOutOfRangeException>(() => SourceArtifactManifestCatalog.GetRequired(
            "sqlite", BenchmarkSourceMode.PackageConsumer, "net8.0"));

        foreach (var manifest in SourceArtifactManifestCatalog.Manifests)
        {
            Assert.Equal(ResolvedDependencyManifest.RequiredSelectionRule, manifest.ResolvedDependencyScope);
            Assert.Equal(TestData.RuntimeIdentifier, manifest.RuntimeIdentifier);
            var source = TestData.ProjectSource(manifest.Provider, manifest.RuntimeTfm);
            Assert.Equal(manifest.IdentityHash, source.ArtifactManifestHash);
            var selectedArtifacts = source.ResolvedDependencies.FromSelectedAssets();
            Assert.Equal(manifest.ExpectedArtifacts.Select(static item => (item.Role, item.RelativeArtifactId))
                    .Concat(selectedArtifacts.Select(static item => (item.Role, item.RelativeArtifactId)))
                    .OrderBy(static item => item.Role).ThenBy(static item => item.RelativeArtifactId, StringComparer.Ordinal),
                source.Artifacts.Select(static item => (item.Role, item.RelativeArtifactId)).OrderBy(static item => item.Role)
                    .ThenBy(static item => item.RelativeArtifactId, StringComparer.Ordinal));
            Assert.Equal(selectedArtifacts, source.Artifacts.Where(static artifact => artifact.Role is
                    SourceArtifactRole.RuntimeAssembly or SourceArtifactRole.AnalyzerAssembly or SourceArtifactRole.GeneratedSource)
                .OrderBy(static artifact => artifact.Role)
                .ThenBy(static artifact => artifact.RelativeArtifactId, StringComparer.Ordinal));
            Assert.DoesNotContain(EvidenceValidator.Validate(TestData.Envelope(source, manifest.Provider, manifest.RuntimeTfm)),
                static error => error.Code == "source-artifact-manifest");
            var dependencies = manifest.ExpectedArtifacts
                .Where(static artifact => artifact.Role == SourceArtifactRole.DependencyArtifact).ToArray();
            Assert.Single(dependencies);
            Assert.EndsWith("/project.assets.json", dependencies[0].RelativeArtifactId, StringComparison.Ordinal);
            Assert.DoesNotContain(manifest.ExpectedArtifacts, static artifact => artifact.Role is
                SourceArtifactRole.RuntimeAssembly or SourceArtifactRole.AnalyzerAssembly or SourceArtifactRole.GeneratedSource);
            var analyzers = selectedArtifacts
                .Where(static artifact => artifact.Role == SourceArtifactRole.AnalyzerAssembly).ToArray();
            Assert.Equal(2, analyzers.Length);
            Assert.Contains(analyzers, static artifact => artifact.RelativeArtifactId.EndsWith(
                "/Inquiry.Generators.Shared.dll", StringComparison.Ordinal));
            var resolvedManifest = Assert.Single(manifest.ExpectedArtifacts,
                static artifact => artifact.Role == SourceArtifactRole.ResolvedDependencyManifest);
            Assert.EndsWith("/resolved-assets.manifest", resolvedManifest.RelativeArtifactId, StringComparison.Ordinal);
            var selectedAssetsManifest = Assert.Single(manifest.ExpectedArtifacts,
                static artifact => artifact.Role == SourceArtifactRole.SelectedAssetsManifest);
            Assert.EndsWith("/selected-assets.tsv", selectedAssetsManifest.RelativeArtifactId, StringComparison.Ordinal);
            Assert.NotEmpty(source.ResolvedDependencies.Assets);
            Assert.All(source.ResolvedDependencies.Assets, static asset => Assert.Matches("^[a-f0-9]{64}$", asset.Sha256));
            Assert.All(selectedArtifacts, static artifact => Assert.True(artifact.Role switch
            {
                SourceArtifactRole.RuntimeAssembly or SourceArtifactRole.AnalyzerAssembly =>
                    artifact.RelativeArtifactId.EndsWith(".dll", StringComparison.Ordinal),
                SourceArtifactRole.GeneratedSource => artifact.RelativeArtifactId.EndsWith(".g.cs", StringComparison.Ordinal),
                _ => false,
            }));
            Assert.All(manifest.ExpectedArtifacts, artifact =>
            {
                Assert.True(artifact.Role switch
                {
                    SourceArtifactRole.Package => artifact.RelativeArtifactId.EndsWith(".nupkg", StringComparison.Ordinal),
                    SourceArtifactRole.RuntimeAssembly or SourceArtifactRole.AnalyzerAssembly =>
                        artifact.RelativeArtifactId.EndsWith(".dll", StringComparison.Ordinal),
                    SourceArtifactRole.GeneratedSource => artifact.RelativeArtifactId.EndsWith(".g.cs", StringComparison.Ordinal),
                    SourceArtifactRole.SelectedAssetsManifest => artifact.RelativeArtifactId.EndsWith(".tsv", StringComparison.Ordinal),
                    SourceArtifactRole.ResolvedDependencyManifest => artifact.RelativeArtifactId.EndsWith(".manifest", StringComparison.Ordinal),
                    _ => artifact.RelativeArtifactId.EndsWith(".json", StringComparison.Ordinal),
                });
                if (artifact.Role != SourceArtifactRole.Package)
                    Assert.Contains($"/{manifest.Provider}/", artifact.RelativeArtifactId, StringComparison.Ordinal);
                if (artifact.Role is not (SourceArtifactRole.Package or SourceArtifactRole.AnalyzerAssembly))
                    Assert.Contains($"/{manifest.RuntimeTfm}/", artifact.RelativeArtifactId, StringComparison.Ordinal);
            });
        }

        Assert.Contains(EvidenceValidator.Validate(TestData.Envelope(TestData.PackageSource(), "postgresql", "net8.0")),
            static error => error.Code == "source-artifact-manifest");
        Assert.Contains(EvidenceValidator.Validate(TestData.Envelope(TestData.PackageSource(), "sqlite", "net10.0")),
            static error => error.Code == "source-artifact-manifest");
    }

    [Fact]
    public void EveryManifestArtifactRejectsOneAtATimeRemovalSubstitutionAndExtra()
    {
        foreach (var mode in new[] { BenchmarkSourceMode.ProjectReference })
        {
            var source = mode == BenchmarkSourceMode.PackageConsumer ? TestData.PackageSource() : TestData.ProjectSource();
            for (var index = 0; index < source.Artifacts.Count; index++)
            {
                var removed = source with { Artifacts = source.Artifacts.Where((_, candidate) => candidate != index).ToArray() };
                AssertManifestFailure(removed);

                var substitutedArtifacts = source.Artifacts.ToArray();
                substitutedArtifacts[index] = substitutedArtifacts[index] with
                {
                    RelativeArtifactId = substitutedArtifacts[index].RelativeArtifactId + ".substituted",
                };
                AssertManifestFailure(source with { Artifacts = substitutedArtifacts });

                var extra = source.Artifacts.Append(source.Artifacts[index] with
                {
                    RelativeArtifactId = source.Artifacts[index].RelativeArtifactId + ".extra",
                }).ToArray();
                AssertManifestFailure(source with { Artifacts = extra });
            }

            for (var index = 0; index < source.ResolvedDependencies.Assets.Count; index++)
            {
                var removed = source.ResolvedDependencies with
                {
                    Assets = source.ResolvedDependencies.Assets.Where((_, candidate) => candidate != index).ToArray(),
                };
                AssertResolvedFailure(source with { ResolvedDependencies = removed });

                var substitutedAssets = source.ResolvedDependencies.Assets.ToArray();
                substitutedAssets[index] = substitutedAssets[index] with
                {
                    Sha256 = new string(substitutedAssets[index].Sha256[0] == '0' ? '1' : '0', 64),
                };
                AssertResolvedFailure(source with
                {
                    ResolvedDependencies = source.ResolvedDependencies with { Assets = substitutedAssets },
                });

                var extraAssets = source.ResolvedDependencies.Assets.Append(source.ResolvedDependencies.Assets[index] with
                {
                    LogicalAssetId = source.ResolvedDependencies.Assets[index].LogicalAssetId + ".extra.dll",
                }).ToArray();
                AssertResolvedFailure(source with
                {
                    ResolvedDependencies = source.ResolvedDependencies with { Assets = extraAssets },
                });
            }
        }

        static void AssertManifestFailure(BenchmarkSourceIdentity source)
        {
            var envelope = TestData.Envelope();
            var drifted = envelope with { Source = source, CaseKey = envelope.CaseKey with { Source = source } };
            Assert.Contains(EvidenceValidator.Validate(drifted), static error => error.Code == "source-artifact-manifest");
        }

        static void AssertResolvedFailure(BenchmarkSourceIdentity source)
        {
            var envelope = TestData.Envelope();
            var drifted = envelope with { Source = source, CaseKey = envelope.CaseKey with { Source = source } };
            Assert.Contains(EvidenceValidator.Validate(drifted), static error => error.Code == "resolved-dependency-manifest");
        }
    }

    [Fact]
    public void ResolvedDependencyEvidenceRequiresTheExactProviderAnalyzer()
    {
        var source = TestData.ProjectSource();
        var resolved = source.ResolvedDependencies with
        {
            Assets = source.ResolvedDependencies.Assets.Where(static asset =>
                !asset.LogicalAssetId.EndsWith("/Inquiry.Sqlite.Analyzer.dll", StringComparison.Ordinal)).ToArray(),
        };
        var fixedArtifacts = source.Artifacts.Where(static artifact => artifact.Role is not
            (SourceArtifactRole.RuntimeAssembly or SourceArtifactRole.AnalyzerAssembly or SourceArtifactRole.GeneratedSource) &&
            artifact.Role != SourceArtifactRole.ResolvedDependencyManifest).Append(
                source.Artifacts.GetSingle(SourceArtifactRole.ResolvedDependencyManifest) with
                {
                    Sha256 = resolved.ContentSha256,
                });
        source = source with
        {
            ResolvedDependencies = resolved,
            Artifacts = fixedArtifacts.Concat(resolved.FromSelectedAssets()).ToArray(),
        };

        Assert.Contains(EvidenceValidator.Validate(TestData.Envelope(source)),
            static error => error.Code == "resolved-dependency-manifest");
    }

    [Fact]
    public void AuthoritativeEvidenceRejectsDirtyCheckoutAndSchemaOrKeyMismatch()
    {
        var envelope = TestData.Envelope() with
        {
            Authoritative = true,
            Checkout = new CheckoutEvidence(TestData.Commit, IsClean: false, HasUntrackedFiles: true),
            CaseId = "wrong",
            SchemaHash = "wrong",
        };

        var codes = EvidenceValidator.Validate(envelope).Select(static error => error.Code).ToHashSet();
        Assert.Contains("dirty-checkout", codes);
        Assert.Contains("case-key", codes);
        Assert.Contains("schema-hash", codes);
    }

    [Fact]
    public void SourceRolesBindConfigAndDependencyHashesExactly()
    {
        var envelope = TestData.Envelope();
        var artifacts = envelope.Source.Artifacts.ToArray();
        var runtimeIndex = Array.FindIndex(artifacts, static artifact => artifact.Role == SourceArtifactRole.RuntimeAssembly);
        artifacts[runtimeIndex] = artifacts[runtimeIndex] with { Sha256 = "not-a-hash" };
        var source = envelope.Source with
        {
            Artifacts = artifacts,
        };
        var drifted = envelope with
        {
            Source = source,
            CaseKey = envelope.CaseKey with { Source = source },
            BenchmarkConfigFileSha256 = new string('1', 64),
            DependencyHash = new string('2', 64),
        };

        var codes = EvidenceValidator.Validate(drifted).Select(static error => error.Code).ToHashSet();
        Assert.Contains("source-artifact", codes);
        Assert.Contains("config-source", codes);
        Assert.Contains("dependency-source", codes);

        var packageSource = TestData.PackageSource();
        Assert.NotEqual(packageSource.BundleSha256,
            packageSource.Artifacts.First(static artifact => artifact.Role == SourceArtifactRole.Package).Sha256);

        var resolvedAssets = envelope.Source.ResolvedDependencies.Assets.ToArray();
        resolvedAssets[0] = resolvedAssets[0] with { Sha256 = new string('e', 64) };
        var dependencySource = envelope.Source with
        {
            ResolvedDependencies = envelope.Source.ResolvedDependencies with { Assets = resolvedAssets },
        };
        Assert.Contains(EvidenceValidator.Validate(envelope with
        {
            Source = dependencySource,
            CaseKey = envelope.CaseKey with { Source = dependencySource },
        }), static error => error.Code == "resolved-dependency-manifest");

        var dependencyArtifacts = envelope.Source.Artifacts.ToArray();
        var resolvedManifestIndex = Array.FindIndex(dependencyArtifacts,
            static artifact => artifact.Role == SourceArtifactRole.ResolvedDependencyManifest);
        dependencyArtifacts[resolvedManifestIndex] = dependencyArtifacts[resolvedManifestIndex] with { Sha256 = new string('e', 64) };
        dependencySource = envelope.Source with { Artifacts = dependencyArtifacts };
        var dependencyCodes = EvidenceValidator.Validate(envelope with
        {
            Source = dependencySource,
            CaseKey = envelope.CaseKey with { Source = dependencySource },
        }).Select(static error => error.Code).ToHashSet();
        Assert.Contains("resolved-dependency-manifest", dependencyCodes);
        Assert.Contains("dependency-source", dependencyCodes);
    }

    [Fact]
    public void SourceArtifactsRejectDuplicateAndUnsafeRelativeIdentities()
    {
        var artifacts = TestData.Artifacts(BenchmarkSourceMode.PackageConsumer).ToList();
        artifacts.Add(artifacts[0] with { RelativeArtifactId = artifacts[0].RelativeArtifactId.ToUpperInvariant() });
        artifacts[1] = artifacts[1] with { RelativeArtifactId = "C:/private/package.nupkg" };
        var source = TestData.PackageSource() with { Artifacts = artifacts };

        var codes = EvidenceValidator.Validate(TestData.Envelope(source)).Select(static error => error.Code).ToHashSet();
        Assert.Contains("source-artifact-identity", codes);
        Assert.Contains("source-artifact", codes);
        Assert.Contains("source-artifact-manifest", codes);

        var resolved = TestData.ResolvedDependencies(BenchmarkSourceMode.ProjectReference);
        var collision = resolved.Assets.Append(resolved.Assets[0] with
        {
            LogicalAssetId = resolved.Assets[0].LogicalAssetId.ToUpperInvariant(),
        }).ToArray();
        source = TestData.ProjectSource() with
        {
            ResolvedDependencies = resolved with { Assets = collision },
        };
        Assert.Contains(EvidenceValidator.Validate(TestData.Envelope(source)),
            static error => error.Code == "resolved-dependency-manifest");
    }

    [Fact]
    public void ProjectModeForbidsPackageArtifactsAndBundleIdentity()
    {
        var source = new BenchmarkSourceIdentity(
            BenchmarkSourceMode.ProjectReference,
            TestData.Commit,
            "not-a-project-bundle",
            new string('9', 64),
            SourceArtifactManifestCatalog.GetRequired("sqlite", BenchmarkSourceMode.ProjectReference, "net8.0").IdentityHash,
            TestData.ResolvedDependencies(BenchmarkSourceMode.ProjectReference),
            TestData.Artifacts(BenchmarkSourceMode.PackageConsumer));

        var codes = EvidenceValidator.Validate(TestData.Envelope(source)).Select(static error => error.Code).ToHashSet();
        Assert.Contains("source-artifact-manifest", codes);
        Assert.Contains("package-identity", codes);
    }

    [Fact]
    public void ConfigFileBytesAndJobContractHaveIndependentHashes()
    {
        var envelope = TestData.Envelope();
        var manifestDrift = envelope.Source with { ArtifactManifestHash = new string('0', 64) };
        Assert.Contains(EvidenceValidator.Validate(envelope with
        {
            Source = manifestDrift,
            CaseKey = envelope.CaseKey with { Source = manifestDrift },
        }), static error => error.Code == "source-artifact-manifest");

        var configDrift = envelope with { BenchmarkConfigFileSha256 = new string('9', 64) };
        Assert.Contains(EvidenceValidator.Validate(configDrift), static error => error.Code == "config-source");

        var jobDrift = envelope with { BenchmarkJobContractHash = new string('0', 64) };
        Assert.Contains(EvidenceValidator.Validate(jobDrift), static error => error.Code == "bdn-contract");
        Assert.DoesNotContain(EvidenceValidator.Validate(jobDrift), static error => error.Code == "config-source");
    }

    [Fact]
    public void EvidenceBindsCommitFixtureScenarioParitySqlAndExactJobContract()
    {
        var envelope = TestData.Envelope();
        var drifted = envelope with
        {
            Checkout = envelope.Checkout with { Commit = new string('b', 40) },
            Seed = envelope.Seed + 1,
            Parity = envelope.Parity with
            {
                Observation = envelope.Parity.Observation with
                {
                    CommandGraph = envelope.Parity.Observation.CommandGraph with { SqlStatements = ["SELECT 1"] },
                },
            },
            BenchmarkDotNet = envelope.BenchmarkDotNet with { WarmupIterations = 0, MaxRelativeError = 0.50 },
        };

        var codes = EvidenceValidator.Validate(drifted).Select(static error => error.Code).ToHashSet();
        Assert.Contains("source-commit", codes);
        Assert.Contains("dataset-identity", codes);
        Assert.Contains("parity-hash", codes);
        Assert.Contains("sql-fingerprint", codes);
        Assert.Contains("bdn-contract", codes);
    }

    [Fact]
    public void EvidenceRequiresEveryLaunchIterationCoordinateExactlyOnce()
    {
        var envelope = TestData.Envelope();
        var measurements = envelope.BenchmarkDotNet.Measurements.ToArray();
        measurements[^1] = measurements[0];
        var drifted = envelope with
        {
            BenchmarkDotNet = envelope.BenchmarkDotNet with
            {
                Measurements = measurements,
                RawStatistics = measurements.Select(static item => item.NanosecondsPerOperation).ToArray(),
            },
        };

        Assert.Contains(EvidenceValidator.Validate(drifted), static error => error.Code == "bdn-fields");
    }

    [Fact]
    public void EveryEnvironmentAndDatabaseFacetIsValidatedAndHashed()
    {
        var envelope = TestData.Envelope();
        var drifted = envelope with
        {
            Environment = envelope.Environment with { DockerNetwork = "" },
            Database = envelope.Database with { ResourceTopology = "" },
        };
        var codes = EvidenceValidator.Validate(drifted).Select(static error => error.Code).ToHashSet();
        Assert.Contains("environment", codes);
        Assert.Contains("environment-hash", codes);
        Assert.Contains("database-topology", codes);

        var baseline = TestData.Baseline() with { EnvironmentIdentity = new string('0', 64) };
        Assert.Contains(BaselineValidator.Validate(baseline, envelope.CaseId, envelope.EnvironmentHash),
            static error => error.Code == "baseline-environment");
    }

    [Fact]
    public void RuntimeTfmVersionAndDescriptionAreIndependentlyHashedAndValidated()
    {
        var envelope = TestData.Envelope();
        var versionDrift = envelope.Environment with { RuntimeVersion = "8.0.99" };
        var descriptionDrift = envelope.Environment with { RuntimeDescription = ".NET custom runtime" };
        Assert.NotEqual(envelope.Environment.IdentityHash, versionDrift.IdentityHash);
        Assert.NotEqual(envelope.Environment.IdentityHash, descriptionDrift.IdentityHash);

        var tfmDrift = envelope with { Environment = envelope.Environment with { RuntimeTfm = "net10.0" } };
        var codes = EvidenceValidator.Validate(tfmDrift).Select(static error => error.Code).ToHashSet();
        Assert.Contains("environment-hash", codes);
        Assert.Contains("bdn-contract", codes);
    }

    [Fact]
    public void BenchmarkDotNetResultFieldsAreExactAndEveryRetainedValueIsValidated()
    {
        var envelope = TestData.Envelope();
        Assert.DoesNotContain(EvidenceValidator.Validate(envelope), static error => error.Code == "bdn-field-name");

        var unknown = envelope with
        {
            BenchmarkDotNet = envelope.BenchmarkDotNet with
            {
                ResultFields = envelope.BenchmarkDotNet.ResultFields.Append(new("Mystery", "1")).ToArray(),
            },
        };
        Assert.Contains(EvidenceValidator.Validate(unknown), static error => error.Code == "bdn-field-name");
    }

    [Fact]
    public void TimingStatisticsUseRawMeasurementsAndAllocationUsesNativeReportGcStats()
    {
        var envelope = TestData.Envelope();
        var validCodes = EvidenceValidator.Validate(envelope).Select(static error => error.Code).ToHashSet();
        Assert.DoesNotContain("aggregation-contract", validCodes);
        Assert.DoesNotContain("result-fields", validCodes);
        Assert.DoesNotContain("result-statistics", validCodes);

        var measurements = envelope.BenchmarkDotNet.Measurements.ToArray();
        measurements[0] = measurements[0] with
        {
            NanosecondsPerOperation = measurements[0].NanosecondsPerOperation + 10,
        };
        var rawDrift = envelope with
        {
            BenchmarkDotNet = envelope.BenchmarkDotNet with
            {
                Measurements = measurements,
                RawStatistics = measurements.Select(static item => item.NanosecondsPerOperation).ToArray(),
            },
        };
        var rawCodes = EvidenceValidator.Validate(rawDrift).Select(static error => error.Code).ToHashSet();
        Assert.Contains("result-fields", rawCodes);
        Assert.Contains("result-statistics", rawCodes);
        Assert.Contains(EvidenceArtifactValidator.Validate(
                JsonSerializer.SerializeToUtf8Bytes(rawDrift, EvidenceJson.Options)).Errors,
            static error => error.Code == "result-fields");

        var allocationDrift = envelope with
        {
            BenchmarkDotNet = envelope.BenchmarkDotNet with
            {
                GcStats = envelope.BenchmarkDotNet.GcStats with { BytesAllocatedPerOperation = 72 },
            },
        };
        var allocationCodes = EvidenceValidator.Validate(allocationDrift).Select(static error => error.Code).ToHashSet();
        Assert.Contains("result-fields", allocationCodes);
        Assert.Contains("result-statistics", allocationCodes);

        var forgedProvenance = envelope with
        {
            BenchmarkDotNet = envelope.BenchmarkDotNet with
            {
                GcStats = envelope.BenchmarkDotNet.GcStats with { Provenance = "custom-allocation-estimate" },
            },
        };
        Assert.Contains(EvidenceValidator.Validate(forgedProvenance), static error => error.Code == "allocation-provenance");
        var operationsDrift = envelope with
        {
            BenchmarkDotNet = envelope.BenchmarkDotNet with
            {
                GcStats = envelope.BenchmarkDotNet.GcStats with { TotalOperations = 0 },
            },
        };
        Assert.Contains(EvidenceValidator.Validate(operationsDrift),
            static error => error.Code == "allocation-provenance");

        var resultDrift = envelope with { Result = envelope.Result with { Value = envelope.Result.Value + 0.001 } };
        Assert.Contains(EvidenceValidator.Validate(resultDrift), static error => error.Code == "result-statistics");

        var wrongPrecision = envelope with
        {
            BenchmarkDotNet = envelope.BenchmarkDotNet with
            {
                ResultFields = envelope.BenchmarkDotNet.ResultFields.Select(field =>
                    field.Name == "Mean" ? field with { Value = "137.5 ns" } : field).ToArray(),
            },
        };
        Assert.Contains(EvidenceValidator.Validate(wrongPrecision), static error => error.Code == "result-fields");

        var duplicate = envelope with
        {
            BenchmarkDotNet = envelope.BenchmarkDotNet with
            {
                ResultFields = envelope.BenchmarkDotNet.ResultFields.Append(new("Mean", "137.500 ns")).ToArray(),
            },
        };
        Assert.Contains(EvidenceValidator.Validate(duplicate), static error => error.Code == "result-fields");

        var contractDrift = envelope with
        {
            Aggregation = envelope.Aggregation with { DecimalPlaces = 2 },
        };
        Assert.Contains(EvidenceValidator.Validate(contractDrift), static error => error.Code == "aggregation-contract");
    }

    [Fact]
    public void NativeAllocationCanBeUnavailableForLatencyButNotAllocationCases()
    {
        var envelope = TestData.Envelope();
        var unavailable = envelope with
        {
            BenchmarkDotNet = envelope.BenchmarkDotNet with
            {
                GcStats = envelope.BenchmarkDotNet.GcStats with { BytesAllocatedPerOperation = null },
                ResultFields = envelope.BenchmarkDotNet.ResultFields.Select(field =>
                    field.Name == "Allocated" ? field with { Value = BenchmarkAggregationCatalog.UnavailableAllocation } : field).ToArray(),
            },
            Result = envelope.Result with { AllocatedBytes = null },
        };
        var latencyCodes = EvidenceValidator.Validate(unavailable).Select(static error => error.Code).ToHashSet();
        Assert.DoesNotContain("allocation-provenance", latencyCodes);
        Assert.DoesNotContain("result-fields", latencyCodes);
        Assert.DoesNotContain("result-statistics", latencyCodes);

        var allocationCase = unavailable with
        {
            CaseKey = unavailable.CaseKey with { MetricFamily = MetricFamily.Allocation },
        };
        Assert.Contains(EvidenceValidator.Validate(allocationCase),
            static error => error.Code == "allocation-unavailable");
    }

    [Theory]
    [InlineData("Server=db;User Id=sa;Password=secret")]
    [InlineData("/home/alice/worktrees/inquiry/result.json")]
    [InlineData("C:\\Users\\alice\\source\\result.json")]
    [InlineData("prefix /root/private/result.json")]
    [InlineData("prefix /arbitrary-ci/worktrees/inquiry/result.json")]
    [InlineData("\\\\server\\share\\result.json")]
    [InlineData("https://user:token@example.test/path")]
    public void HygieneRejectsSecretsAndAbsolutePaths(string leaked)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(new { note = leaked });
        Assert.NotEmpty(EvidenceHygieneValidator.Validate(json));
    }

    [Fact]
    public void HygieneRejectsRowPayloadsAndOversizeShards()
    {
        var rows = JsonSerializer.SerializeToUtf8Bytes(new { rows = new[] { new { CustomerId = "ALFKI" } } });
        Assert.Contains(EvidenceHygieneValidator.Validate(rows), static error => error.Code == "row-data");

        var oversized = new byte[EvidenceLimits.MaxShardBytes + 1];
        Assert.Contains(EvidenceHygieneValidator.Validate(oversized), static error => error.Code == "shard-size");
        Assert.Contains(EvidenceHygieneValidator.ValidateTotalBytes(EvidenceLimits.MaxCheckedEvidenceBytes + 1),
            static error => error.Code == "total-size");

        var aliases = JsonSerializer.SerializeToUtf8Bytes(new { records = new[] { "customer" } });
        Assert.Contains(EvidenceHygieneValidator.Validate(aliases), static error => error.Code == "row-data");

        var pathKey = JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, string> { ["/home/alice/secret"] = "value" });
        Assert.Contains(EvidenceHygieneValidator.Validate(pathKey), static error => error.Code == "absolute-path");

        var exception = JsonSerializer.SerializeToUtf8Bytes(new { note = "System.InvalidOperationException: failed" });
        Assert.Contains(EvidenceHygieneValidator.Validate(exception), static error => error.Code == "unsafe-exception");

        foreach (var property in new[] { "db_user", "buildHost", "api-key", "raw_records", "entityPayload" })
        {
            var alias = JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, string> { [property] = "redacted" });
            Assert.NotEmpty(EvidenceHygieneValidator.Validate(alias));
        }
    }

    [Fact]
    public void CheckedJsonMatchesPinnedSchema()
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(TestData.Envelope(), EvidenceJson.Options));
        var result = CheckedArtifactSchemas.Evidence.Evaluate(document.RootElement, new EvaluationOptions { OutputFormat = OutputFormat.List });
        Assert.True(result.IsValid, result.ToString());
    }

    [Fact]
    public void CheckedSchemaRejectsUnknownNestedProperties()
    {
        var json = JsonSerializer.Serialize(TestData.Envelope(), EvidenceJson.Options);
        using var original = JsonDocument.Parse(json);
        var checkout = original.RootElement.GetProperty("checkout");
        var mutated = json.Replace(checkout.GetRawText(), checkout.GetRawText().TrimEnd('}') + ",\"workspacePath\":\"redacted\"}", StringComparison.Ordinal);
        using var document = JsonDocument.Parse(mutated);
        var result = CheckedArtifactSchemas.Evidence.Evaluate(document.RootElement, new EvaluationOptions { OutputFormat = OutputFormat.List });
        Assert.False(result.IsValid);
    }

    [Fact]
    public void ProductionEvidenceArtifactValidatorRunsEveryGateInOrder()
    {
        var valid = JsonSerializer.SerializeToUtf8Bytes(TestData.Envelope(), EvidenceJson.Options);
        var accepted = EvidenceArtifactValidator.Validate(valid);
        Assert.False(accepted.IsValid);
        Assert.Contains(accepted.Errors, static error => error.Code == "filesystem-context");

        Assert.Contains(EvidenceArtifactValidator.Validate("{"u8.ToArray()).Errors,
            static error => error.Code == "invalid-json");

        var schemaInvalid = JsonSerializer.SerializeToUtf8Bytes(new { schemaVersion = EvidenceSchema.Version });
        Assert.Contains(EvidenceArtifactValidator.Validate(schemaInvalid).Errors,
            static error => error.Code == "json-schema");

        var unsafeEnvironment = TestData.Envelope();
        var environment = unsafeEnvironment.Environment with { DockerStorage = "/arbitrary/worktree/private/output" };
        unsafeEnvironment = unsafeEnvironment with { Environment = environment, EnvironmentHash = environment.IdentityHash };
        Assert.Contains(EvidenceArtifactValidator.Validate(JsonSerializer.SerializeToUtf8Bytes(unsafeEnvironment, EvidenceJson.Options)).Errors,
            static error => error.Code == "absolute-path");

        var semantic = TestData.Envelope() with { CaseId = new string('0', 64) };
        Assert.Contains(EvidenceArtifactValidator.Validate(JsonSerializer.SerializeToUtf8Bytes(semantic, EvidenceJson.Options)).Errors,
            static error => error.Code == "case-key");
    }

    [Fact]
    public void ProductionEvidenceArtifactValidatorRehashesContainedPhysicalArtifactsAndResolvedAssets()
    {
        var fixture = CreatePhysicalEvidenceFixture();
        try
        {
            var json = JsonSerializer.SerializeToUtf8Bytes(fixture.Evidence, EvidenceJson.Options);
            var accepted = EvidenceArtifactValidator.Validate(json, fixture.Context);
            Assert.True(accepted.IsValid, string.Join(Environment.NewLine, accepted.Errors));

            var selectedArtifact = fixture.Evidence.Source.Artifacts.First(static artifact =>
                artifact.Role == SourceArtifactRole.GeneratedSource);
            const string forgedArtifactId = "repo/forged/Forged.InquiryStore.g.cs";
            var forgedPath = Path.Combine(fixture.Context.ArtifactRoot,
                forgedArtifactId.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(forgedPath)!);
            File.WriteAllText(forgedPath, "// forged generated role beside the authentic selected manifest");
            var forgedArtifact = selectedArtifact with
            {
                RelativeArtifactId = forgedArtifactId,
                Sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(forgedPath)))
                    .ToLowerInvariant(),
            };
            var forgedArtifacts = fixture.Evidence.Source.Artifacts
                .Select(artifact => artifact == selectedArtifact ? forgedArtifact : artifact).ToArray();
            var forgedSource = fixture.Evidence.Source with { Artifacts = forgedArtifacts };
            var forgedCaseKey = fixture.Evidence.CaseKey with { Source = forgedSource };
            var forgedEvidence = fixture.Evidence with
            {
                Source = forgedSource,
                CaseKey = forgedCaseKey,
                CaseId = forgedCaseKey.StableId,
            };
            Assert.Contains(EvidenceArtifactValidator.Validate(
                    JsonSerializer.SerializeToUtf8Bytes(forgedEvidence, EvidenceJson.Options), fixture.Context).Errors,
                static error => error.Code == "selected-asset-binding");

            var selectedPath = Path.Combine(fixture.Context.ArtifactRoot,
                selectedArtifact.RelativeArtifactId.Replace('/', Path.DirectorySeparatorChar));
            var authenticSelectedBytes = File.ReadAllBytes(selectedPath);
            File.WriteAllText(selectedPath, "// forged bytes under the authentic logical selected-asset ID");
            var forgedHashArtifact = selectedArtifact with
            {
                Sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(selectedPath)))
                    .ToLowerInvariant(),
            };
            var forgedHashArtifacts = fixture.Evidence.Source.Artifacts
                .Select(artifact => artifact == selectedArtifact ? forgedHashArtifact : artifact).ToArray();
            var forgedHashSource = fixture.Evidence.Source with { Artifacts = forgedHashArtifacts };
            var forgedHashCaseKey = fixture.Evidence.CaseKey with { Source = forgedHashSource };
            var forgedHashEvidence = fixture.Evidence with
            {
                Source = forgedHashSource,
                CaseKey = forgedHashCaseKey,
                CaseId = forgedHashCaseKey.StableId,
            };
            Assert.Contains(EvidenceArtifactValidator.Validate(
                    JsonSerializer.SerializeToUtf8Bytes(forgedHashEvidence, EvidenceJson.Options), fixture.Context).Errors,
                static error => error.Code == "selected-asset-binding");
            File.WriteAllBytes(selectedPath, authenticSelectedBytes);

            var configArtifact = fixture.Evidence.Source.Artifacts.GetSingle(SourceArtifactRole.BenchmarkConfigFile);
            var configPath = Path.Combine(fixture.Context.ArtifactRoot,
                configArtifact.RelativeArtifactId.Replace('/', Path.DirectorySeparatorChar));
            File.AppendAllText(configPath, "tampered");
            Assert.Contains(EvidenceArtifactValidator.Validate(json, fixture.Context).Errors,
                static error => error.Code == "artifact-content");
        }
        finally
        {
            if (Directory.Exists(fixture.Context.ArtifactRoot))
                Directory.Delete(fixture.Context.ArtifactRoot, recursive: true);
        }
    }

    [Fact]
    public void CheckedBaselineHasClosedMachineSchemaAndBindsEvidenceEnvironment()
    {
        var evidence = TestData.Envelope();
        var valid = JsonSerializer.SerializeToUtf8Bytes(TestData.Baseline(), EvidenceJson.Options);
        Assert.True(BaselineArtifactValidator.Validate(valid, evidence).IsValid);

        var json = JsonSerializer.Serialize(TestData.Baseline(), EvidenceJson.Options);
        var invalid = json[..^1] + ",\"workspace\":\"redacted\"}";
        Assert.Contains(BaselineArtifactValidator.Validate(System.Text.Encoding.UTF8.GetBytes(invalid), evidence).Errors,
            static error => error.Code == "json-schema");
    }

    [Fact]
    public void BaselineRetainsExactVectorsBudgetsAndExclusions()
    {
        var baseline = TestData.Baseline();
        Assert.Empty(BaselineValidator.Validate(baseline));

        var broken = baseline with
        {
            LaunchSamples = [],
            RelativeBudget = null,
            InvalidSamples = [new InvalidSample(3, LaunchHealthStatus.Healthy, "bad")],
        };
        var codes = BaselineValidator.Validate(broken).Select(static error => error.Code).ToHashSet();
        Assert.Contains("baseline-vector", codes);
        Assert.Contains("baseline-budget", codes);
        Assert.Contains("invalid-sample-health", codes);

        Assert.Contains(BaselineValidator.Validate(baseline with
        {
            Approval = baseline.Approval with { Reviewers = ["reviewer-a", "   "] },
        }), static error => error.Code == "baseline-approval");
        Assert.Contains(BaselineValidator.Validate(baseline with
        {
            Approval = baseline.Approval with { Commit = "ABCDEF" },
        }), static error => error.Code == "baseline-approval");
        Assert.Contains(BaselineValidator.Validate(baseline with
        {
            Approval = baseline.Approval with { Commit = new string('f', 40) },
        }, TestData.Envelope()), static error => error.Code == "baseline-approval");
    }

    [Fact]
    public void BaselineFamilyIdentityBindsCanonicalMembershipAndOrder()
    {
        var baseline = TestData.Baseline();
        var members = new[] { baseline.CaseId, new string('f', 64) }.Order(StringComparer.Ordinal).ToArray();
        var bound = baseline with
        {
            FamilyMembers = members,
            FamilyOrder = Array.IndexOf(members, baseline.CaseId),
            FamilyIdentity = BaselineFamilyIdentity.Compute(members),
        };
        Assert.DoesNotContain(BaselineValidator.Validate(bound), static error => error.Code == "baseline-family");

        Assert.Contains(BaselineValidator.Validate(bound with { FamilyIdentity = new string('0', 64) }),
            static error => error.Code == "baseline-family");
        Assert.Contains(BaselineValidator.Validate(bound with { FamilyMembers = members.AsEnumerable().Reverse().ToArray() }),
            static error => error.Code == "baseline-family");
        Assert.Contains(BaselineValidator.Validate(bound with { FamilyMembers = [members[0], members[0]] }),
            static error => error.Code == "baseline-family");
        Assert.Contains(BaselineValidator.Validate(bound with { FamilyOrder = 1 - bound.FamilyOrder }),
            static error => error.Code == "baseline-family");
    }

    [Fact]
    public void BaselineCampaignRequiresExactUniqueInRangeLaunchCoverage()
    {
        var baseline = TestData.Baseline();
        var duplicate = baseline with
        {
            LaunchSamples = baseline.LaunchSamples.Append(baseline.LaunchSamples[0]).ToArray(),
        };
        Assert.Contains(BaselineValidator.Validate(duplicate), static error => error.Code == "baseline-sample-coverage");

        var outOfRange = baseline with
        {
            InvalidSamples = [new InvalidSample(16, LaunchHealthStatus.CpuContention, new string('1', 64))],
        };
        Assert.Contains(BaselineValidator.Validate(outOfRange), static error => error.Code == "baseline-sample-coverage");

        var invalidWindow = baseline with { WindowEndUtc = baseline.WindowStartUtc };
        Assert.Contains(BaselineValidator.Validate(invalidWindow), static error => error.Code == "baseline-campaign");

        Assert.Contains(BaselineValidator.Validate(baseline, expectedLaunchCount: 15),
            static error => error.Code == "baseline-sample-count");
    }

    [Fact]
    public void BaselineBindsCampaignWindowRecomputedLaunchMediansAndAuditedExclusions()
    {
        var evidence = TestData.Envelope();
        var baseline = TestData.Baseline();
        Assert.Empty(BaselineValidator.Validate(baseline, evidence));
        Assert.Contains(EvidenceValidator.Validate(evidence with { CampaignId = "" }),
            static error => error.Code == "campaign-evidence");
        var futureAudit = evidence.LaunchHealth.ToArray();
        futureAudit[0] = futureAudit[0] with { CollectedAtUtc = evidence.CollectedAtUtc.AddTicks(1) };
        Assert.Contains(EvidenceValidator.Validate(evidence with { LaunchHealth = futureAudit }),
            static error => error.Code == "launch-health");
        var forgedHealthy = evidence.LaunchHealth.ToArray();
        forgedHealthy[0] = forgedHealthy[0] with
        {
            Metrics = forgedHealthy[0].Metrics with { CpuContentionPercent = 99.0 },
            Status = LaunchHealthStatus.Healthy,
        };
        Assert.Contains(EvidenceValidator.Validate(evidence with { LaunchHealth = forgedHealthy }),
            static error => error.Code == "launch-health");
        var forgedNoisy = evidence.LaunchHealth.ToArray();
        forgedNoisy[0] = forgedNoisy[0] with
        {
            Metrics = forgedNoisy[0].Metrics with { CoefficientOfVariation = 0.50 },
            Status = LaunchHealthStatus.Healthy,
        };
        var forgedNoisyCodes = EvidenceValidator.Validate(evidence with { LaunchHealth = forgedNoisy })
            .Select(static error => error.Code).ToHashSet();
        Assert.Contains("launch-health", forgedNoisyCodes);
        Assert.Contains("launch-health-metrics", forgedNoisyCodes);
        Assert.Contains(EvidenceValidator.Validate(evidence with
        {
            LaunchHealthRule = evidence.LaunchHealthRule with { MaximumCpuContentionPercent = 100.0 },
        }), static error => error.Code == "launch-health-rule");

        Assert.Contains(BaselineValidator.Validate(baseline with { CampaignId = "different-campaign" }, evidence),
            static error => error.Code == "baseline-campaign-evidence");
        Assert.Contains(BaselineValidator.Validate(baseline with { WindowEndUtc = evidence.CollectedAtUtc.AddTicks(-1) }, evidence),
            static error => error.Code == "baseline-campaign-evidence");

        var medianDrift = baseline with
        {
            LaunchSamples = baseline.LaunchSamples.Select((sample, index) =>
                index == 0 ? sample with { Median = sample.Median + 0.001 } : sample).ToArray(),
        };
        Assert.Contains(BaselineValidator.Validate(medianDrift, evidence),
            static error => error.Code == "baseline-launch-median");
        Assert.Contains(BaselineArtifactValidator.Validate(
                JsonSerializer.SerializeToUtf8Bytes(medianDrift, EvidenceJson.Options), evidence).Errors,
            static error => error.Code == "baseline-launch-median");

        var retainedHealth = evidence.LaunchHealth.ToArray();
        retainedHealth[0] = retainedHealth[0] with { Status = LaunchHealthStatus.ThermalThrottling };
        Assert.Contains(BaselineValidator.Validate(baseline, evidence with { LaunchHealth = retainedHealth }),
            static error => error.Code == "baseline-health-contradiction");

        var exclusion = baseline.InvalidSamples[0];
        var contradictedExclusion = baseline with
        {
            InvalidSamples = [exclusion with { Status = LaunchHealthStatus.PowerThrottling }],
        };
        Assert.Contains(BaselineValidator.Validate(contradictedExclusion, evidence),
            static error => error.Code == "baseline-exclusion-evidence");
        var unboundExclusion = baseline with
        {
            InvalidSamples = [exclusion with { HealthEvidenceHash = new string('0', 64) }],
        };
        Assert.Contains(BaselineValidator.Validate(unboundExclusion, evidence),
            static error => error.Code == "baseline-exclusion-evidence");

        var outsideHealth = evidence.LaunchHealth.ToArray();
        outsideHealth[0] = outsideHealth[0] with { CollectedAtUtc = baseline.WindowStartUtc.AddTicks(-1) };
        Assert.Contains(BaselineValidator.Validate(baseline, evidence with { LaunchHealth = outsideHealth }),
            static error => error.Code == "baseline-health-window");
    }

    [Fact]
    public void CanonicalJobManifestPinsNet8Net10AndLiveIterationRules()
    {
        Assert.Equal(["net8.0", "net10.0"], BenchmarkJobCatalog.Jobs.Select(static job => job.RuntimeTfm));
        Assert.All(BenchmarkJobCatalog.Jobs, static job =>
        {
            Assert.Equal(16, job.LaunchCount);
            Assert.Equal(1, job.InvocationCount);
            Assert.Equal(1, job.UnrollFactor);
            Assert.True(job.WarmupIterationFloor > 0);
            Assert.True(job.MeasurementIterationFloor >= 15);
            Assert.True(job.MinIterationTimeMilliseconds >= 100);
            Assert.InRange(job.MaxRelativeError, 0, 0.02);
            Assert.True(job.EvaluateOverhead);
            Assert.True(job.MemoryDiagnoser);
            Assert.Equal("dont-remove", job.OutlierMode);
        });
    }

    [Fact]
    public void CheckedJobMaterializesAnExecutableFullJsonBenchmarkDotNetConfiguration()
    {
        foreach (var contract in BenchmarkJobCatalog.Jobs)
        {
            var config = BenchmarkDotNetConfigFactory.Create(contract);
            var job = Assert.Single(config.GetJobs());

            Assert.Equal(contract.Id, job.Id);
            Assert.Equal(contract.LaunchCount, job.Run.LaunchCount);
            Assert.Equal(contract.WarmupIterationFloor, job.Run.WarmupCount);
            Assert.Equal(contract.MeasurementIterationFloor, job.Run.IterationCount);
            Assert.Equal(contract.InvocationCount, job.Run.InvocationCount);
            Assert.Equal(contract.UnrollFactor, job.Run.UnrollFactor);
            Assert.Equal(TimeInterval.Millisecond * contract.MinIterationTimeMilliseconds, job.Accuracy.MinIterationTime);
            Assert.Equal(contract.EvaluateOverhead, job.Accuracy.EvaluateOverhead);
            Assert.Equal(contract.MaxRelativeError, job.Accuracy.MaxRelativeError);
            Assert.Equal(OutlierMode.DontRemove, job.Accuracy.OutlierMode);
            Assert.Contains(config.GetExporters(), exporter => exporter == JsonExporter.Full);
            Assert.Contains(config.GetDiagnosers(), diagnoser => diagnoser == MemoryDiagnoser.Default);
            Assert.NotEmpty(config.GetValidators());
            Assert.NotEmpty(config.GetLoggers());
            Assert.NotEmpty(config.GetColumnProviders());
            Assert.Equal(contract.ArtifactRoot, config.ArtifactsPath);
        }

        Assert.Throws<ArgumentException>(() => BenchmarkDotNetConfigFactory.Create(
            BenchmarkJobCatalog.Jobs[0] with { MemoryDiagnoser = false }));
    }

    [Fact]
    public void TinyBenchmarkRunsValidatesExportsAndUsesNativeNullableAllocationApi()
    {
        var artifacts = Path.Combine(Path.GetTempPath(), $"inquiry-bdn-smoke-{Guid.NewGuid():N}");
        try
        {
            var runtimeTfm = Environment.Version.Major == 8 ? "net8.0" : "net10.0";
            var contract = BenchmarkJobCatalog.GetRequired(runtimeTfm == "net8.0" ? "net8-live-v1" : "net10-live-v1") with
            {
                Id = "evidence-smoke",
                LaunchCount = 1,
                WarmupIterationFloor = 1,
                MeasurementIterationFloor = 2,
                MinIterationTimeMilliseconds = 1,
                MaxRelativeError = 0.99,
                ArtifactRoot = artifacts,
            };
            var config = BenchmarkDotNetConfigFactory.Create(contract, InProcessEmitToolchain.Instance);
#if DEBUG
            // The test host and its referenced contracts are Debug assemblies. Production Release runs
            // retain DefaultConfig's optimization validator; only this Debug smoke execution disables it.
            config.WithOptions(config.Options | ConfigOptions.DisableOptimizationsValidator);
#endif
            var summary = BenchmarkRunner.Run<TinyAllocationBenchmark>(config);
            Assert.DoesNotContain(summary.ValidationErrors, static error => error.IsCritical);
            var report = Assert.Single(summary.Reports);
            Assert.True(report.Success);
            Assert.NotEmpty(Directory.EnumerateFiles(artifacts, "*.json", SearchOption.AllDirectories));

            Assert.Equal([(0, 1), (0, 2)], report.GetResultRuns()
                .Select(static measurement => (measurement.LaunchIndex, measurement.IterationIndex)));
            var snapshot = BenchmarkDotNetReportCollector.Collect(
                report, memoryDiagnoserEnabled: true, nativeLaunchIndexBase: 0);
            Assert.Equal(report.GcStats.TotalOperations, snapshot.GcStats.TotalOperations);
            Assert.Equal(report.GcStats.GetBytesAllocatedPerOperation(report.BenchmarkCase),
                snapshot.GcStats.BytesAllocatedPerOperation);
            Assert.True(snapshot.GcStats.TotalOperations > 0);
            Assert.NotNull(snapshot.GcStats.BytesAllocatedPerOperation);
            Assert.Equal(["Mean", "Median", "Allocated"], snapshot.ResultFields.Select(static field => field.Name));
            Assert.Equal([(0, 0), (0, 1)], snapshot.Measurements
                .Select(static measurement => (measurement.LaunchIndex, measurement.IterationIndex)));

            var template = TestData.Envelope(runtimeTfm: runtimeTfm);
            var collectedAt = DateTimeOffset.UtcNow;
            var coefficientOfVariation = LaunchHealthEvidence.ComputeCoefficientOfVariation(
                snapshot.Measurements.Select(static measurement => measurement.NanosecondsPerOperation),
                LaunchHealthRuleCatalog.Required);
            var metrics = new LaunchHealthMetrics(false, false, 0, false, coefficientOfVariation);
            var smoke = template with
            {
                CollectedAtUtc = collectedAt,
                BenchmarkJobContractHash = contract.IdentityHash,
                LaunchHealth =
                [
                    new LaunchHealthEvidence(0, collectedAt, LaunchHealthRuleCatalog.Required.IdentityHash,
                        metrics, LaunchHealthEvidence.DeriveStatus(metrics, LaunchHealthRuleCatalog.Required)),
                ],
                BenchmarkDotNet = template.BenchmarkDotNet with
                {
                    JobId = contract.Id,
                    LaunchCount = contract.LaunchCount,
                    WarmupIterations = contract.WarmupIterationFloor,
                    MeasurementIterations = contract.MeasurementIterationFloor,
                    MinIterationTimeMilliseconds = contract.MinIterationTimeMilliseconds,
                    MaxRelativeError = contract.MaxRelativeError,
                    GcStats = snapshot.GcStats,
                    RawStatistics = snapshot.Measurements.Select(static measurement => measurement.NanosecondsPerOperation).ToArray(),
                    Measurements = snapshot.Measurements,
                    ResultFields = snapshot.ResultFields,
                },
                Result = new ResultEvidence(
                    BenchmarkStatistics.Mean(snapshot.Measurements.Select(static measurement => measurement.NanosecondsPerOperation)),
                    BenchmarkAggregationCatalog.Required.ResultTimingUnit,
                    snapshot.GcStats.BytesAllocatedPerOperation,
                    BenchmarkAggregationCatalog.Required.ResultAllocationUnit),
            };
            Assert.Empty(EvidenceValidator.Validate(smoke, contract));
        }
        finally
        {
            if (Directory.Exists(artifacts)) Directory.Delete(artifacts, recursive: true);
        }
    }

    public class TinyAllocationBenchmark
    {
        [Benchmark]
        public byte[] Allocate() => new byte[64];
    }

    [Fact]
    public void NativeBenchmarkCoordinatesMustBeTheExactOneBasedConfiguredSet()
    {
        BenchmarkDotNetReportCollector.ValidateNativeCoordinates(
            [(2, 2), (1, 1), (2, 1), (1, 2)], expectedLaunchCount: 2, expectedIterationCount: 2);

        Assert.Throws<InvalidDataException>(() => BenchmarkDotNetReportCollector.ValidateNativeCoordinates(
            [(1, 1), (1, 3)], expectedLaunchCount: 1, expectedIterationCount: 2));
        Assert.Throws<InvalidDataException>(() => BenchmarkDotNetReportCollector.ValidateNativeCoordinates(
            [(0, 1), (1, 2)], expectedLaunchCount: 1, expectedIterationCount: 2));
        Assert.Throws<InvalidDataException>(() => BenchmarkDotNetReportCollector.ValidateNativeCoordinates(
            [(1, 1), (1, 1)], expectedLaunchCount: 1, expectedIterationCount: 2));
        Assert.Throws<InvalidDataException>(() => BenchmarkDotNetReportCollector.ValidateNativeCoordinates(
            [(1, 1)], expectedLaunchCount: 2, expectedIterationCount: 1));
    }

    private static (BenchmarkEvidenceEnvelope Evidence, EvidenceArtifactValidationContext Context)
        CreatePhysicalEvidenceFixture()
    {
        var metadata = typeof(EvidenceContractTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .ToDictionary(static attribute => attribute.Key, static attribute => attribute.Value!, StringComparer.Ordinal);
        var runtimeTfm = Environment.Version.Major == 8 ? "net8.0" : "net10.0";
        var repositoryRoot = FindRepositoryRoot();
        var nuGetPackageRoot = metadata["NuGetPackageRoot"];
        var userRoot = DeriveUserProfileRoot(nuGetPackageRoot);
        var selectedRoots = new List<SelectedAssetRoot>
        {
            new("repo", repositoryRoot),
            new("nuget", nuGetPackageRoot),
            new("dotnet", metadata["DotNetRoot"]),
            new("user", userRoot),
        };
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (Directory.Exists(programFilesX86)) selectedRoots.Add(new("programfilesx86", programFilesX86));
        var manifest = SourceArtifactManifestCatalog.GetRequired("sqlite", BenchmarkSourceMode.ProjectReference, runtimeTfm);
        var artifactRoot = Path.Combine(Path.GetTempPath(), $"inquiry-evidence-filesystem-{Guid.NewGuid():N}");
        Directory.CreateDirectory(artifactRoot);
        var selectedInput = Path.Combine(artifactRoot, "physical-selected-assets.tsv");
        var selectedLines = File.ReadAllLines(metadata["SelectedAssetsManifest"]);
        selectedLines[0] = selectedLines[0].Replace(metadata["RuntimeIdentifier"],
            SourceArtifactManifestCatalog.ReleaseRuntimeIdentifier, StringComparison.Ordinal);
        File.WriteAllLines(selectedInput, selectedLines);
        var resolved = ResolvedDependencyManifestCollector.Collect(
            selectedInput, metadata["ProjectAssetsFile"], "sqlite",
            BenchmarkSourceLane.DeveloperProject, runtimeTfm,
            SourceArtifactManifestCatalog.ReleaseRuntimeIdentifier, selectedRoots);
        var artifacts = new List<SourceArtifact>();
        var selectedArtifacts = resolved.FromSelectedAssets();
        var expectedArtifacts = manifest.ExpectedArtifacts.Concat(selectedArtifacts.Select(static artifact =>
            new SourceArtifactExpectation(artifact.Role, artifact.RelativeArtifactId)));
        foreach (var expected in expectedArtifacts)
        {
            var path = Path.Combine(artifactRoot,
                expected.RelativeArtifactId.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string sha;
            var selectedArtifact = selectedArtifacts.SingleOrDefault(artifact => artifact.Role == expected.Role &&
                StringComparer.Ordinal.Equals(artifact.RelativeArtifactId, expected.RelativeArtifactId));
            if (selectedArtifact is not null)
            {
                File.Copy(ResolveSelectedAssetPath(selectedArtifact.RelativeArtifactId, selectedRoots), path);
                sha = selectedArtifact.Sha256;
            }
            else switch (expected.Role)
            {
                case SourceArtifactRole.DependencyArtifact:
                    File.Copy(metadata["ProjectAssetsFile"], path);
                    sha = resolved.ProjectAssetsSha256;
                    break;
                case SourceArtifactRole.SelectedAssetsManifest:
                    File.Copy(selectedInput, path);
                    sha = resolved.SelectedAssetsManifestSha256;
                    break;
                case SourceArtifactRole.ResolvedDependencyManifest:
                    File.WriteAllBytes(path, resolved.ToCanonicalJsonBytes());
                    sha = resolved.ContentSha256;
                    break;
                case SourceArtifactRole.BenchmarkConfigFile:
                    File.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(BenchmarkJobCatalog.GetRequired(
                        runtimeTfm == "net8.0" ? "net8-live-v1" : "net10-live-v1"), EvidenceJson.Options));
                    sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)))
                        .ToLowerInvariant();
                    break;
                case SourceArtifactRole.PackageLockFile:
                    File.WriteAllText(path, "{\"version\":2,\"dependencies\":{}}");
                    sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)))
                        .ToLowerInvariant();
                    break;
                default:
                    throw new InvalidOperationException($"Physical fixture has no byte source for {expected.Role}.");
            }
            artifacts.Add(new SourceArtifact(expected.Role, expected.RelativeArtifactId, sha));
        }
        var source = BenchmarkSourceIdentity.Project(TestData.Commit, manifest.IdentityHash, resolved, artifacts);
        return (TestData.Envelope(source, "sqlite", runtimeTfm),
            new EvidenceArtifactValidationContext(artifactRoot, selectedRoots));

        static string FindRepositoryRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Inquiry.slnx")))
                directory = directory.Parent;
            return directory?.FullName ?? throw new InvalidOperationException("Could not locate repository root.");
        }

        static string ResolveSelectedAssetPath(string logicalAssetId, IReadOnlyList<SelectedAssetRoot> roots)
        {
            var separator = logicalAssetId.IndexOf('/');
            if (separator <= 0)
                throw new InvalidDataException($"Selected asset has no physical root identity: {logicalAssetId}");
            var rootId = logicalAssetId[..separator];
            var root = roots.Single(candidate => StringComparer.Ordinal.Equals(candidate.Id, rootId));
            var relative = logicalAssetId[(separator + 1)..].Replace('/', Path.DirectorySeparatorChar);
            var path = Path.GetFullPath(Path.Combine(root.Path, relative));
            if (!File.Exists(path))
                throw new FileNotFoundException($"Selected asset does not exist: {logicalAssetId}", path);
            return path;
        }
    }

    private static string DeriveUserProfileRoot(string nuGetPackageRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nuGetPackageRoot);

        var packages = new DirectoryInfo(Path.GetFullPath(nuGetPackageRoot));
        var nuGetDirectory = packages.Parent;
        var profileDirectory = nuGetDirectory?.Parent;
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (nuGetDirectory is null || profileDirectory is null ||
            !string.Equals(packages.Name, "packages", comparison) ||
            !string.Equals(nuGetDirectory.Name, ".nuget", comparison))
        {
            throw new InvalidOperationException(
                "NuGet package root must use the canonical '<profile>/.nuget/packages' layout.");
        }

        var profileRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(profileDirectory.FullName));
        var volumeRootPath = Path.GetPathRoot(profileRoot);
        var volumeRoot = volumeRootPath is null
            ? null
            : Path.TrimEndingDirectorySeparator(Path.GetFullPath(volumeRootPath));
        if (volumeRoot is null || string.Equals(profileRoot, volumeRoot, comparison))
        {
            throw new InvalidOperationException(
                "NuGet package root must be contained by a user profile, not a volume or share root.");
        }

        return profileDirectory.FullName;
    }
}
