using System.Diagnostics;
using System.IO.Compression;
using System.Text;

namespace Inquiry.ReleaseTools.Tests;

public sealed class PackageAdversarialTests
{
    private static readonly Lazy<PackageFixture> Fixture = new(() => CreateFixture("Ignyte.Inquiry", "src/Inquiry/Inquiry.csproj"));
    private static readonly Lazy<PackageFixture> ProviderFixture = new(() => CreateFixture("Ignyte.Inquiry.Sqlite", "src/Inquiry.Sqlite/Inquiry.Sqlite.csproj"));

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

    [Theory]
    [InlineData("missing-provider-pdb")]
    [InlineData("provider-pdb-mismatch")]
    [InlineData("shared-pdb-mismatch")]
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

            Mutate(snupkg, archive =>
            {
                const string providerPdb = "lib/net8.0/Inquiry.Sqlite.Analyzer.pdb";
                const string sharedPdb = "lib/net8.0/Inquiry.Generators.Shared.pdb";
                switch (mutation)
                {
                    case "missing-provider-pdb":
                        archive.GetEntry(providerPdb)!.Delete();
                        break;
                    case "provider-pdb-mismatch":
                        Replace(archive, providerPdb, providerPdb, Read(archive, sharedPdb));
                        break;
                    case "shared-pdb-mismatch":
                        Replace(archive, sharedPdb, sharedPdb, Read(archive, providerPdb));
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
        var inputStamp = string.Join('-', new[] { "Directory.Build.props", "Directory.Build.targets", "README.md", "icon.png", project }
            .Select(path => File.GetLastWriteTimeUtc(Path.Combine(root, path)).Ticks));
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
                _ = Run(root, "dotnet", "pack", project, "--configuration", "Release", "--output", output,
                    "--no-restore", "--disable-build-servers", "-m:1", "-p:ContinuousIntegrationBuild=true",
                    "-p:MinVerVersionOverride=1.0.0", $"-p:RepositoryCommit={commit}");
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
