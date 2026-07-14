namespace Inquiry.ReleaseTools.Tests;

public sealed class ReleaseToolTests
{
    [Theory]
    [InlineData("verify-manifest")]
    [InlineData("verify-ci-contract")]
    [InlineData("verify-ci-workflow")]
    public async Task Missing_input_is_reported_as_a_release_verification_error(string scenario)
    {
        var missing = Path.Combine(Path.GetTempPath(), $"inquiry-missing-{Guid.NewGuid():N}");

        var (args, expected) = scenario switch
        {
            "verify-manifest" => (new[] { "verify-manifest", RepositoryFixture.Root, missing }, "release manifest"),
            "verify-ci-contract" => (new[]
            {
                "verify-ci",
                RepositoryFixture.Root,
                missing,
                Path.Combine(RepositoryFixture.Root, ".github", "workflows", "ci.yml")
            }, "CI contract"),
            "verify-ci-workflow" => (new[]
            {
                "verify-ci",
                RepositoryFixture.Root,
                Path.Combine(RepositoryFixture.Root, "eng", "ci-required-v1.json"),
                missing
            }, "CI workflow"),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await ReleaseTool.RunAsync(args, output, error);

        Assert.Equal(1, exitCode);
        Assert.StartsWith("release-verification-error: ", error.ToString(), StringComparison.Ordinal);
        Assert.Contains(expected, error.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("verify-manifest")]
    [InlineData("verify-ci-contract")]
    [InlineData("verify-ci-workflow")]
    public async Task Directory_input_is_reported_as_a_release_verification_error(string scenario)
    {
        var directory = Directory.CreateTempSubdirectory("inquiry-release-input-");
        try
        {
            var args = scenario switch
            {
                "verify-manifest" => new[] { "verify-manifest", RepositoryFixture.Root, directory.FullName },
                "verify-ci-contract" => new[]
                {
                    "verify-ci",
                    RepositoryFixture.Root,
                    directory.FullName,
                    Path.Combine(RepositoryFixture.Root, ".github", "workflows", "ci.yml")
                },
                "verify-ci-workflow" => new[]
                {
                    "verify-ci",
                    RepositoryFixture.Root,
                    Path.Combine(RepositoryFixture.Root, "eng", "ci-required-v1.json"),
                    directory.FullName
                },
                _ => throw new ArgumentOutOfRangeException(nameof(scenario))
            };
            using var output = new StringWriter();
            using var error = new StringWriter();

            var exitCode = await ReleaseTool.RunAsync(args, output, error);

            Assert.Equal(1, exitCode);
            Assert.StartsWith("release-verification-error: ", error.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            directory.Delete();
        }
    }
}
