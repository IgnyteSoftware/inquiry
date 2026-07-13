using System;
using Inquiry.SqlServer.Tests.Fixtures;

namespace Inquiry.SqlServer.Tests;

public sealed class SqlServerFullTextPolicyTests
{
    [Fact]
    public void RequiredMissingCapabilityThrows()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => SqlServerFullTextPolicy.ShouldSkip(isRequired: true, isInstalled: false));

        Assert.Contains("IsFullTextInstalled", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LocalMissingCapabilitySkips()
        => Assert.True(SqlServerFullTextPolicy.ShouldSkip(isRequired: false, isInstalled: false));

    [Fact]
    public void RequiredInstalledCapabilityRuns()
        => Assert.False(SqlServerFullTextPolicy.ShouldSkip(isRequired: true, isInstalled: true));

    [Fact]
    public void LocalInstalledCapabilityRuns()
        => Assert.False(SqlServerFullTextPolicy.ShouldSkip(isRequired: false, isInstalled: true));
}
