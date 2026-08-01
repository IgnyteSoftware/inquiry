using System;

namespace Inquiry.IntegrationTesting;

/// <summary>
/// Gates Docker-backed integration suites in CI. When <c>INQUIRY_REQUIRE_DOCKER=1</c> is set (CI only)
/// and a test container did not start, the run must FAIL rather than silently skip — otherwise a runner
/// with broken Docker leaves CI green while the live tests never ran. Locally (env var unset) the suites
/// still skip when Docker is absent.
/// </summary>
public static class DockerRequirement
{
    /// <summary>Environment variable that, when set to <c>"1"</c>, makes a missing container a hard failure.</summary>
    public const string EnvVarName = "INQUIRY_REQUIRE_DOCKER";

    /// <summary>True when <see cref="EnvVarName"/> is set to <c>"1"</c> (set on CI, unset locally).</summary>
    public static bool IsRequired() => Environment.GetEnvironmentVariable(EnvVarName) == "1";

    /// <summary>
    /// Called by each container fixture at the end of <c>InitializeAsync</c>. Throws when Docker is
    /// required (CI) but the container did not start; otherwise a no-op.
    /// </summary>
    public static void ThrowIfRequiredButUnavailable(bool isAvailable, string? skipReason)
        => Enforce(IsRequired(), isAvailable, skipReason);

    /// <summary>Pure core, separated from the environment read so it is deterministically unit-testable.</summary>
    public static void Enforce(bool isRequired, bool isAvailable, string? skipReason)
    {
        if (isRequired && !isAvailable)
        {
            throw new InvalidOperationException(
                $"{EnvVarName}=1 requires a running test container, but it did not start: {skipReason}");
        }
    }
}
