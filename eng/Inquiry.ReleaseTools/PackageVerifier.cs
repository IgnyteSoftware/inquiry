using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace Inquiry.ReleaseTools;

public static class PackageVerifier
{
    private static readonly TimeSpan ProjectEvaluationTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan ProcessTerminationTimeout = TimeSpan.FromSeconds(10);
    private static readonly ConcurrentDictionary<string, Lazy<IReadOnlyDictionary<string, (bool IsPackable, string PackageId)>>> ProjectEvaluationCache = new(StringComparer.Ordinal);
    // Packages ship under the Ignyte.* prefix because the bare "Inquiry" ID is taken on
    // nuget.org; assemblies keep their canonical Inquiry.* names.
    private const string PackageIdPrefix = "Ignyte.";
    private static readonly string[] RequiredPackageIds =
    [
        "Ignyte.Inquiry",
        "Ignyte.Inquiry.AspNetCore",
        "Ignyte.Inquiry.Interceptors",
        "Ignyte.Inquiry.MariaDb",
        "Ignyte.Inquiry.MySql",
        "Ignyte.Inquiry.Oracle",
        "Ignyte.Inquiry.PostgreSql",
        "Ignyte.Inquiry.Sqlite",
        "Ignyte.Inquiry.SqlServer",
        "Ignyte.Inquiry.Testing"
    ];

    private static readonly string[] RequiredTfms = ["net8.0", "net9.0", "net10.0"];
    private static readonly EnumerationOptions ProjectEnumerationOptions = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = false,
        AttributesToSkip = FileAttributes.ReparsePoint
    };
    private static readonly Guid SourceLinkKind = new("CC110556-A091-4D38-9FEC-25AB9A351A6A");

    public static void VerifyManifest(string repositoryRoot, string manifestPath)
    {
        var root = Path.GetFullPath(repositoryRoot);
        var manifest = ReleaseTool.ReadManifest(Path.GetFullPath(manifestPath, root));
        var packages = manifest.Packages ?? throw new ReleaseVerificationException("packages must be a non-null array.");
        var assets = manifest.Assets ?? throw new ReleaseVerificationException("assets must be a non-null object.");
        Require(packages.All(package => package is not null), "packages must contain only package objects.");
        Require(manifest.SchemaVersion == "inquiry-release-v1", "schemaVersion must be inquiry-release-v1.");
        Require(IsStableSemVer(manifest.PackageVersion), "packageVersion must be a stable MAJOR.MINOR.PATCH version.");
        Require(manifest.Tag == $"v{manifest.PackageVersion}", "tag must equal v plus packageVersion.");
        Require(packages.Count == RequiredPackageIds.Length, $"The manifest must contain exactly {RequiredPackageIds.Length} packages.");
        foreach (var package in packages)
        {
            Require(!string.IsNullOrWhiteSpace(package.Id), "Package ID cannot be empty.");
        }

        Require(packages.Select(package => package.Id).Distinct(StringComparer.Ordinal).Count() == packages.Count,
            "The manifest contains duplicate package IDs.");

        var packagesById = packages.ToDictionary(package => package.Id, StringComparer.Ordinal);
        RequireSequence(packagesById.Keys.Order(StringComparer.Ordinal), RequiredPackageIds.Order(StringComparer.Ordinal), "package IDs");

        foreach (var package in packages)
        {
            var dependencies = package.Dependencies ?? throw new ReleaseVerificationException($"{package.Id} dependencies must be a non-null object.");
            var libTfms = package.LibTfms ?? throw new ReleaseVerificationException($"{package.Id} libTfms must be an array.");
            var analyzers = package.Analyzers ?? throw new ReleaseVerificationException($"{package.Id} analyzers must be an array.");
            var frameworkReferences = package.FrameworkReferences ?? throw new ReleaseVerificationException($"{package.Id} frameworkReferences must be an array.");
            Require(frameworkReferences.All(reference => !string.IsNullOrWhiteSpace(reference)), $"{package.Id} frameworkReferences must contain only non-empty strings.");
            Require(frameworkReferences.Distinct(StringComparer.Ordinal).Count() == frameworkReferences.Count, $"{package.Id} has duplicate framework references.");
            foreach (var pruned in package.PrunedDependencies ?? new Dictionary<string, IReadOnlyList<string>>())
            {
                Require(frameworkReferences.Count > 0, $"{package.Id} prunedDependencies requires a framework reference to prune against.");
                Require(package.LibTfms.Contains(pruned.Key, StringComparer.Ordinal), $"{package.Id} prunes dependencies for unknown TFM {pruned.Key}.");
                Require(pruned.Value is not null && pruned.Value.All(id => package.Dependencies.ContainsKey(id)),
                    $"{package.Id} prunes dependencies that are not declared for {pruned.Key}.");
            }
            Require(dependencies.All(dependency => !string.IsNullOrWhiteSpace(dependency.Key) && !string.IsNullOrWhiteSpace(dependency.Value)),
                $"{package.Id} dependencies must map non-empty IDs to exact versions.");
            Require(libTfms.All(tfm => tfm is not null), $"{package.Id} libTfms must contain only strings.");
            Require(analyzers.All(analyzer => analyzer is not null), $"{package.Id} analyzers must contain only strings.");
            ValidateRelativePath(package.Project, "project");
            var projectPath = Path.GetFullPath(package.Project, root);
            Require(IsWithin(root, projectPath), $"Project path escapes the repository: {package.Project}");
            Require(File.Exists(projectPath), $"Project does not exist: {package.Project}");
            Require(PackageIdPrefix + Path.GetFileNameWithoutExtension(projectPath) == package.Id,
                $"Package ID {package.Id} must be {PackageIdPrefix} plus its project name.");
            RequireSequence(libTfms.Order(StringComparer.Ordinal), RequiredTfms.Order(StringComparer.Ordinal), $"{package.Id} lib TFMs");
            Require(analyzers.Distinct(StringComparer.Ordinal).Count() == analyzers.Count, $"{package.Id} has duplicate analyzer assets.");

            foreach (var dependency in dependencies.Keys.Where(packagesById.ContainsKey))
            {
                Require(dependency != package.Id, $"{package.Id} cannot depend on itself.");
                Require(dependencies[dependency] == manifest.PackageVersion,
                    $"{package.Id} dependency {dependency} must equal package version {manifest.PackageVersion}.");
            }
        }

        VerifyDependencyGraph(packages);
        VerifyPackableProjects(root, packagesById);
        Require(assets.LicenseExpression == "MIT", "license expression must be MIT.");
        Require(assets.Readme == "README.md", "readme must be README.md.");
        Require(assets.Icon == "icon.png", "icon must be icon.png.");
        Require(Uri.TryCreate(assets.RepositoryUrl, UriKind.Absolute, out var repositoryUri)
            && repositoryUri.Scheme == Uri.UriSchemeHttps, "repositoryUrl must be an absolute HTTPS URL.");
        Require(assets.RepositoryBranch == "refs/heads/main", "repositoryBranch must be exactly refs/heads/main.");
        Require(assets.RequireSymbols, "Symbol packages are required.");
        Require(assets.RequireSourceLink, "SourceLink is required.");
    }

    public static void VerifyBundle(
        string repositoryRoot,
        string manifestPath,
        string bundleDirectory,
        string expectedCommit,
        string? expectedTag = null,
        string? expectedVersion = null,
        string? expectedBranch = null)
    {
        VerifyManifest(repositoryRoot, manifestPath);
        var root = Path.GetFullPath(repositoryRoot);
        var manifest = ReleaseTool.ReadManifest(Path.GetFullPath(manifestPath, root));
        var version = expectedVersion ?? manifest.PackageVersion;
        var branch = expectedBranch ?? manifest.Assets.RepositoryBranch;
        Require(version == manifest.PackageVersion || IsPreviewOf(manifest.PackageVersion, version),
            $"Expected version {version} must equal the manifest version {manifest.PackageVersion} or be one of its -preview.N versions.");
        Require(branch.StartsWith("refs/heads/", StringComparison.Ordinal) && branch.Length > "refs/heads/".Length,
            $"Expected branch {branch} must be a fully qualified refs/heads/ reference.");
        var bundle = Path.GetFullPath(bundleDirectory, root);
        Require(Directory.Exists(bundle), $"Bundle directory does not exist: {bundle}");
        Require((File.GetAttributes(bundle) & FileAttributes.ReparsePoint) == 0,
            "Bundle root must be a real directory, not a reparse point or symbolic link.");
        Require(IsLowerHex(expectedCommit, 40) || IsLowerHex(expectedCommit, 64), "Expected commit must be a complete lowercase 40- or 64-character hexadecimal object ID.");
        if (expectedTag is not null)
        {
            Require(expectedTag == manifest.Tag, $"Tag {expectedTag} does not equal manifest tag {manifest.Tag}.");
            Require(version == manifest.PackageVersion, "Tagged releases must use the exact manifest version.");
        }

        var expectedFiles = manifest.Packages
            .SelectMany(package => new[]
            {
                $"{package.Id}.{version}.nupkg",
                $"{package.Id}.{version}.snupkg"
            })
            .Append("sbom.cdx.json")
            .Order(StringComparer.Ordinal)
            .ToArray();
        var fileSystemEntries = Directory.EnumerateFileSystemEntries(bundle, "*", SearchOption.TopDirectoryOnly).ToArray();
        Require(fileSystemEntries.All(path => File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0),
            "Bundle must contain regular files only; directories and links are forbidden.");
        var actualFiles = fileSystemEntries.Select(path => Normalize(Path.GetRelativePath(bundle, path))).Order(StringComparer.Ordinal).ToArray();
        Require(actualFiles.All(path => !path.Contains('/')), "Bundle files must all be at its root.");
        Require(actualFiles.Distinct(StringComparer.OrdinalIgnoreCase).Count() == actualFiles.Length,
            "Bundle contains duplicate file names under case-insensitive comparison.");
        RequireSequence(actualFiles, expectedFiles, "exact bundle inventory");

        foreach (var package in manifest.Packages)
        {
            var nupkg = Path.Combine(bundle, $"{package.Id}.{version}.nupkg");
            var snupkg = Path.Combine(bundle, $"{package.Id}.{version}.snupkg");
            var debugIdentities = VerifyNupkg(root, nupkg, package, manifest, expectedCommit, version, branch);
            VerifySnupkg(snupkg, package, manifest, expectedCommit, debugIdentities, version, branch);
        }

        VerifySbom(Path.Combine(bundle, "sbom.cdx.json"));
    }

    internal static void VerifyPackagePairForTests(
        string repositoryRoot,
        string manifestPath,
        string bundleDirectory,
        string packageId,
        string expectedCommit)
    {
        var root = Path.GetFullPath(repositoryRoot);
        var manifest = ReleaseTool.ReadManifest(Path.GetFullPath(manifestPath, root));
        var package = manifest.Packages.Single(item => item.Id == packageId);
        var nupkg = Path.Combine(bundleDirectory, $"{package.Id}.{manifest.PackageVersion}.nupkg");
        var snupkg = Path.Combine(bundleDirectory, $"{package.Id}.{manifest.PackageVersion}.snupkg");
        var identities = VerifyNupkg(root, nupkg, package, manifest, expectedCommit, manifest.PackageVersion, manifest.Assets.RepositoryBranch);
        VerifySnupkg(snupkg, package, manifest, expectedCommit, identities, manifest.PackageVersion, manifest.Assets.RepositoryBranch);
    }

    private static void VerifyPackableProjects(string root, IReadOnlyDictionary<string, ReleasePackage> packagesById)
    {
        var expectedPaths = packagesById.Values
            .Select(package => Normalize(package.Project))
            .ToHashSet(StringComparer.Ordinal);
        var actualPaths = new HashSet<string>(StringComparer.Ordinal);

        var projectPaths = Directory.EnumerateFiles(root, "*.csproj", ProjectEnumerationOptions)
            .Where(path => !IsBuildOrWorktreePath(root, path))
            .Order(StringComparer.Ordinal)
            .ToArray();
        var signature = string.Join('|', projectPaths.Select(path =>
        {
            var info = new FileInfo(path);
            return $"{Normalize(Path.GetRelativePath(root, path))}:{info.Length}:{info.LastWriteTimeUtc.Ticks}";
        }));
        var evaluated = ProjectEvaluationCache.GetOrAdd(root + "\n" + signature, _ => new Lazy<IReadOnlyDictionary<string, (bool IsPackable, string PackageId)>>(
            () => projectPaths.ToDictionary(path => path, EvaluateProject, StringComparer.OrdinalIgnoreCase),
            LazyThreadSafetyMode.ExecutionAndPublication)).Value;

        foreach (var projectPath in projectPaths)
        {
            var properties = evaluated[projectPath];
            if (properties.IsPackable)
            {
                Require(properties.PackageId == PackageIdPrefix + Path.GetFileNameWithoutExtension(projectPath),
                    $"Packable project {projectPath} must use {PackageIdPrefix} plus its canonical project name as PackageId.");
                actualPaths.Add(Normalize(Path.GetRelativePath(root, projectPath)));
            }
        }

        RequireSequence(actualPaths.Order(StringComparer.Ordinal), expectedPaths.Order(StringComparer.Ordinal), "packable project inventory");
    }

    private static bool IsBuildOrWorktreePath(string root, string path)
    {
        var relative = Normalize(Path.GetRelativePath(root, path));
        var segments = relative.Split('/');
        return segments.Any(segment => segment is ".git" or ".claude" or ".artifacts" or "artifacts" or "bin" or "obj");
    }

    private static (bool IsPackable, string PackageId) EvaluateProject(string projectPath)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        start.ArgumentList.Add("msbuild");
        start.ArgumentList.Add(projectPath);
        start.ArgumentList.Add("-nologo");
        start.ArgumentList.Add("-property:Configuration=Release");
        start.ArgumentList.Add("-property:ContinuousIntegrationBuild=true");
        start.ArgumentList.Add("-getProperty:IsPackable,PackageId");
        BoundedProcessResult result;
        try
        {
            result = BoundedProcess.Run(start, ProjectEvaluationTimeout, ProcessTerminationTimeout);
        }
        catch (Exception exception) when (exception is IOException or Win32Exception or InvalidOperationException)
        {
            throw new ReleaseVerificationException($"Could not evaluate {projectPath}: {exception.Message}");
        }
        Require(!result.TimedOut,
            $"MSBuild evaluation timed out after {ProjectEvaluationTimeout.TotalSeconds:0} seconds for {projectPath}. " +
            $"Process-tree kill requested: {result.ProcessTreeKillRequested}; root exited: {result.RootExited}; " +
            $"streams drained: {result.StreamsDrained}; kill error: {result.KillError ?? "none"}. " +
            $"stderr truncated: {result.StandardErrorTruncated}; stderr: {DiagnosticTail(result.StandardError)}; " +
            $"stdout: {DiagnosticTail(result.StandardOutput)}");
        Require(result.StreamsDrained,
            $"MSBuild evaluation exited for {projectPath}, but its redirected output streams did not close within " +
            $"{ProcessTerminationTimeout.TotalSeconds:0} seconds. A descendant process may still hold inherited handles. " +
            $"stderr: {DiagnosticTail(result.StandardError)}; stdout: {DiagnosticTail(result.StandardOutput)}");
        Require(!result.StandardOutputTruncated,
            $"MSBuild evaluation output exceeded the {BoundedProcess.MaximumCapturedCharacters}-character capture limit " +
            $"for {projectPath}; refusing to parse truncated property JSON. stdout tail: {DiagnosticTail(result.StandardOutput)}");
        Require(result.ExitCode == 0,
            $"MSBuild evaluation failed for {projectPath} with exit code {result.ExitCode}: {DiagnosticTail(result.StandardError)}");
        try
        {
            using var json = JsonDocument.Parse(result.StandardOutput);
            var properties = json.RootElement.GetProperty("Properties");
            var isPackable = properties.GetProperty("IsPackable").GetString();
            var packageId = properties.GetProperty("PackageId").GetString();
            Require(isPackable is "true" or "false" && !string.IsNullOrWhiteSpace(packageId),
                $"MSBuild returned an incomplete effective package inventory for {projectPath}.");
            return (isPackable == "true", packageId!);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new ReleaseVerificationException($"MSBuild returned invalid effective properties for {projectPath}: {exception.Message}");
        }
    }

    private static void VerifyDependencyGraph(IReadOnlyList<ReleasePackage> packages)
    {
        var byId = packages.ToDictionary(package => package.Id, StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);

        void Visit(string id)
        {
            if (visited.Contains(id))
            {
                return;
            }

            Require(visiting.Add(id), $"Package dependency cycle includes {id}.");
            foreach (var dependency in byId[id].Dependencies.Keys.Where(byId.ContainsKey))
            {
                Visit(dependency);
            }

            visiting.Remove(id);
            visited.Add(id);
        }

        foreach (var package in packages)
        {
            Visit(package.Id);
        }
    }

    private static IReadOnlyDictionary<string, DebugIdentity> VerifyNupkg(string root, string packagePath, ReleasePackage package, ReleaseManifest manifest, string expectedCommit, string version, string branch)
    {
        try
        {
            using var archive = ZipFile.OpenRead(packagePath);
            var entries = ValidateZipEntries(archive, package.Id);
            var coreProperties = ResolveCoreProperties(entries, package.Id);
            RequireSequence(entries.Order(StringComparer.Ordinal), ExpectedNupkgEntries(package, manifest, coreProperties).Order(StringComparer.Ordinal), $"{package.Id} nupkg entries");
            var readmeEntry = archive.GetEntry(manifest.Assets.Readme);
            Require(readmeEntry is not null, $"{package.Id} is missing {manifest.Assets.Readme}.");
            VerifyCanonicalBytes(readmeEntry!, Path.Combine(root, manifest.Assets.Readme), $"{package.Id} readme");
            var iconEntry = archive.GetEntry(manifest.Assets.Icon);
            Require(iconEntry is not null, $"{package.Id} is missing {manifest.Assets.Icon}.");
            VerifyCanonicalBytes(iconEntry!, Path.Combine(root, manifest.Assets.Icon), $"{package.Id} icon");
            VerifyPngIcon(iconEntry!, package.Id);

            var metadata = ReadNuspecMetadata(archive, package, symbols: false);
            VerifyNuspecIdentity(metadata, package, manifest, expectedCommit, version, branch);
            Require(Element(metadata, "license") == manifest.Assets.LicenseExpression, $"{package.Id} license mismatch.");
            Require(Element(metadata, "authors") == "Ignyte Software Inc.", $"{package.Id} authors mismatch.");
            Require(Element(metadata, "licenseUrl") == "https://licenses.nuget.org/MIT", $"{package.Id} license URL mismatch.");
            Require(Element(metadata, "readme") == manifest.Assets.Readme, $"{package.Id} readme metadata mismatch.");
            Require(Element(metadata, "icon") == manifest.Assets.Icon, $"{package.Id} icon metadata mismatch.");
            Require(!string.IsNullOrWhiteSpace(Element(metadata, "description")), $"{package.Id} description is empty.");

            var dependenciesElement = metadata.Elements().Single(element => element.Name.LocalName == "dependencies");
            var dependencyGroups = dependenciesElement.Elements().Where(element => element.Name.LocalName == "group").ToArray();
            RequireSequence(
                dependencyGroups.Select(group => (string?)group.Attribute("targetFramework") ?? string.Empty).Order(StringComparer.Ordinal),
                package.LibTfms.Order(StringComparer.Ordinal),
                $"{package.Id} dependency TFMs");
            foreach (var group in dependencyGroups)
            {
                var groupTfm = (string?)group.Attribute("targetFramework") ?? string.Empty;
                var dependencies = group.Elements().Where(element => element.Name.LocalName == "dependency").ToArray();
                var ids = dependencies.Select(element => (string?)element.Attribute("id") ?? string.Empty).ToArray();
                Require(ids.Distinct(StringComparer.Ordinal).Count() == ids.Length,
                    $"{package.Id} dependency group {groupTfm} contains duplicate IDs.");
                var actual = dependencies.ToDictionary(
                    element => (string?)element.Attribute("id") ?? string.Empty,
                    element => (string?)element.Attribute("version") ?? string.Empty,
                    StringComparer.Ordinal);
                var prunedIds = package.PrunedDependencies?.GetValueOrDefault(groupTfm) ?? [];
                var expectedIds = package.Dependencies.Keys.Where(id => !prunedIds.Contains(id, StringComparer.Ordinal)).ToArray();
                RequireSequence(actual.Keys.Order(StringComparer.Ordinal), expectedIds.Order(StringComparer.Ordinal),
                    $"{package.Id} dependencies for {groupTfm}");
                var familyIds = manifest.Packages.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
                foreach (var dependency in package.Dependencies.Where(item => actual.ContainsKey(item.Key)))
                {
                    // Sibling packages are packed at the effective (possibly -preview.N) version;
                    // external dependencies stay at their manifest-pinned versions.
                    var expectedDependencyVersion = familyIds.Contains(dependency.Key) ? version : dependency.Value;
                    Require(actual[dependency.Key] == expectedDependencyVersion,
                        $"{package.Id} dependency {dependency.Key} must be {expectedDependencyVersion}; found {actual[dependency.Key]}.");
                }
            }

            var assemblyName = AssemblyName(package);
            var expectedLibDlls = package.LibTfms.Select(tfm => $"lib/{tfm}/{assemblyName}.dll").Order(StringComparer.Ordinal).ToArray();
            var expectedLibAssets = expectedLibDlls
                .Concat(package.LibTfms.Select(tfm => $"lib/{tfm}/{assemblyName}.xml"))
                .Order(StringComparer.Ordinal).ToArray();
            var actualLibAssets = entries.Where(path => path.StartsWith("lib/", StringComparison.Ordinal)).Order(StringComparer.Ordinal).ToArray();
            RequireSequence(actualLibAssets, expectedLibAssets, $"{package.Id} lib assets");
            var analyzerDlls = entries.Where(path => path.StartsWith("analyzers/dotnet/cs/", StringComparison.Ordinal) && path.EndsWith(".dll", StringComparison.Ordinal))
                .Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray();
            RequireSequence(analyzerDlls!, package.Analyzers.Order(StringComparer.Ordinal), $"{package.Id} analyzer assets");

            var identities = new Dictionary<string, DebugIdentity>(StringComparer.Ordinal);
            foreach (var dllPath in expectedLibDlls)
            {
                identities.Add(Path.ChangeExtension(dllPath, ".pdb"),
                    VerifyAssemblyVersion(archive.GetEntry(dllPath)!, version, expectedCommit));
            }

            // Analyzer assemblies carry their symbols EMBEDDED (DebugType=embedded): a loose analyzer
            // PDB cannot ship in the snupkg because nuget.org symbol validation requires every snupkg
            // PDB to match a lib/ DLL, and these live under analyzers/dotnet/cs. Verified here, in the
            // nupkg, instead of registering a snupkg expectation.
            foreach (var analyzer in package.Analyzers)
            {
                var dllPath = $"analyzers/dotnet/cs/{analyzer}";
                var identity = VerifyAssemblyVersion(archive.GetEntry(dllPath)!, version, expectedCommit);
                VerifyEmbeddedAnalyzerSymbols(archive.GetEntry(dllPath)!, package.Id, manifest.Assets.RepositoryUrl, expectedCommit, identity);
            }
            return identities;
        }
        catch (ReleaseVerificationException)
        {
            throw;
        }
        catch (Exception exception) when (exception is InvalidDataException or InvalidOperationException or BadImageFormatException or JsonException)
        {
            throw new ReleaseVerificationException($"{package.Id} nupkg is malformed: {exception.Message}");
        }
    }

    private static void VerifySnupkg(string packagePath, ReleasePackage package, ReleaseManifest manifest, string expectedCommit,
        IReadOnlyDictionary<string, DebugIdentity> debugIdentities, string version, string branch)
    {
        try
        {
            using var archive = ZipFile.OpenRead(packagePath);
            var entries = ValidateZipEntries(archive, package.Id);
            var coreProperties = ResolveCoreProperties(entries, package.Id);
            RequireSequence(entries.Order(StringComparer.Ordinal), ExpectedSnupkgEntries(package, coreProperties).Order(StringComparer.Ordinal), $"{package.Id} snupkg entries");
            var metadata = ReadNuspecMetadata(archive, package, symbols: true);
            VerifyNuspecIdentity(metadata, package, manifest, expectedCommit, version, branch);
            var expectedPdbs = package.LibTfms.Select(tfm => $"lib/{tfm}/{AssemblyName(package)}.pdb")
                .Order(StringComparer.Ordinal).ToArray();
            var actualPdbs = entries.Where(path => path.EndsWith(".pdb", StringComparison.Ordinal))
                .Order(StringComparer.Ordinal).ToArray();
            RequireSequence(actualPdbs, expectedPdbs, $"{package.Id} symbol assets");
            foreach (var pdbPath in expectedPdbs)
            {
                VerifySourceLink(archive.GetEntry(pdbPath)!, package.Id, manifest.Assets.RepositoryUrl, expectedCommit,
                    debugIdentities[pdbPath]);
            }
        }
        catch (ReleaseVerificationException)
        {
            throw;
        }
        catch (Exception exception) when (exception is InvalidDataException or InvalidOperationException or BadImageFormatException or JsonException)
        {
            throw new ReleaseVerificationException($"{package.Id} snupkg is malformed: {exception.Message}");
        }
    }

    private static void VerifySbom(string sbomPath)
    {
        Require(File.Exists(sbomPath), "Bundle is missing the CycloneDX SBOM (sbom.cdx.json).");
        try
        {
            using var stream = File.OpenRead(sbomPath);
            using var document = JsonDocument.Parse(stream);
            var root = document.RootElement;
            Require(root.TryGetProperty("bomFormat", out var format) && format.GetString() == "CycloneDX",
                "SBOM bomFormat must be CycloneDX.");
            Require(root.TryGetProperty("specVersion", out _), "SBOM must declare a specVersion.");
            Require(root.TryGetProperty("components", out var components) && components.ValueKind == JsonValueKind.Array && components.GetArrayLength() > 0,
                "SBOM must contain a non-empty components array.");
        }
        catch (JsonException exception)
        {
            throw new ReleaseVerificationException($"SBOM is invalid JSON: {exception.Message}");
        }
    }

    private static XElement ReadNuspecMetadata(ZipArchive archive, ReleasePackage package, bool symbols)
    {
        var packageId = package.Id;
        var entry = archive.GetEntry($"{packageId}.nuspec");
        Require(entry is not null, $"{packageId} must contain the exact canonical nuspec name.");
        using var stream = entry!.Open();
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        var document = XDocument.Load(reader, LoadOptions.None);
        Require(document.Root is not null && document.Root.Name.LocalName == "package"
            && document.Root.Attributes().Count() == 1
            && document.Root.Attributes().Single().IsNamespaceDeclaration
            && document.Root.Attributes().Single().Value == "http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd",
            $"{packageId} nuspec root must be an unadorned package element.");
        var root = document.Root!;
        var packageChildren = root.Elements().ToArray();
        Require(packageChildren.Length == 1 && packageChildren[0].Name.LocalName == "metadata",
            $"{packageId} nuspec package must contain only metadata.");
        var metadata = packageChildren[0];
        Require(!metadata.HasAttributes, $"{packageId} nuspec metadata must not have attributes.");
        var allowed = symbols
            ? new[] { "id", "version", "projectUrl", "description", "tags", "packageTypes", "repository", "dependencies" }
            : new[] { "id", "version", "authors", "license", "licenseUrl", "icon", "readme", "projectUrl", "description", "tags", "repository", "dependencies" };
        if (package.FrameworkReferences.Count > 0)
        {
            allowed = [.. allowed, "frameworkReferences"];
        }
        RequireSequence(metadata.Elements().Select(element => element.Name.LocalName), allowed, $"{packageId} nuspec metadata structure");
        Require(metadata.Elements().All(element => element.Name.Namespace == metadata.Name.Namespace), $"{packageId} nuspec mixes XML namespaces.");
        foreach (var scalar in metadata.Elements().Where(element => element.Name.LocalName is not ("license" or "repository" or "dependencies" or "packageTypes" or "frameworkReferences")))
        {
            Require(!scalar.HasAttributes && !scalar.HasElements, $"{packageId} nuspec {scalar.Name.LocalName} must be a plain scalar element.");
        }
        if (symbols)
        {
            var packageTypes = metadata.Elements().Single(element => element.Name.LocalName == "packageTypes");
            Require(!packageTypes.HasAttributes && packageTypes.Elements().Count() == 1, $"{packageId} symbols packageTypes structure is invalid.");
            var packageType = packageTypes.Elements().Single();
            Require(packageType.Name.LocalName == "packageType" && packageType.Attributes().Select(attribute => attribute.Name.LocalName).SequenceEqual(["name"])
                && (string?)packageType.Attribute("name") == "SymbolsPackage", $"{packageId} must declare only SymbolsPackage.");
        }
        return metadata;
    }

    private static void VerifyNuspecIdentity(
        XElement metadata,
        ReleasePackage package,
        ReleaseManifest manifest,
        string expectedCommit,
        string version,
        string branch)
    {
        var packageId = package.Id;
        Require(Element(metadata, "id") == packageId, $"{packageId} nuspec ID mismatch.");
        Require(Element(metadata, "version") == version, $"{packageId} nuspec version mismatch.");
        Require(Element(metadata, "projectUrl") == manifest.Assets.RepositoryUrl, $"{packageId} project URL mismatch.");
        Require(Element(metadata, "tags") == "micro-orm source-generator ado.net sql", $"{packageId} tags mismatch.");
        Require(!string.IsNullOrWhiteSpace(Element(metadata, "description")), $"{packageId} description is empty.");
        var license = metadata.Elements().SingleOrDefault(element => element.Name.LocalName == "license");
        Require(license is null || license.Attributes().Select(attribute => attribute.Name.LocalName).SequenceEqual(["type"])
            && (string?)license.Attribute("type") == "expression", $"{packageId} license must be an expression.");
        var repositories = metadata.Elements().Where(element => element.Name.LocalName == "repository").ToArray();
        Require(repositories.Length == 1, $"{packageId} nuspec must contain exactly one repository element.");
        RequireSequence(repositories[0].Attributes().Select(attribute => attribute.Name.LocalName), ["type", "url", "branch", "commit"], $"{packageId} repository attributes");
        Require((string?)repositories[0].Attribute("url") == manifest.Assets.RepositoryUrl, $"{packageId} repository URL mismatch.");
        Require((string?)repositories[0].Attribute("type") == "git", $"{packageId} repository type must be git.");
        Require((string?)repositories[0].Attribute("branch") == branch,
            $"{packageId} repository branch mismatch.");
        Require((string?)repositories[0].Attribute("commit") == expectedCommit, $"{packageId} repository commit mismatch.");

        var dependencies = metadata.Elements().Single(element => element.Name.LocalName == "dependencies");
        Require(!dependencies.HasAttributes, $"{packageId} dependencies must not have attributes.");
        foreach (var group in dependencies.Elements())
        {
            Require(group.Name.LocalName == "group" && group.Name.Namespace == metadata.Name.Namespace
                && group.Attributes().Select(attribute => attribute.Name.LocalName).SequenceEqual(["targetFramework"]),
                $"{packageId} dependency group has invalid structure.");
            foreach (var dependency in group.Elements())
            {
                Require(dependency.Name.LocalName == "dependency" && dependency.Name.Namespace == metadata.Name.Namespace
                    && !dependency.HasElements && string.IsNullOrWhiteSpace(dependency.Value),
                    $"{packageId} dependency has invalid child content.");
                RequireSequence(dependency.Attributes().Select(attribute => attribute.Name.LocalName), ["id", "version", "exclude"],
                    $"{packageId} dependency attributes");
                var dependencyId = (string?)dependency.Attribute("id");
                var expectedExclude = packageId != "Ignyte.Inquiry.Oracle" && dependencyId == "System.Configuration.ConfigurationManager"
                    ? "Compile,Build,Analyzers"
                    : "Build,Analyzers";
                Require((string?)dependency.Attribute("exclude") == expectedExclude,
                    $"{packageId} dependency {dependencyId} must use the exact exclude contract {expectedExclude}.");
            }
        }

        var frameworkReferenceRoots = metadata.Elements().Where(element => element.Name.LocalName == "frameworkReferences").ToArray();
        if (package.FrameworkReferences.Count == 0)
        {
            Require(frameworkReferenceRoots.Length == 0, $"{packageId} must not declare framework references.");
            return;
        }

        Require(frameworkReferenceRoots.Length == 1, $"{packageId} must contain exactly one frameworkReferences element.");
        Require(!frameworkReferenceRoots[0].HasAttributes, $"{packageId} frameworkReferences must not have attributes.");
        var frameworkGroups = frameworkReferenceRoots[0].Elements().ToArray();
        RequireSequence(
            frameworkGroups.Select(group => (string?)group.Attribute("targetFramework") ?? string.Empty).Order(StringComparer.Ordinal),
            package.LibTfms.Order(StringComparer.Ordinal),
            $"{packageId} framework reference TFMs");
        foreach (var group in frameworkGroups)
        {
            Require(group.Name.LocalName == "group" && group.Name.Namespace == metadata.Name.Namespace
                && group.Attributes().Select(attribute => attribute.Name.LocalName).SequenceEqual(["targetFramework"]),
                $"{packageId} framework reference group has invalid structure.");
            RequireSequence(
                group.Elements().Select(reference =>
                {
                    Require(reference.Name.LocalName == "frameworkReference" && reference.Name.Namespace == metadata.Name.Namespace
                        && !reference.HasElements && string.IsNullOrWhiteSpace(reference.Value)
                        && reference.Attributes().Select(attribute => attribute.Name.LocalName).SequenceEqual(["name"]),
                        $"{packageId} framework reference has invalid structure.");
                    return (string?)reference.Attribute("name") ?? string.Empty;
                }).Order(StringComparer.Ordinal),
                package.FrameworkReferences.Order(StringComparer.Ordinal),
                $"{packageId} framework references for {(string?)group.Attribute("targetFramework")}");
        }
    }

    private static void VerifyPngIcon(ZipArchiveEntry entry, string packageId)
    {
        Span<byte> header = stackalloc byte[24];
        using var stream = entry.Open();
        stream.ReadExactly(header);
        ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
        Require(header[..8].SequenceEqual(signature), $"{packageId} icon is not a PNG.");
        Require(BinaryPrimitives.ReadUInt32BigEndian(header[8..12]) == 13
            && header[12..16].SequenceEqual("IHDR"u8), $"{packageId} icon has an invalid PNG header.");
        var width = BinaryPrimitives.ReadUInt32BigEndian(header[16..20]);
        var height = BinaryPrimitives.ReadUInt32BigEndian(header[20..24]);
        Require(width >= 128 && height >= 128, $"{packageId} icon must be at least 128x128; found {width}x{height}.");
    }

    private static DebugIdentity VerifyAssemblyVersion(ZipArchiveEntry entry, string packageVersion, string expectedCommit)
    {
        using var stream = entry.Open();
        using var image = new MemoryStream();
        stream.CopyTo(image);
        image.Position = 0;
        using var peReader = new PEReader(image);
        Require(peReader.HasMetadata, $"{entry.FullName} has no managed metadata.");
        var reader = peReader.GetMetadataReader();
        var assembly = reader.GetAssemblyDefinition();
        // MinVer stamps AssemblyVersion as MAJOR.0.0.0 and FileVersion as MAJOR.MINOR.PATCH.0.
        var baseVersion = Version.Parse(packageVersion.Split('-')[0]);
        var expectedAssemblyVersion = new Version(baseVersion.Major, 0, 0, 0);
        Require(assembly.Version == expectedAssemblyVersion,
            $"{entry.FullName} assembly version must be {expectedAssemblyVersion}; found {assembly.Version}.");

        var informational = ReadStringAttribute(reader, assembly.GetCustomAttributes(), "System.Reflection.AssemblyInformationalVersionAttribute");
        Require(informational == $"{packageVersion}+{expectedCommit}",
            $"{entry.FullName} informational version must be {packageVersion}+{expectedCommit}; found {informational ?? "<missing>"}.");
        var fileVersion = ReadStringAttribute(reader, assembly.GetCustomAttributes(), "System.Reflection.AssemblyFileVersionAttribute");
        var expectedFileVersion = $"{baseVersion.Major}.{baseVersion.Minor}.{baseVersion.Build}.0";
        Require(fileVersion == expectedFileVersion, $"{entry.FullName} file version must be {expectedFileVersion}; found {fileVersion ?? "<missing>"}.");
        var codeViewEntries = peReader.ReadDebugDirectory().Where(item => item.Type == DebugDirectoryEntryType.CodeView).ToArray();
        Require(codeViewEntries.Length == 1, $"{entry.FullName} must contain exactly one CodeView debug identity.");
        var codeView = peReader.ReadCodeViewDebugDirectoryData(codeViewEntries[0]);
        Require(codeView.Age == 1, $"{entry.FullName} must use a portable PDB CodeView age of 1.");
        return new DebugIdentity(codeView.Guid, codeViewEntries[0].Stamp);
    }

    private static string? ReadStringAttribute(MetadataReader reader, CustomAttributeHandleCollection attributes, string fullName)
    {
        foreach (var handle in attributes)
        {
            var attribute = reader.GetCustomAttribute(handle);
            var constructor = attribute.Constructor;
            EntityHandle typeHandle;
            if (constructor.Kind == HandleKind.MemberReference)
            {
                typeHandle = reader.GetMemberReference((MemberReferenceHandle)constructor).Parent;
            }
            else
            {
                typeHandle = reader.GetMethodDefinition((MethodDefinitionHandle)constructor).GetDeclaringType();
            }

            var actualName = typeHandle.Kind switch
            {
                HandleKind.TypeReference => FullName(reader, reader.GetTypeReference((TypeReferenceHandle)typeHandle)),
                HandleKind.TypeDefinition => FullName(reader, reader.GetTypeDefinition((TypeDefinitionHandle)typeHandle)),
                _ => string.Empty
            };
            if (actualName != fullName)
            {
                continue;
            }

            var blob = reader.GetBlobBytes(attribute.Value);
            Require(blob.Length >= 3 && BinaryPrimitives.ReadUInt16LittleEndian(blob) == 1, $"Malformed {fullName} value.");
            var offset = 2;
            return ReadSerializedString(blob, ref offset);
        }

        return null;
    }

    private static void VerifySourceLink(ZipArchiveEntry entry, string packageId, string repositoryUrl, string expectedCommit, DebugIdentity expectedIdentity)
    {
        using var stream = entry.Open();
        using var image = new MemoryStream();
        stream.CopyTo(image);
        image.Position = 0;
        using var pdb = MetadataReaderProvider.FromPortablePdbStream(image, MetadataStreamOptions.LeaveOpen);
        VerifyPortablePdb(pdb.GetMetadataReader(), entry.FullName, packageId, repositoryUrl, expectedCommit, expectedIdentity);
    }

    /// <summary>
    /// Verifies an analyzer assembly's EMBEDDED portable PDB: analyzer symbols cannot ship as loose
    /// snupkg PDBs (nuget.org symbol validation requires every snupkg PDB to match a lib/ DLL, and
    /// analyzer DLLs live under analyzers/dotnet/cs), so the same identity and SourceLink guarantees
    /// are enforced against the PDB embedded in the DLL itself. The assembly's metadata name must also
    /// equal its file name — the loose-PDB layout used to catch a content-swapped analyzer DLL through
    /// the PDB/DLL identity cross-check, and this restores that property for the embedded layout.
    /// </summary>
    private static void VerifyEmbeddedAnalyzerSymbols(ZipArchiveEntry entry, string packageId, string repositoryUrl, string expectedCommit, DebugIdentity expectedIdentity)
    {
        using var stream = entry.Open();
        using var image = new MemoryStream();
        stream.CopyTo(image);
        image.Position = 0;
        using var peReader = new PEReader(image);
        var assemblyName = peReader.GetMetadataReader().GetString(peReader.GetMetadataReader().GetAssemblyDefinition().Name);
        Require(assemblyName == Path.GetFileNameWithoutExtension(entry.FullName),
            $"{entry.FullName} assembly name '{assemblyName}' does not match its file name.");
        var embedded = peReader.ReadDebugDirectory().Where(item => item.Type == DebugDirectoryEntryType.EmbeddedPortablePdb).ToArray();
        Require(embedded.Length == 1, $"{entry.FullName} must contain exactly one embedded portable PDB.");
        using var pdb = peReader.ReadEmbeddedPortablePdbDebugDirectoryData(embedded[0]);
        VerifyPortablePdb(pdb.GetMetadataReader(), $"{entry.FullName} (embedded PDB)", packageId, repositoryUrl, expectedCommit, expectedIdentity);
    }

    private static void VerifyPortablePdb(MetadataReader reader, string subject, string packageId, string repositoryUrl, string expectedCommit, DebugIdentity expectedIdentity)
    {
        var pdbId = reader.DebugMetadataHeader?.Id
            ?? throw new ReleaseVerificationException($"{subject} has no portable PDB debug metadata header.");
        Require(pdbId.Length == 20, $"{subject} has an invalid portable PDB ID.");
        Require(new Guid(pdbId.AsSpan(0, 16)) == expectedIdentity.Guid
            && BinaryPrimitives.ReadUInt32LittleEndian(pdbId.AsSpan(16, 4)) == expectedIdentity.Stamp,
            $"{subject} does not match its packaged DLL CodeView identity.");
        var documentNames = reader.Documents.Select(handle => reader.GetString(reader.GetDocument(handle).Name)).ToArray();
        Require(documentNames.Length > 0 && documentNames.Distinct(StringComparer.Ordinal).Count() == documentNames.Length,
            $"{subject} must contain a non-empty unique document inventory.");
        foreach (var handle in reader.GetCustomDebugInformation(EntityHandle.ModuleDefinition))
        {
            var debug = reader.GetCustomDebugInformation(handle);
            if (reader.GetGuid(debug.Kind) != SourceLinkKind)
            {
                continue;
            }

            using var sourceLink = JsonDocument.Parse(reader.GetBlobBytes(debug.Value));
            Require(sourceLink.RootElement.TryGetProperty("documents", out var documents)
                && documents.ValueKind == JsonValueKind.Object, $"{packageId} SourceLink has no documents map.");
            var repository = new Uri(repositoryUrl, UriKind.Absolute);
            var expectedRawPrefix = $"https://raw.githubusercontent.com{repository.AbsolutePath.TrimEnd('/')}/{expectedCommit}/";
            var mappings = documents.EnumerateObject().ToArray();
            Require(mappings.Length > 0, $"{packageId} SourceLink documents map is empty.");
            foreach (var mapping in mappings)
            {
                Require(mapping.Value.ValueKind == JsonValueKind.String
                    && mapping.Value.GetString()!.StartsWith(expectedRawPrefix, StringComparison.Ordinal)
                    && mapping.Value.GetString()!.EndsWith('*'),
                    $"{packageId} SourceLink mapping '{mapping.Name}' is not bound to {expectedRawPrefix}.");
            }
            foreach (var document in documentNames)
            {
                var matches = mappings.Where(mapping => SourceLinkMatch(mapping.Name, document, out _)).ToArray();
                Require(matches.Length == 1, $"{subject} document '{document}' must match exactly one SourceLink mapping.");
                _ = SourceLinkMatch(matches[0].Name, document, out var suffix);
                Require(!suffix.Split('/', '\\').Any(segment => segment is "" or "." or ".."),
                    $"{subject} document '{document}' has an unsafe SourceLink suffix.");
                var targetPattern = matches[0].Value.GetString()!;
                Require(targetPattern.Count(character => character == '*') == 1
                    && targetPattern.Replace("*", suffix.Replace('\\', '/'), StringComparison.Ordinal) == expectedRawPrefix + suffix.Replace('\\', '/'),
                    $"{subject} document '{document}' does not resolve to the exact repository commit URL.");
            }
            foreach (var mapping in mappings)
            {
                Require(documentNames.Any(document => SourceLinkMatch(mapping.Name, document, out _)),
                    $"{subject} contains an unused SourceLink mapping '{mapping.Name}'.");
            }
            return;
        }

        throw new ReleaseVerificationException($"{packageId} is missing SourceLink data in {subject}.");
    }

    private static bool SourceLinkMatch(string pattern, string document, out string suffix)
    {
        suffix = string.Empty;
        var wildcard = pattern.IndexOf('*');
        if (wildcard < 0)
        {
            return pattern == document;
        }
        if (wildcard != pattern.LastIndexOf('*') || !document.StartsWith(pattern[..wildcard], StringComparison.Ordinal)
            || !document.EndsWith(pattern[(wildcard + 1)..], StringComparison.Ordinal))
        {
            return false;
        }
        suffix = document[wildcard..(document.Length - pattern.Length + wildcard + 1)];
        return true;
    }

    private static string[] ValidateZipEntries(ZipArchive archive, string packageId)
    {
        foreach (var entry in archive.Entries)
        {
            var unixType = (entry.ExternalAttributes >> 16) & 0xf000;
            var windowsAttributes = entry.ExternalAttributes & 0xffff;
            Require((unixType == 0 || unixType == 0x8000) &&
                    (windowsAttributes & ((int)FileAttributes.Directory | (int)FileAttributes.ReparsePoint)) == 0,
                $"{packageId} contains a non-regular or linked ZIP entry: {entry.FullName}.");
        }
        var entries = archive.Entries.Select(entry => entry.FullName).ToArray();
        Require(entries.All(path => path.Length > 0 && !path.EndsWith('/') && !path.Contains('\\') && !path.StartsWith('/')
            && !path.Contains(':') && path.Split('/').All(segment => segment is not "" and not "." and not "..")),
            $"{packageId} contains an unsafe or non-canonical ZIP entry path.");
        Require(entries.Distinct(StringComparer.OrdinalIgnoreCase).Count() == entries.Length,
            $"{packageId} contains duplicate ZIP entry paths under case-insensitive comparison.");
        return entries;
    }

    private static IEnumerable<string> ExpectedNupkgEntries(ReleasePackage package, ReleaseManifest manifest, string coreProperties)
    {
        yield return "_rels/.rels";
        yield return $"{package.Id}.nuspec";
        yield return manifest.Assets.Readme;
        yield return manifest.Assets.Icon;
        foreach (var tfm in package.LibTfms)
        {
            yield return $"lib/{tfm}/{AssemblyName(package)}.dll";
            yield return $"lib/{tfm}/{AssemblyName(package)}.xml";
        }
        foreach (var analyzer in package.Analyzers)
        {
            yield return $"analyzers/dotnet/cs/{analyzer}";
        }
        yield return "[Content_Types].xml";
        yield return coreProperties;
    }

    private static IEnumerable<string> ExpectedSnupkgEntries(ReleasePackage package, string coreProperties)
    {
        yield return "_rels/.rels";
        yield return $"{package.Id}.nuspec";
        foreach (var tfm in package.LibTfms)
        {
            yield return $"lib/{tfm}/{AssemblyName(package)}.pdb";
        }
        yield return "[Content_Types].xml";
        yield return coreProperties;
    }

    private static string ResolveCoreProperties(IEnumerable<string> entries, string packageId)
    {
        const string prefix = "package/services/metadata/core-properties/";
        const string suffix = ".psmdcp";
        var matches = entries.Where(path => path.StartsWith(prefix, StringComparison.Ordinal)).ToArray();
        Require(matches.Length == 1, $"{packageId} must contain exactly one core-properties entry.");
        Require(matches[0].Length == prefix.Length + 32 + suffix.Length && matches[0].EndsWith(suffix, StringComparison.Ordinal),
            $"{packageId} core-properties entry has an invalid shape.");
        var name = matches[0][prefix.Length..^suffix.Length];
        Require(IsLowerHex(name, 32),
            $"{packageId} core-properties entry must use NuGet's canonical lowercase 32-hex name.");
        return matches[0];
    }

    private static void VerifyCanonicalBytes(ZipArchiveEntry entry, string canonicalPath, string subject)
    {
        Require(File.Exists(canonicalPath), $"Canonical asset is missing: {canonicalPath}.");
        using var stream = entry.Open();
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        Require(memory.ToArray().AsSpan().SequenceEqual(File.ReadAllBytes(canonicalPath)), $"{subject} bytes differ from the repository canonical asset.");
    }

    private readonly record struct DebugIdentity(Guid Guid, uint Stamp);

    private static string FullName(MetadataReader reader, TypeReference type) => $"{reader.GetString(type.Namespace)}.{reader.GetString(type.Name)}";

    private static string FullName(MetadataReader reader, TypeDefinition type) => $"{reader.GetString(type.Namespace)}.{reader.GetString(type.Name)}";

    private static string? ReadSerializedString(ReadOnlySpan<byte> blob, ref int offset)
    {
        if (blob[offset] == 0xff)
        {
            offset++;
            return null;
        }

        var length = ReadCompressedInteger(blob, ref offset);
        Require(offset + length <= blob.Length, "Malformed serialized string.");
        var value = Encoding.UTF8.GetString(blob.Slice(offset, length));
        offset += length;
        return value;
    }

    private static int ReadCompressedInteger(ReadOnlySpan<byte> blob, ref int offset)
    {
        Require(offset < blob.Length, "Malformed compressed integer.");
        var first = blob[offset++];
        if ((first & 0x80) == 0)
        {
            return first;
        }

        if ((first & 0xc0) == 0x80)
        {
            Require(offset < blob.Length, "Malformed compressed integer.");
            return ((first & 0x3f) << 8) | blob[offset++];
        }

        Require(offset + 2 < blob.Length, "Malformed compressed integer.");
        return ((first & 0x1f) << 24) | (blob[offset++] << 16) | (blob[offset++] << 8) | blob[offset++];
    }

    private static string Element(XElement parent, string localName) =>
        parent.Elements().Single(element => element.Name.LocalName == localName).Value;

    private static bool IsLowerHex(string value, int length) =>
        value.Length == length && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static string AssemblyName(ReleasePackage package) => Path.GetFileNameWithoutExtension(package.Project);

    private static bool IsStableSemVer(string version) =>
        Version.TryParse(version, out var parsed) && parsed.Major >= 0 && parsed.Minor >= 0 && parsed.Build >= 0 && parsed.Revision == -1
        && version == $"{parsed.Major}.{parsed.Minor}.{parsed.Build}";

    private static bool IsPreviewOf(string stableVersion, string candidate)
    {
        var prefix = stableVersion + "-preview.";
        return candidate.StartsWith(prefix, StringComparison.Ordinal)
            && candidate.Length > prefix.Length
            && candidate[prefix.Length..].All(character => character is >= '0' and <= '9')
            && (candidate[prefix.Length] != '0' || candidate.Length == prefix.Length + 1);
    }

    private static string DiagnosticTail(string value)
    {
        const int maximumLength = 4_096;
        var trimmed = value.Trim();
        return trimmed.Length <= maximumLength ? trimmed : "..." + trimmed[^maximumLength..];
    }

    private static void ValidateRelativePath(string path, string name)
    {
        Require(!string.IsNullOrWhiteSpace(path), $"{name} path cannot be empty.");
        Require(!Path.IsPathRooted(path), $"{name} path must be repository-relative: {path}");
        Require(!path.Split('/', '\\').Contains("..", StringComparer.Ordinal), $"{name} path cannot contain '..': {path}");
        Require(path == Normalize(path), $"{name} path must use canonical forward slashes: {path}");
    }

    private static bool IsWithin(string root, string path) =>
        path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static bool IsWithinOrEqual(string root, string path) =>
        string.Equals(root.TrimEnd(Path.DirectorySeparatorChar), path.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
        || IsWithin(root, path);

    private static string Normalize(string path) => path.Replace('\\', '/');

    private static void RequireSequence<T>(IEnumerable<T> actual, IEnumerable<T> expected, string subject)
    {
        var actualArray = actual.ToArray();
        var expectedArray = expected.ToArray();
        Require(actualArray.SequenceEqual(expectedArray), $"Unexpected {subject}. Expected [{string.Join(", ", expectedArray)}], found [{string.Join(", ", actualArray)}].");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new ReleaseVerificationException(message);
        }
    }
}
