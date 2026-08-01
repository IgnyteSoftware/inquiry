using System.Text.Json.Nodes;

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
    public void Release_workflow_publishes_from_branch_builds_never_tag_rebuilds()
    {
        var workflow = File.ReadAllText(Path.Combine(RepositoryFixture.Root, ".github", "workflows", "release.yml"));

        // Publishing must be driven by pushes to the protected branches; a tag trigger would
        // allow republishing bytes that were never produced by a verified branch build.
        Assert.Contains("branches: [main, prerelease]", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("tags:", workflow, StringComparison.Ordinal);
        Assert.Contains("./eng/pack-release.ps1", workflow, StringComparison.Ordinal);
        Assert.Contains("--skip-duplicate", workflow, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("missing-provider")]
    [InlineData("missing-tfm")]
    [InlineData("unexpected-key")]
    public void Malformed_contract_integration_matrix_is_rejected(string mutation)
    {
        var contract = JsonNode.Parse(File.ReadAllText(
            Path.Combine(RepositoryFixture.Root, "eng", "ci-required-v1.json")))!.AsObject();
        var integration = contract["requiredJobs"]!.AsArray()
            .Select(job => job!.AsObject())
            .Single(job => job["job"]!.GetValue<string>() == "integration");
        var matrix = integration["matrix"]!.AsObject();
        switch (mutation)
        {
            case "null":
                integration["matrix"] = null;
                break;
            case "missing-provider":
                matrix.Remove("provider");
                break;
            case "missing-tfm":
                matrix.Remove("tfm");
                break;
            case "unexpected-key":
                matrix["unexpected"] = new JsonArray();
                break;
        }

        var path = Path.Combine(Path.GetTempPath(), $"inquiry-ci-contract-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, contract.ToJsonString());
        try
        {
            var exception = Assert.Throws<ReleaseVerificationException>(() => CiContractVerifier.Verify(
                RepositoryFixture.Root,
                path,
                Path.Combine(RepositoryFixture.Root, ".github", "workflows", "ci.yml")));
            Assert.Contains("CI contract integration matrix", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("provider")]
    [InlineData("tfm")]
    public void Contract_and_workflow_cannot_drift_from_the_canonical_matrix_together(string dimension)
    {
        var contract = JsonNode.Parse(File.ReadAllText(
            Path.Combine(RepositoryFixture.Root, "eng", "ci-required-v1.json")))!.AsObject();
        var integration = contract["requiredJobs"]!.AsArray()
            .Select(job => job!.AsObject())
            .Single(job => job["job"]!.GetValue<string>() == "integration");
        var matrix = integration["matrix"]!.AsObject();
        var workflow = File.ReadAllText(Path.Combine(RepositoryFixture.Root, ".github", "workflows", "ci.yml"));
        if (dimension == "provider")
        {
            Assert.Contains("provider: [PostgreSql, MySql, MariaDb, SqlServer, Oracle]", workflow, StringComparison.Ordinal);
            matrix["provider"]!.AsArray()[4] = "SqlServer";
            workflow = workflow.Replace(
                "provider: [PostgreSql, MySql, MariaDb, SqlServer, Oracle]",
                "provider: [PostgreSql, MySql, MariaDb, SqlServer, SqlServer]",
                StringComparison.Ordinal);
        }
        else
        {
            Assert.Contains("tfm: [net8.0, net9.0, net10.0]", workflow, StringComparison.Ordinal);
            matrix["tfm"]!.AsArray()[2] = "net9.0";
            workflow = workflow.Replace(
                "tfm: [net8.0, net9.0, net10.0]",
                "tfm: [net8.0, net9.0, net9.0]",
                StringComparison.Ordinal);
        }

        var contractPath = Path.Combine(Path.GetTempPath(), $"inquiry-ci-contract-{Guid.NewGuid():N}.json");
        var workflowPath = Path.Combine(Path.GetTempPath(), $"inquiry-ci-{Guid.NewGuid():N}.yml");
        File.WriteAllText(contractPath, contract.ToJsonString());
        File.WriteAllText(workflowPath, workflow);
        try
        {
            Assert.Throws<ReleaseVerificationException>(() => CiContractVerifier.Verify(
                RepositoryFixture.Root, contractPath, workflowPath));
        }
        finally
        {
            File.Delete(contractPath);
            File.Delete(workflowPath);
        }
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
        var workflow = File.ReadAllText(Path.Combine(RepositoryFixture.Root, ".github", "workflows", "ci.yml"))
            .ReplaceLineEndings("\n");
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
