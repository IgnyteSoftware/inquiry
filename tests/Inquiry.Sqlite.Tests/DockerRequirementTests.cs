using System;
using Inquiry.IntegrationTesting;
using Xunit;

namespace Inquiry.Sqlite.Tests;

public class DockerRequirementTests
{
    [Fact]
    public void EnforceThrowsWhenRequiredAndUnavailable()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => DockerRequirement.Enforce(isRequired: true, isAvailable: false, skipReason: "Docker down"));
        Assert.Contains(DockerRequirement.EnvVarName, ex.Message);
        Assert.Contains("Docker down", ex.Message);
    }

    [Fact]
    public void EnforceDoesNotThrowWhenRequiredAndAvailable()
        => Assert.Null(Record.Exception(
            () => DockerRequirement.Enforce(isRequired: true, isAvailable: true, skipReason: null)));

    [Fact]
    public void EnforceDoesNotThrowWhenNotRequiredAndUnavailable()
        => Assert.Null(Record.Exception(
            () => DockerRequirement.Enforce(isRequired: false, isAvailable: false, skipReason: "Docker down")));

    [Fact]
    public void EnforceDoesNotThrowWhenNotRequiredAndAvailable()
        => Assert.Null(Record.Exception(
            () => DockerRequirement.Enforce(isRequired: false, isAvailable: true, skipReason: null)));
}
