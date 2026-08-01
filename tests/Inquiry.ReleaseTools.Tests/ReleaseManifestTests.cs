using System.Text.Json;
using System.Text.Json.Nodes;

namespace Inquiry.ReleaseTools.Tests;

public sealed class ReleaseManifestTests
{
    [Fact]
    public void Repository_manifest_is_valid()
    {
        PackageVerifier.VerifyManifest(RepositoryFixture.Root, Path.Combine(RepositoryFixture.Root, "eng", "release-manifest.json"));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("extra")]
    [InlineData("duplicate")]
    [InlineData("version")]
    [InlineData("dependency")]
    [InlineData("tfm")]
    [InlineData("project")]
    [InlineData("unknown")]
    [InlineData("null-assets")]
    [InlineData("casing")]
    [InlineData("repository-branch")]
    [InlineData("null-id")]
    [InlineData("empty-id")]
    public void Invalid_manifest_is_rejected(string mutation)
    {
        var path = MutatedManifest(mutation);
        try
        {
            Assert.Throws<ReleaseVerificationException>(() => PackageVerifier.VerifyManifest(RepositoryFixture.Root, path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Null_dependencies_reports_object_contract()
    {
        var path = MutatedManifest("null-dependencies");
        try
        {
            var exception = Assert.Throws<ReleaseVerificationException>(() =>
                PackageVerifier.VerifyManifest(RepositoryFixture.Root, path));

            Assert.Contains("dependencies must be a non-null object", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Bundle_with_missing_packages_is_rejected_before_publishable_metadata_is_considered()
    {
        var bundle = Directory.CreateTempSubdirectory("inquiry-empty-bundle-");
        try
        {
            Assert.Throws<ReleaseVerificationException>(() => PackageVerifier.VerifyBundle(
                RepositoryFixture.Root,
                Path.Combine(RepositoryFixture.Root, "eng", "release-manifest.json"),
                bundle.FullName,
                new string('a', 40)));
        }
        finally
        {
            bundle.Delete(recursive: true);
        }
    }

    [Fact]
    public void Bundle_root_reparse_point_is_rejected()
    {
        var target = Directory.CreateTempSubdirectory("inquiry-bundle-target-");
        var link = Path.Combine(Path.GetTempPath(), $"inquiry-bundle-link-{Guid.NewGuid():N}");
        try
        {
            try
            {
                Directory.CreateSymbolicLink(link, target.FullName);
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or PlatformNotSupportedException or IOException)
            {
                return;
            }

            Assert.Throws<ReleaseVerificationException>(() => PackageVerifier.VerifyBundle(
                RepositoryFixture.Root,
                Path.Combine(RepositoryFixture.Root, "eng", "release-manifest.json"),
                link,
                new string('a', 40)));
        }
        finally
        {
            if (Directory.Exists(link))
            {
                Directory.Delete(link);
            }
            target.Delete(recursive: true);
        }
    }

    [Fact]
    public void Bundle_directory_reparse_point_is_rejected_without_traversal()
    {
        var bundle = Directory.CreateTempSubdirectory("inquiry-bundle-inventory-");
        var target = Directory.CreateTempSubdirectory("inquiry-bundle-target-");
        var link = Path.Combine(bundle.FullName, "linked-directory");
        try
        {
            CreateExpectedEmptyBundle(bundle.FullName);
            File.WriteAllBytes(Path.Combine(target.FullName, "outside.txt"), []);
            try
            {
                Directory.CreateSymbolicLink(link, target.FullName);
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or PlatformNotSupportedException or IOException)
            {
                return;
            }

            var verificationException = Assert.Throws<ReleaseVerificationException>(() => PackageVerifier.VerifyBundle(
                RepositoryFixture.Root,
                Path.Combine(RepositoryFixture.Root, "eng", "release-manifest.json"),
                bundle.FullName,
                new string('a', 40)));

            Assert.Equal("Bundle must contain regular files only; directories and links are forbidden.", verificationException.Message);
        }
        finally
        {
            if (Directory.Exists(link))
            {
                Directory.Delete(link);
            }
            bundle.Delete(recursive: true);
            target.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("extra")]
    [InlineData("version")]
    [InlineData("nested")]
    [InlineData("wrong-case")]
    public void Bundle_inventory_drift_is_rejected(string mutation)
    {
        var bundle = Directory.CreateTempSubdirectory("inquiry-bundle-inventory-");
        try
        {
            CreateExpectedEmptyBundle(bundle.FullName);
            if (mutation == "extra")
            {
                File.WriteAllBytes(Path.Combine(bundle.FullName, "unexpected.txt"), []);
            }
            else if (mutation == "version")
            {
                File.Delete(Path.Combine(bundle.FullName, "Inquiry.1.0.0.nupkg"));
                File.WriteAllBytes(Path.Combine(bundle.FullName, "Inquiry.1.0.1.nupkg"), []);
            }
            else if (mutation == "nested")
            {
                Directory.CreateDirectory(Path.Combine(bundle.FullName, "nested"));
                File.WriteAllBytes(Path.Combine(bundle.FullName, "nested", "unexpected.txt"), []);
            }
            else
            {
                var source = Path.Combine(bundle.FullName, "Inquiry.1.0.0.nupkg");
                var temporary = Path.Combine(bundle.FullName, "case.tmp");
                File.Move(source, temporary);
                File.Move(temporary, Path.Combine(bundle.FullName, "inquiry.1.0.0.nupkg"));
            }

            Assert.Throws<ReleaseVerificationException>(() => PackageVerifier.VerifyBundle(
                RepositoryFixture.Root,
                Path.Combine(RepositoryFixture.Root, "eng", "release-manifest.json"),
                bundle.FullName,
                new string('a', 40)));
        }
        finally
        {
            bundle.Delete(recursive: true);
        }
    }

    [Fact]
    public void Unexpected_packable_project_anywhere_in_repository_is_rejected()
    {
        var repository = Directory.CreateTempSubdirectory("inquiry-packable-inventory-");
        try
        {
            var manifestPath = CreateSyntheticManifestRepository(repository.FullName);

            var unexpected = Path.Combine(repository.FullName, "tools", "Unexpected", "Unexpected.csproj");
            Directory.CreateDirectory(Path.GetDirectoryName(unexpected)!);
            File.WriteAllText(unexpected, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><IsPackable Condition=\"'$(Configuration)' == 'Release'\">true</IsPackable><IsPackable Condition=\"'$(Configuration)' != 'Release'\">false</IsPackable><PackageId>Unexpected</PackageId></PropertyGroup></Project>");

            Assert.Throws<ReleaseVerificationException>(() =>
                PackageVerifier.VerifyManifest(repository.FullName, manifestPath));
        }
        finally
        {
            repository.Delete(recursive: true);
        }
    }

    [Fact]
    public void Packable_project_inventory_does_not_descend_through_reparse_directories()
    {
        var repository = Directory.CreateTempSubdirectory("inquiry-packable-inventory-");
        var external = Directory.CreateTempSubdirectory("inquiry-external-project-");
        var link = Path.Combine(repository.FullName, "linked-projects");
        try
        {
            var manifestPath = CreateSyntheticManifestRepository(repository.FullName);
            var unexpected = Path.Combine(external.FullName, "Unexpected.csproj");
            File.WriteAllText(unexpected, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><IsPackable>true</IsPackable><PackageId>Unexpected</PackageId></PropertyGroup></Project>");
            try
            {
                Directory.CreateSymbolicLink(link, external.FullName);
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or PlatformNotSupportedException or IOException)
            {
                return;
            }

            PackageVerifier.VerifyManifest(repository.FullName, manifestPath);
        }
        finally
        {
            if (Directory.Exists(link))
            {
                Directory.Delete(link);
            }
            repository.Delete(recursive: true);
            external.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("ABCDEF0123456789ABCDEF0123456789ABCDEF01")]
    [InlineData("abcdef0")]
    [InlineData("not-a-commit")]
    public void Bundle_rejects_noncanonical_commit_identity(string commit)
    {
        var bundle = Directory.CreateTempSubdirectory("inquiry-empty-bundle-");
        try
        {
            Assert.Throws<ReleaseVerificationException>(() => PackageVerifier.VerifyBundle(
                RepositoryFixture.Root,
                Path.Combine(RepositoryFixture.Root, "eng", "release-manifest.json"),
                bundle.FullName,
                commit));
        }
        finally
        {
            bundle.Delete(recursive: true);
        }
    }

    private static string MutatedManifest(string mutation)
    {
        var sourcePath = Path.Combine(RepositoryFixture.Root, "eng", "release-manifest.json");
        var root = JsonNode.Parse(File.ReadAllText(sourcePath))!.AsObject();
        var packages = root["packages"]!.AsArray();
        switch (mutation)
        {
            case "missing":
                packages.RemoveAt(packages.Count - 1);
                break;
            case "extra":
                packages.Add(packages[0]!.DeepClone());
                packages[^1]!["id"] = "Inquiry.Unexpected";
                packages[^1]!["project"] = "src/Inquiry/Inquiry.csproj";
                break;
            case "duplicate":
                packages[1]!["id"] = packages[0]!["id"]!.GetValue<string>();
                break;
            case "version":
                root["packageVersion"] = "1.0.1";
                break;
            case "dependency":
                packages[0]!["dependencies"]!["Inquiry"] = "1.0.0";
                break;
            case "tfm":
                packages[0]!["libTfms"]!.AsArray().RemoveAt(0);
                break;
            case "project":
                packages[0]!["project"] = "../Inquiry.csproj";
                break;
            case "unknown":
                root["unexpectedProperty"] = true;
                break;
            case "null-assets":
                root["assets"] = null;
                break;
            case "casing":
                root["SchemaVersion"] = root["schemaVersion"]!.DeepClone();
                root.Remove("schemaVersion");
                break;
            case "repository-branch":
                root["assets"]!["repositoryBranch"] = "refs/heads/prerelease";
                break;
            case "null-id":
                packages[0]!["id"] = null;
                break;
            case "empty-id":
                packages[0]!["id"] = string.Empty;
                break;
            case "null-dependencies":
                packages[0]!["dependencies"] = null;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mutation));
        }

        var path = Path.Combine(Path.GetTempPath(), $"inquiry-release-manifest-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions(JsonSerializerOptions.Default) { WriteIndented = true }));
        return path;
    }

    private static string CreateSyntheticManifestRepository(string repository)
    {
        var sourceManifest = Path.Combine(RepositoryFixture.Root, "eng", "release-manifest.json");
        var manifestPath = Path.Combine(repository, "eng", "release-manifest.json");
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
        File.Copy(sourceManifest, manifestPath);
        var manifest = JsonNode.Parse(File.ReadAllText(sourceManifest))!.AsObject();
        foreach (var package in manifest["packages"]!.AsArray())
        {
            var projectPath = Path.Combine(repository, package!["project"]!.GetValue<string>());
            Directory.CreateDirectory(Path.GetDirectoryName(projectPath)!);
            var packageId = package!["id"]!.GetValue<string>();
            File.WriteAllText(projectPath, $"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><IsPackable>true</IsPackable><PackageId>{packageId}</PackageId></PropertyGroup></Project>");
        }

        return manifestPath;
    }

    private static void CreateExpectedEmptyBundle(string bundle)
    {
        var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(RepositoryFixture.Root, "eng", "release-manifest.json")))!.AsObject();
        foreach (var package in manifest["packages"]!.AsArray())
        {
            var id = package!["id"]!.GetValue<string>();
            File.WriteAllBytes(Path.Combine(bundle, $"{id}.1.0.0.nupkg"), []);
            File.WriteAllBytes(Path.Combine(bundle, $"{id}.1.0.0.snupkg"), []);
        }
    }
}
