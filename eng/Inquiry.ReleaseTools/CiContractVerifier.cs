using System.Text.Json;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Inquiry.ReleaseTools;

public static class CiContractVerifier
{
    private static readonly IReadOnlyDictionary<string, string> ApprovedActions = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["actions/checkout"] = "9c091bb21b7c1c1d1991bb908d89e4e9dddfe3e0",
        ["actions/setup-dotnet"] = "26b0ec14cb23fa6904739307f278c14f94c95bf1",
        ["actions/upload-artifact"] = "043fb46d1a93c77aae656e7c1c64a875d1fc6a0a",
        ["actions/download-artifact"] = "3e5f45b2cfb9172054b4087a40e8e0b5a5461e7c"
    };

    private static readonly string[] JobIds =
        ["build-and-unit", "aot-smoke", "integration", "package-producer", "package-verifier", "ci-required-v1"];
    private static readonly string[] IntegrationProviders =
        ["PostgreSql", "MySql", "MariaDb", "SqlServer", "Oracle"];
    private static readonly string[] IntegrationTfms = ["net8.0", "net9.0", "net10.0"];

    public static void Verify(string repositoryRoot, string contractPath, string workflowPath)
    {
        var root = Path.GetFullPath(repositoryRoot);
        var contract = ReadContract(Path.GetFullPath(contractPath, root));
        var required = contract.RequiredJobs ?? throw new ReleaseVerificationException("CI contract requiredJobs must be an array.");
        Require(contract.SchemaVersion == "ci-required-v1", "CI contract schemaVersion must be ci-required-v1.");
        Require(required.Count == 5 && required.Select(job => job.Job).SequenceEqual(JobIds[..5]),
            "CI contract must declare the exact five required jobs in canonical order.");

        var document = LoadYaml(Path.GetFullPath(workflowPath, root));
        var workflow = Map(document, "workflow", "name", "on", "permissions", "jobs");
        Require(Scalar(workflow["name"], "workflow.name") == "CI", "Workflow name must be CI.");
        VerifyTriggers(Map(workflow["on"], "workflow.on", "pull_request", "merge_group"));
        var permissions = Map(workflow["permissions"], "workflow.permissions", "contents");
        Require(Scalar(permissions["contents"], "workflow.permissions.contents") == "read", "Workflow contents permission must be read.");

        var jobs = Map(workflow["jobs"], "workflow.jobs", JobIds);
        RequireSequence(jobs.Keys, JobIds, "workflow jobs");
        foreach (var jobId in JobIds)
        {
            VerifyJob(jobId, Map(jobs[jobId], $"job {jobId}"), required);
        }

        VerifyRequiredCommands(jobs);
    }

    private static void VerifyTriggers(IReadOnlyDictionary<string, YamlNode> triggers)
    {
        var pullRequest = Map(triggers["pull_request"], "pull_request", "branches");
        RequireSequence(Sequence(pullRequest["branches"], "pull_request.branches"), ["main", "prerelease"], "pull_request branches");
        Require(IsNull(triggers["merge_group"]), "merge_group must not be narrowed by filters.");
    }

    private static void VerifyJob(string jobId, IReadOnlyDictionary<string, YamlNode> job, IReadOnlyList<CiRequiredJob> contract)
    {
        var allowed = jobId switch
        {
            "integration" => new[] { "runs-on", "timeout-minutes", "env", "strategy", "steps" },
            "package-producer" => new[] { "runs-on", "timeout-minutes", "outputs", "steps" },
            "package-verifier" => new[] { "needs", "runs-on", "timeout-minutes", "permissions", "steps" },
            "ci-required-v1" => new[] { "name", "if", "needs", "runs-on", "steps" },
            "aot-smoke" => new[] { "runs-on", "timeout-minutes", "steps" },
            _ => new[] { "runs-on", "steps" }
        };
        RequireNoUnknown(job, $"job {jobId}", allowed);
        Require(Scalar(job["runs-on"], $"{jobId}.runs-on") == "ubuntu-latest", $"{jobId} must run on ubuntu-latest.");
        Require(!job.ContainsKey("continue-on-error"), $"{jobId} must not continue on error.");

        if (jobId == "integration")
        {
            var environment = Map(job["env"], "integration.env", "INQUIRY_REQUIRE_DOCKER");
            Require(Scalar(environment["INQUIRY_REQUIRE_DOCKER"], "integration.env.INQUIRY_REQUIRE_DOCKER") == "1",
                "Integration jobs must fail closed when Docker is unavailable.");
            var strategy = Map(job["strategy"], "integration.strategy", "fail-fast", "matrix");
            Require(Scalar(strategy["fail-fast"], "integration.strategy.fail-fast") == "false", "Integration matrix must fail-fast false.");
            var matrix = Map(strategy["matrix"], "integration.strategy.matrix", "provider", "tfm");
            var requiredMatrix = contract.Single(item => item.Job == "integration").Matrix;
            Require(requiredMatrix is not null, "CI contract integration matrix must be an object.");
            var hasProviders = requiredMatrix!.TryGetValue("provider", out var requiredProviders);
            var hasTfms = requiredMatrix.TryGetValue("tfm", out var requiredTfms);
            Require(requiredMatrix.Count == 2 && hasProviders && hasTfms
                    && requiredProviders is not null && requiredTfms is not null,
                "CI contract integration matrix must contain exactly the non-null provider and tfm keys.");
            RequireSequence(requiredProviders!, IntegrationProviders, "CI contract integration providers");
            RequireSequence(requiredTfms!, IntegrationTfms, "CI contract integration TFMs");
            RequireSequence(Sequence(matrix["provider"], "integration.matrix.provider"), requiredProviders!, "integration providers");
            RequireSequence(Sequence(matrix["tfm"], "integration.matrix.tfm"), requiredTfms!, "integration TFMs");
            Require(Sequence(matrix["provider"], "integration.matrix.provider").Count * Sequence(matrix["tfm"], "integration.matrix.tfm").Count == 15,
                "Integration matrix must contain exactly 15 provider/TFM legs.");
        }
        else
        {
            Require(!job.ContainsKey("strategy"), $"{jobId} must not define a strategy or hidden matrix.");
        }

        if (jobId == "package-verifier")
        {
            RequireSequence(Sequence(job["needs"], "package-verifier.needs"), ["package-producer"], "package-verifier needs");
        }
        else if (jobId == "ci-required-v1")
        {
            Require(Scalar(job["name"], "ci-required-v1.name") == "ci-required-v1", "Aggregator name must be ci-required-v1.");
            Require(Scalar(job["if"], "ci-required-v1.if") == "always()", "Aggregator must use if: always().");
            RequireSequence(Sequence(job["needs"], "ci-required-v1.needs"), JobIds[..5], "aggregator needs");
        }
        else
        {
            Require(!job.ContainsKey("needs") && !job.ContainsKey("if"), $"{jobId} must be an unconditional independent required job.");
        }

        var steps = Nodes(job["steps"], $"{jobId}.steps");
        Require(steps.Count > 0, $"{jobId} must contain steps.");
        foreach (var (node, index) in steps.Select((node, index) => (node, index)))
        {
            var step = Map(node, $"{jobId}.steps[{index}]");
            RequireNoUnknown(step, $"{jobId}.steps[{index}]", "name", "id", "uses", "with", "run", "env", "shell", "if");
            Require(!step.ContainsKey("continue-on-error"), $"{jobId}.steps[{index}] must not continue on error.");
            Require(step.ContainsKey("uses") ^ step.ContainsKey("run"), $"{jobId}.steps[{index}] must have exactly one of uses or run.");
            if (step.TryGetValue("uses", out var usesNode))
            {
                VerifyAction(Scalar(usesNode, $"{jobId}.steps[{index}].uses"), step, jobId);
            }

            if (step.TryGetValue("if", out var ifNode))
            {
                var condition = Scalar(ifNode, $"{jobId}.steps[{index}].if");
                var name = step.TryGetValue("name", out var nameNode) ? Scalar(nameNode, "step.name") : string.Empty;
                var approved = (name.StartsWith("Upload ", StringComparison.Ordinal) && condition == "always()")
                    || (jobId == "integration" && name is "Build pinned SQL Server full-text image" or "Preflight SQL Server image runtime user"
                        && condition == "matrix.provider == 'SqlServer'");
                Require(approved, $"Unapproved conditional step in {jobId}: {name}.");
            }

            VerifyStepMappings(jobId, index, step);
        }

        var expectedActions = jobId switch
        {
            "build-and-unit" => new[] { "actions/checkout", "actions/setup-dotnet", "actions/upload-artifact" },
            "aot-smoke" => new[] { "actions/checkout", "actions/setup-dotnet" },
            "integration" => new[] { "actions/checkout", "actions/setup-dotnet", "actions/upload-artifact" },
            "package-producer" => new[] { "actions/checkout", "actions/setup-dotnet", "actions/upload-artifact" },
            "package-verifier" => new[] { "actions/checkout", "actions/setup-dotnet", "actions/download-artifact" },
            _ => []
        };
        var actualActions = steps.Select(step => Map(step, "step"))
            .Where(step => step.TryGetValue("uses", out _))
            .Select(step => Scalar(step["uses"], "step.uses").Split('@')[0])
            .ToArray();
        RequireSequence(actualActions, expectedActions, $"{jobId} action sequence");

        if (jobId == "package-producer")
        {
            var outputs = Map(job["outputs"], "package-producer.outputs", "artifact-id", "artifact-digest");
            Require(Scalar(outputs["artifact-id"], "artifact-id") == "${{ steps.upload.outputs.artifact-id }}"
                && Scalar(outputs["artifact-digest"], "artifact-digest") == "${{ steps.upload.outputs.artifact-digest }}",
                "Package producer outputs must be bound to the immutable upload identity.");
        }
        if (jobId == "package-verifier")
        {
            var permissions = Map(job["permissions"], "package-verifier.permissions", "actions", "contents");
            Require(Scalar(permissions["actions"], "permissions.actions") == "read"
                && Scalar(permissions["contents"], "permissions.contents") == "read",
                "Package verifier permissions must be actions:read and contents:read.");
        }
        if (jobId == "ci-required-v1")
        {
            var aggregatorStep = Map(steps.Single(), "ci-required-v1 step", "name", "env", "run");
            var env = Map(aggregatorStep["env"], "ci-required-v1.env",
                "BUILD_AND_UNIT_RESULT", "AOT_SMOKE_RESULT", "INTEGRATION_RESULT", "PACKAGE_PRODUCER_RESULT", "PACKAGE_VERIFIER_RESULT");
            var expected = JobIds[..5].ToDictionary(
                item => item.Replace('-', '_').ToUpperInvariant() + "_RESULT",
                item => $"${{{{ needs.{item}.result }}}}",
                StringComparer.Ordinal);
            Require(env.All(pair => Scalar(pair.Value, $"aggregator.env.{pair.Key}") == expected[pair.Key]),
                "Aggregator result environment must bind every exact required-job result.");
        }
    }

    private static void VerifyRequiredCommands(IReadOnlyDictionary<string, YamlNode> jobs)
    {
        RequireRuns(jobs, "build-and-unit",
            "dotnet restore",
            "dotnet build --no-restore -c Release",
            "dotnet run --project eng/Inquiry.ReleaseTools/Inquiry.ReleaseTools.csproj --configuration Release --no-build -- verify-manifest . eng/release-manifest.json\ndotnet run --project eng/Inquiry.ReleaseTools/Inquiry.ReleaseTools.csproj --configuration Release --no-build -- verify-ci . eng/ci-required-v1.json .github/workflows/ci.yml\ndotnet test tests/Inquiry.ReleaseTools.Tests/Inquiry.ReleaseTools.Tests.csproj --configuration Release --no-build",
            "dotnet test tests/Inquiry.Benchmarks.Contracts.Tests/Inquiry.Benchmarks.Contracts.Tests.csproj -c Release --no-build --logger \"trx;LogFileName=benchmark-contracts.trx\" --results-directory test-results\ndotnet test tests/Inquiry.Generators.Tests/Inquiry.Generators.Tests.csproj -c Release --no-build --logger \"trx;LogFileName=generators.trx\" --results-directory test-results\ndotnet test tests/Inquiry.Tests/Inquiry.Tests.csproj -c Release --no-build --logger \"trx;LogFileName=unit.trx\" --results-directory test-results\ndotnet test tests/Inquiry.Sqlite.Tests/Inquiry.Sqlite.Tests.csproj -c Release --no-build --logger \"trx;LogFileName=sqlite.trx\" --results-directory test-results");
        RequireRuns(jobs, "aot-smoke",
            "dotnet publish samples/Inquiry.AotSmoke/Inquiry.AotSmoke.csproj -c Release -r linux-x64 -o aot-out",
            "out=$(./aot-out/Inquiry.AotSmoke)\necho \"$out\"\necho \"$out\" | grep -q \"AOT-SMOKE-OK\"");
        RequireRuns(jobs, "integration",
            "docker build --file tests/Inquiry.SqlServer.Tests/Fixtures/SqlServerFts.Dockerfile --tag inquiry-sqlserver-fts:2022-cu14 .",
            "uid=\"$(docker run --rm --entrypoint /usr/bin/id inquiry-sqlserver-fts:2022-cu14 -u)\"\ntest -n \"$uid\"\ntest \"$uid\" != \"0\"\necho \"SQL Server image runtime UID: $uid\"",
            "dotnet test tests/Inquiry.${{ matrix.provider }}.Tests/Inquiry.${{ matrix.provider }}.Tests.csproj -c Release -f ${{ matrix.tfm }} --logger \"trx;LogFileName=${{ matrix.provider }}-${{ matrix.tfm }}.trx\" --results-directory test-results\nif [ \"${{ matrix.provider }}\" = \"SqlServer\" ]; then\n  dotnet test tests/Inquiry.Benchmarks.SqlServer.Tests/Inquiry.Benchmarks.SqlServer.Tests.csproj -c Release -f ${{ matrix.tfm }} --logger \"trx;LogFileName=SqlServer-collection-${{ matrix.tfm }}.trx\" --results-directory test-results\nfi");
        RequireRuns(jobs, "package-producer",
            "./eng/pack-release.ps1 -OutputPath \"$env:RUNNER_TEMP/package-contract\" -Commit $env:GITHUB_SHA",
            "test -n \"$ARTIFACT_ID\"\necho \"$ARTIFACT_DIGEST\" | grep -Eq '^[0-9a-f]{64}$'");
        RequireRuns(jobs, "package-verifier",
            "echo \"$ARTIFACT_ID\" | grep -Eq '^[0-9]+$'\necho \"$ARTIFACT_DIGEST\" | grep -Eq '^[0-9a-f]{64}$'\ncurl --fail --silent --show-error --retry 3 --max-redirs 0 \\\n  --header \"Authorization: Bearer $GH_TOKEN\" \\\n  --header \"X-GitHub-Api-Version: 2022-11-28\" \\\n  \"$GITHUB_API_URL/repos/$GITHUB_REPOSITORY/actions/artifacts/$ARTIFACT_ID/zip\" \\\n  --dump-header artifact.headers --output /dev/null\nartifact_url=\"$(sed -n 's/^location: //Ip' artifact.headers | tr -d '\\r')\"\ncase \"$artifact_url\" in https://*) ;; *) exit 1 ;; esac\ncurl --fail --location --retry 3 --proto '=https' --proto-redir '=https' \\\n  \"$artifact_url\" --output artifact.zip\necho \"$ARTIFACT_DIGEST  artifact.zip\" | sha256sum --check --strict",
            "echo \"$ARTIFACT_DIGEST\" | grep -Eq '^[0-9a-f]{64}$'\ndotnet run --project eng/Inquiry.ReleaseTools/Inquiry.ReleaseTools.csproj --configuration Release -- verify-bundle . eng/release-manifest.json .artifacts/downloaded \"$GITHUB_SHA\"");
        RequireRuns(jobs, "ci-required-v1",
            "test \"$BUILD_AND_UNIT_RESULT\" = success\ntest \"$AOT_SMOKE_RESULT\" = success\ntest \"$INTEGRATION_RESULT\" = success\ntest \"$PACKAGE_PRODUCER_RESULT\" = success\ntest \"$PACKAGE_VERIFIER_RESULT\" = success");

        var workflowText = string.Join('\n', jobs.Values.Select(node => node.ToString()));
        Require(!workflowText.Contains("dotnet nuget push", StringComparison.OrdinalIgnoreCase), "Normal CI must not publish packages.");
    }

    private static void RequireRuns(IReadOnlyDictionary<string, YamlNode> jobs, string jobId, params string[] expected)
    {
        var job = Map(jobs[jobId], jobId);
        var actual = Nodes(job["steps"], $"{jobId}.steps")
            .Select(step => Map(step, "step"))
            .Where(step => step.TryGetValue("run", out _))
            .Select(step => NormalizeRun(Scalar(step["run"], "step.run")))
            .ToArray();
        RequireSequence(actual, expected.Select(NormalizeRun), $"{jobId} commands");
    }

    private static void VerifyAction(string uses, IReadOnlyDictionary<string, YamlNode> step, string jobId)
    {
        var separator = uses.LastIndexOf('@');
        Require(separator > 0, $"Invalid GitHub Action reference: {uses}.");
        var repository = uses[..separator];
        var reference = uses[(separator + 1)..];
        Require(ApprovedActions.TryGetValue(repository, out var approved) && reference == approved,
            $"GitHub Action is not on the exact approved Node 24 SHA allowlist: {uses}.");
        Require(!step.ContainsKey("env"), $"Action {repository} must not receive step environment overrides.");
        if (repository == "actions/checkout")
        {
            if (jobId == "package-producer")
            {
                Require(step.TryGetValue("with", out var withNode), "The package producer checkout must fetch the immutable full history.");
                var with = Map(withNode!, "checkout.with", "fetch-depth");
                Require(Scalar(with["fetch-depth"], "checkout.fetch-depth") == "0",
                    "Only the package producer checkout may use exact fetch-depth: 0.");
            }
            else
            {
                Require(!step.ContainsKey("with"), $"{jobId} checkout must not declare repository, ref, path, fetch-depth, or other inputs.");
            }
        }
        else if (repository == "actions/setup-dotnet")
        {
            Require(step.TryGetValue("with", out var withNode), "setup-dotnet must declare its exact SDK input.");
            var with = Map(withNode!, "setup-dotnet.with", "dotnet-version");
            var expectedVersions = jobId is "aot-smoke" ? "10.0.x"
                : jobId is "package-verifier" ? "8.0.x"
                : "8.0.x\n9.0.x\n10.0.x";
            Require(NormalizeRun(Scalar(with["dotnet-version"], "setup-dotnet.dotnet-version")) == expectedVersions,
                $"{jobId} setup-dotnet SDK inputs drifted.");
        }
        else if (repository == "actions/upload-artifact")
        {
            Require(step.TryGetValue("with", out var withNode), "Artifact upload must declare its exact contract.");
            var with = Map(withNode!, "upload-artifact.with", "name", "path", "if-no-files-found", "retention-days");
            Require(Scalar(with["if-no-files-found"], "upload.if-no-files-found") == "error", "Artifact upload must fail when files are absent.");
            var expectedRetention = jobId == "package-producer" ? "30" : "14";
            Require(Scalar(with["retention-days"], "upload.retention-days") == expectedRetention, "Artifact retention contract drifted.");
            var expectedName = jobId switch
            {
                "build-and-unit" => "unit-test-results",
                "integration" => "integration-test-results-${{ matrix.provider }}-${{ matrix.tfm }}",
                _ => "package-contract-${{ github.sha }}-${{ github.run_attempt }}"
            };
            var expectedPath = jobId == "package-producer" ? "${{ runner.temp }}/package-contract/" : "test-results/";
            Require(Scalar(with["name"], "upload.name") == expectedName && Scalar(with["path"], "upload.path") == expectedPath,
                "Artifact upload name/path contract drifted.");
        }
        else if (repository == "actions/download-artifact")
        {
            Require(step.TryGetValue("with", out var withNode), "Artifact download must declare its exact contract.");
            var with = Map(withNode!, "download-artifact.with", "artifact-ids", "path", "merge-multiple");
            Require(Scalar(with["artifact-ids"], "download.artifact-ids") == "${{ needs.package-producer.outputs.artifact-id }}"
                && Scalar(with["path"], "download.path") == ".artifacts/downloaded"
                && Scalar(with["merge-multiple"], "download.merge-multiple") == "true",
                "Artifact download must use the immutable producer ID and exact verifier path.");
        }
    }

    private static void VerifyStepMappings(
        string jobId,
        int stepIndex,
        IReadOnlyDictionary<string, YamlNode> step)
    {
        if (step.ContainsKey("uses"))
        {
            Require(!step.ContainsKey("shell"), $"Action step {jobId}[{stepIndex}] must not declare a shell.");
            return;
        }

        Require(!step.ContainsKey("with"), $"Run step {jobId}[{stepIndex}] must not declare action inputs.");
        var run = NormalizeRun(Scalar(step["run"], $"{jobId}[{stepIndex}].run"));
        if (jobId == "package-producer"
            && run == "./eng/pack-release.ps1 -OutputPath \"$env:RUNNER_TEMP/package-contract\" -Commit $env:GITHUB_SHA")
        {
            Require(step.TryGetValue("shell", out var shellNode)
                && Scalar(shellNode!, $"{jobId}[{stepIndex}].shell") == "pwsh",
                "The package producer must use the exact pwsh shell.");
        }
        else
        {
            Require(!step.ContainsKey("shell"), $"Run step {jobId}[{stepIndex}] has an unapproved shell override.");
        }

        var name = step.TryGetValue("name", out var nameNode) ? Scalar(nameNode, $"{jobId}[{stepIndex}].name") : string.Empty;
        IReadOnlyDictionary<string, string>? expected = (jobId, name) switch
        {
            ("integration", var value) when value.StartsWith("Integration tests (", StringComparison.Ordinal) =>
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["INQUIRY_SQLSERVER_IMAGE"] = "${{ matrix.provider == 'SqlServer' && 'inquiry-sqlserver-fts:2022-cu14' || '' }}"
                },
            ("package-producer", "Validate immutable artifact identity") =>
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["ARTIFACT_ID"] = "${{ steps.upload.outputs.artifact-id }}",
                    ["ARTIFACT_DIGEST"] = "${{ steps.upload.outputs.artifact-digest }}"
                },
            ("package-verifier", "Verify the stored artifact archive digest") =>
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["ARTIFACT_ID"] = "${{ needs.package-producer.outputs.artifact-id }}",
                    ["ARTIFACT_DIGEST"] = "${{ needs.package-producer.outputs.artifact-digest }}",
                    ["GH_TOKEN"] = "${{ github.token }}"
                },
            ("package-verifier", "Validate producer digest identity and downloaded bytes") =>
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["ARTIFACT_DIGEST"] = "${{ needs.package-producer.outputs.artifact-digest }}"
                },
            ("ci-required-v1", "Enforce every required job and matrix leg") => JobIds[..5].ToDictionary(
                item => item.Replace('-', '_').ToUpperInvariant() + "_RESULT",
                item => $"${{{{ needs.{item}.result }}}}", StringComparer.Ordinal),
            _ => null
        };

        if (expected is null)
        {
            Require(!step.ContainsKey("env"), $"Run step {jobId}[{stepIndex}] has an unapproved environment mapping.");
            return;
        }

        Require(step.TryGetValue("env", out var environmentNode), $"Run step {jobId}[{stepIndex}] is missing its exact environment mapping.");
        var environment = Map(environmentNode!, $"{jobId}[{stepIndex}].env", expected.Keys.ToArray());
        Require(environment.All(pair => Scalar(pair.Value, $"{jobId}[{stepIndex}].env.{pair.Key}") == expected[pair.Key]),
            $"Run step {jobId}[{stepIndex}] environment mapping drifted.");
        Require(!environment.ContainsKey("INQUIRY_REQUIRE_DOCKER"),
            "Integration Docker enforcement is job-owned and cannot be overridden by a step.");
    }

    private static YamlNode LoadYaml(string path)
    {
        try
        {
            using var reader = File.OpenText(path);
            var yaml = new YamlStream();
            yaml.Load(reader);
            Require(yaml.Documents.Count == 1, "CI workflow must contain exactly one YAML document.");
            ValidateCanonicalYaml(yaml.Documents[0].RootNode);
            return yaml.Documents[0].RootNode;
        }
        catch (Exception exception) when (exception is YamlException or ArgumentException)
        {
            throw new ReleaseVerificationException($"CI workflow is invalid YAML: {exception.Message}");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new ReleaseVerificationException($"Could not read CI workflow '{path}': {exception.Message}");
        }
    }

    private static void ValidateCanonicalYaml(YamlNode node)
    {
        Require(node.NodeType != YamlNodeType.Alias, "CI workflow must not use YAML aliases.");
        Require(node.Anchor.IsEmpty, "CI workflow must not use YAML anchors.");
        Require(node.Tag.IsEmpty, "CI workflow must not use explicit YAML tags.");
        if (node is YamlScalarNode scalar)
        {
            Require(scalar.Style is ScalarStyle.Plain or ScalarStyle.Literal,
                "CI workflow scalars must use canonical plain or literal style; quoted and folded scalars are forbidden.");
        }
        foreach (var child in node.AllNodes.Skip(1))
        {
            Require(child.NodeType != YamlNodeType.Alias, "CI workflow must not use YAML aliases.");
            Require(child.Anchor.IsEmpty, "CI workflow must not use YAML anchors.");
            Require(child.Tag.IsEmpty, "CI workflow must not use explicit YAML tags.");
            if (child is YamlScalarNode childScalar)
            {
                Require(childScalar.Style is ScalarStyle.Plain or ScalarStyle.Literal,
                    "CI workflow scalars must use canonical plain or literal style; quoted and folded scalars are forbidden.");
            }
        }
    }

    private static CiRequiredContract ReadContract(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize(stream, ReleaseJsonContext.Default.CiRequiredContract)
                ?? throw new ReleaseVerificationException("The CI contract is empty.");
        }
        catch (JsonException exception)
        {
            throw new ReleaseVerificationException($"The CI contract is invalid JSON: {exception.Message}");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new ReleaseVerificationException($"Could not read CI contract '{path}': {exception.Message}");
        }
    }

    private static IReadOnlyDictionary<string, YamlNode> Map(YamlNode node, string context, params string[] exactKeys)
    {
        Require(node is YamlMappingNode, $"{context} must be a mapping.");
        var result = new Dictionary<string, YamlNode>(StringComparer.Ordinal);
        foreach (var pair in ((YamlMappingNode)node).Children)
        {
            var key = Scalar(pair.Key, $"{context} key");
            Require(result.TryAdd(key, pair.Value), $"{context} contains duplicate key '{key}'.");
        }
        if (exactKeys.Length > 0)
        {
            RequireSequence(result.Keys, exactKeys, $"{context} keys");
        }
        return result;
    }

    private static void RequireNoUnknown(IReadOnlyDictionary<string, YamlNode> map, string context, params string[] allowed)
    {
        var allowedSet = allowed.ToHashSet(StringComparer.Ordinal);
        var unknown = map.Keys.FirstOrDefault(key => !allowedSet.Contains(key));
        Require(unknown is null, $"{context} contains an unknown or unsafe key: {unknown}.");
        Require(map.ContainsKey("steps") || context.Contains("steps[", StringComparison.Ordinal), $"{context} is missing steps.");
    }

    private static IReadOnlyList<YamlNode> Nodes(YamlNode node, string context)
    {
        Require(node is YamlSequenceNode, $"{context} must be a sequence.");
        return ((YamlSequenceNode)node).Children.ToArray();
    }

    private static IReadOnlyList<string> Sequence(YamlNode node, string context) =>
        Nodes(node, context).Select(item => Scalar(item, context)).ToArray();

    private static string Scalar(YamlNode node, string context)
    {
        Require(node is YamlScalarNode scalar && scalar.Value is not null, $"{context} must be a non-null scalar.");
        return ((YamlScalarNode)node).Value!;
    }

    private static bool IsNull(YamlNode node) => node is YamlScalarNode scalar && string.IsNullOrEmpty(scalar.Value);
    private static string NormalizeRun(string value) => string.Join('\n', value.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').Select(line => line.TrimEnd())).Trim();

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
