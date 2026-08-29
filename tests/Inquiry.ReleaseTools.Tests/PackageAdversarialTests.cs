using System.Diagnostics;
using System.IO.Compression;
using System.Text;

namespace Inquiry.ReleaseTools.Tests;

public sealed class PackageAdversarialTests
{
    private static readonly Lazy<PackageFixture> Fixture = new(() => CreateFixture("Ignyte.Inquiry", "src/Inquiry/Inquiry.csproj"));
    private static readonly Lazy<PackageFixture> AspireFixture = new(() => CreateFixture("Ignyte.Inquiry.Aspire", "src/Inquiry.Aspire/Inquiry.Aspire.csproj"));
    private static readonly Lazy<PackageFixture> ProviderFixture = new(() => CreateFixture("Ignyte.Inquiry.Sqlite", "src/Inquiry.Sqlite/Inquiry.Sqlite.csproj"));
    private static readonly Lazy<PackageFixture> SqlServerFixture = new(() => CreateFixture("Ignyte.Inquiry.SqlServer", "src/Inquiry.SqlServer/Inquiry.SqlServer.csproj"));

    [Theory]
    [InlineData("extra-entry")]
    [InlineData("wrong-case")]
    [InlineData("readme-bytes")]
    [InlineData("icon-bytes")]
    [InlineData("nuspec-element")]
    [InlineData("dependency-attribute")]
    [InlineData("pdb-dll-mismatch")]
    [InlineData("symlink-entry")]
    public void Package_bypass_attempts_are_rejected(string mutation)
    {
        var fixture = Fixture.Value;
        var directory = Directory.CreateTempSubdirectory("inquiry-package-mutation-");
        try
        {
            var nupkg = Path.Combine(directory.FullName, "Ignyte.Inquiry.1.0.0.nupkg");
            var snupkg = Path.Combine(directory.FullName, "Ignyte.Inquiry.1.0.0.snupkg");
            File.Copy(fixture.Nupkg, nupkg);
            File.Copy(fixture.Snupkg, snupkg);

            switch (mutation)
            {
                case "extra-entry":
                    Mutate(nupkg, archive => Write(archive, "unexpected.txt", "bypass"u8));
                    break;
                case "wrong-case":
                    Mutate(nupkg, archive => Replace(archive, "lib/net8.0/Inquiry.dll", "Lib/net8.0/Inquiry.dll"));
                    break;
                case "readme-bytes":
                    Mutate(nupkg, archive => Replace(archive, "README.md", "README.md", "not canonical"u8));
                    break;
                case "icon-bytes":
                    Mutate(nupkg, archive => Replace(archive, "icon.png", "icon.png", "not canonical"u8));
                    break;
                case "nuspec-element":
                    Mutate(nupkg, archive => ReplaceText(archive, "Ignyte.Inquiry.nuspec", "</metadata>", "<unexpected /></metadata>"));
                    break;
                case "dependency-attribute":
                    Mutate(nupkg, archive => ReplaceText(archive, "Ignyte.Inquiry.nuspec", "exclude=\"Build,Analyzers\"", "exclude=\"Build,Analyzers\" unexpected=\"true\""));
                    break;
                case "pdb-dll-mismatch":
                    Mutate(snupkg, archive => Replace(archive, "lib/net8.0/Inquiry.pdb", "lib/net8.0/Inquiry.pdb", Read(archive, "lib/net9.0/Inquiry.pdb")));
                    break;
                case "symlink-entry":
                    Mutate(nupkg, archive => archive.GetEntry("README.md")!.ExternalAttributes = unchecked((int)0xA1FF0000));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(mutation));
            }

            Assert.Throws<ReleaseVerificationException>(() => PackageVerifier.VerifyPackagePairForTests(
                RepositoryFixture.Root,
                Path.Combine(RepositoryFixture.Root, "eng", "release-manifest.json"),
                directory.FullName,
                "Ignyte.Inquiry",
                fixture.Commit));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void Provider_snupkg_contains_only_lib_pdbs_and_the_pair_verifies()
    {
        // The exact layout nuget.org rejected in 1.0.0-preview.6: analyzer PDBs riding in the snupkg
        // at lib/net8.0 with no matching lib/ DLL in the nupkg. Analyzer symbols are embedded in the
        // analyzer assemblies instead, so the snupkg must carry lib PDBs ONLY — and the unmutated
        // pair must still pass the verifier, which now checks the embedded PDBs.
        var fixture = ProviderFixture.Value;
        using (var archive = ZipFile.OpenRead(fixture.Snupkg))
        {
            var pdbs = archive.Entries.Select(entry => entry.FullName)
                .Where(path => path.EndsWith(".pdb", StringComparison.Ordinal))
                .Order(StringComparer.Ordinal).ToArray();
            Assert.Equal(
                ["lib/net10.0/Inquiry.Sqlite.pdb", "lib/net8.0/Inquiry.Sqlite.pdb", "lib/net9.0/Inquiry.Sqlite.pdb"],
                pdbs);
        }

        PackageVerifier.VerifyPackagePairForTests(
            RepositoryFixture.Root,
            Path.Combine(RepositoryFixture.Root, "eng", "release-manifest.json"),
            Path.GetDirectoryName(fixture.Nupkg)!,
            "Ignyte.Inquiry.Sqlite",
            fixture.Commit);
    }

    [Fact]
    public void Aspire_package_pair_verifies()
    {
        var fixture = AspireFixture.Value;

        PackageVerifier.VerifyPackagePairForTests(
            RepositoryFixture.Root,
            Path.Combine(RepositoryFixture.Root, "eng", "release-manifest.json"),
            Path.GetDirectoryName(fixture.Nupkg)!,
            "Ignyte.Inquiry.Aspire",
            fixture.Commit);
    }

    [Fact]
    public void Non_Oracle_package_cannot_include_System_Configuration_compile_assets()
    {
        var fixture = SqlServerFixture.Value;
        var directory = Directory.CreateTempSubdirectory("inquiry-system-configuration-mutation-");
        try
        {
            var nupkg = Path.Combine(directory.FullName, "Ignyte.Inquiry.SqlServer.1.0.0.nupkg");
            var snupkg = Path.Combine(directory.FullName, "Ignyte.Inquiry.SqlServer.1.0.0.snupkg");
            File.Copy(fixture.Nupkg, nupkg);
            File.Copy(fixture.Snupkg, snupkg);
            Mutate(nupkg, archive => ReplaceText(
                archive,
                "Ignyte.Inquiry.SqlServer.nuspec",
                "exclude=\"Compile,Build,Analyzers\"",
                "exclude=\"Build,Analyzers\""));

            Assert.Throws<ReleaseVerificationException>(() => PackageVerifier.VerifyPackagePairForTests(
                RepositoryFixture.Root,
                Path.Combine(RepositoryFixture.Root, "eng", "release-manifest.json"),
                directory.FullName,
                "Ignyte.Inquiry.SqlServer",
                fixture.Commit));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("analyzer-content-not-an-analyzer")]
    [InlineData("analyzer-shared-swap")]
    public void Analyzer_symbol_bypass_attempts_are_rejected(string mutation)
    {
        var fixture = ProviderFixture.Value;
        var directory = Directory.CreateTempSubdirectory("inquiry-analyzer-symbol-mutation-");
        try
        {
            var nupkg = Path.Combine(directory.FullName, "Ignyte.Inquiry.Sqlite.1.0.0.nupkg");
            var snupkg = Path.Combine(directory.FullName, "Ignyte.Inquiry.Sqlite.1.0.0.snupkg");
            File.Copy(fixture.Nupkg, nupkg);
            File.Copy(fixture.Snupkg, snupkg);

            Mutate(nupkg, archive =>
            {
                const string providerAnalyzer = "analyzers/dotnet/cs/Inquiry.Sqlite.Analyzer.dll";
                const string sharedAnalyzer = "analyzers/dotnet/cs/Inquiry.Generators.Shared.dll";
                switch (mutation)
                {
                    case "analyzer-content-not-an-analyzer":
                        // A lib assembly has an external (not embedded) PDB and a different assembly
                        // name; either property must reject it standing in for the analyzer.
                        Replace(archive, providerAnalyzer, providerAnalyzer, Read(archive, "lib/net8.0/Inquiry.Sqlite.dll"));
                        break;
                    case "analyzer-shared-swap":
                        // Content-swapped analyzer DLLs pass every version and SourceLink check on
                        // their own; the assembly-name-equals-file-name rule is what catches them.
                        var provider = Read(archive, providerAnalyzer);
                        var shared = Read(archive, sharedAnalyzer);
                        Replace(archive, providerAnalyzer, providerAnalyzer, shared);
                        Replace(archive, sharedAnalyzer, sharedAnalyzer, provider);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(mutation));
                }
            });

            Assert.Throws<ReleaseVerificationException>(() => PackageVerifier.VerifyPackagePairForTests(
                RepositoryFixture.Root,
                Path.Combine(RepositoryFixture.Root, "eng", "release-manifest.json"),
                directory.FullName,
                "Ignyte.Inquiry.Sqlite",
                fixture.Commit));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static PackageFixture CreateFixture(string packageId, string project)
    {
        var root = RepositoryFixture.Root;
        var commit = Run(root, "git", "rev-parse", "HEAD").Trim();
        var inputs = new[] { "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props", "README.md", "icon.png", project }
            .Select(path => File.GetLastWriteTimeUtc(Path.Combine(root, path)).Ticks)
            .ToList();
        var assetsFile = Path.Combine(root, Path.GetDirectoryName(project)!, "obj", "project.assets.json");
        if (File.Exists(assetsFile))
        {
            inputs.Add(File.GetLastWriteTimeUtc(assetsFile).Ticks);
        }

        var inputStamp = string.Join('-', inputs);
        var output = Path.Combine(Path.GetTempPath(), "inquiry-release-tools-fixture", packageId + "-" + commit + "-" + inputStamp);
        using var fixtureLock = new Mutex(false, "Inquiry.ReleaseTools.PackageFixture." + packageId + "." + commit + "." + inputStamp);
        Assert.True(fixtureLock.WaitOne(TimeSpan.FromMinutes(2)), "Timed out waiting for the shared package fixture.");
        try
        {
            var nupkg = Path.Combine(output, $"{packageId}.1.0.0.nupkg");
            var snupkg = Path.Combine(output, $"{packageId}.1.0.0.snupkg");
            if (!File.Exists(nupkg) || !File.Exists(snupkg))
            {
                if (Directory.Exists(output))
                {
                    Directory.Delete(output, recursive: true);
                }
                Directory.CreateDirectory(output);
                // RepositoryBranch is pinned so the fixture verifies positively from any local
                // checkout — the manifest requires refs/heads/main in the nuspec.
                _ = Run(root, "dotnet", "pack", project, "--configuration", "Release", "--output", output,
                    "--no-restore", "--disable-build-servers", "-m:1", "-p:ContinuousIntegrationBuild=true",
                    "-p:MinVerVersionOverride=1.0.0", $"-p:RepositoryCommit={commit}",
                    "-p:RepositoryBranch=refs/heads/main");
            }
        }
        finally
        {
            fixtureLock.ReleaseMutex();
        }
        return new PackageFixture(
            Path.Combine(output, $"{packageId}.1.0.0.nupkg"),
            Path.Combine(output, $"{packageId}.1.0.0.snupkg"),
            commit);
    }

    private static string Run(string workingDirectory, string fileName, params string[] arguments)
    {
        var start = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }
        var result = BoundedProcess.Run(start, TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(10));
        Assert.False(result.TimedOut,
            $"{fileName} timed out; killRequested={result.ProcessTreeKillRequested}; rootExited={result.RootExited}; " +
            $"streamsDrained={result.StreamsDrained}; killError={result.KillError ?? "none"}; " +
            $"stdoutTruncated={result.StandardOutputTruncated}; stderrTruncated={result.StandardErrorTruncated}\n" +
            $"{result.StandardError}\n{result.StandardOutput}");
        Assert.True(result.StreamsDrained,
            $"{fileName} exited, but redirected streams remained open; a descendant may still hold inherited handles. " +
            $"{result.StandardError}\n{result.StandardOutput}");
        Assert.False(result.StandardOutputTruncated,
            $"{fileName} standard output exceeded the bounded capture and cannot be returned completely.");
        Assert.True(result.ExitCode == 0,
            $"{fileName} failed with exit code {result.ExitCode}: {result.StandardError}\n{result.StandardOutput}");
        return result.StandardOutput;
    }

    private static void Mutate(string path, Action<ZipArchive> mutation)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Update);
        mutation(archive);
    }

    private static void ReplaceText(ZipArchive archive, string path, string oldValue, string newValue)
    {
        var text = Encoding.UTF8.GetString(Read(archive, path));
        Assert.Contains(oldValue, text, StringComparison.Ordinal);
        Replace(archive, path, path, Encoding.UTF8.GetBytes(text.Replace(oldValue, newValue, StringComparison.Ordinal)));
    }

    private static void Replace(ZipArchive archive, string oldPath, string newPath, ReadOnlySpan<byte> bytes = default)
    {
        var replacement = bytes.IsEmpty ? Read(archive, oldPath) : bytes.ToArray();
        archive.GetEntry(oldPath)!.Delete();
        Write(archive, newPath, replacement);
    }

    private static byte[] Read(ZipArchive archive, string path)
    {
        using var stream = archive.GetEntry(path)!.Open();
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    private static void Write(ZipArchive archive, string path, ReadOnlySpan<byte> bytes)
    {
        using var stream = archive.CreateEntry(path).Open();
        stream.Write(bytes);
    }

    private sealed record PackageFixture(string Nupkg, string Snupkg, string Commit);
}
