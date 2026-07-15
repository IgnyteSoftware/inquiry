using Inquiry.Oracle.Tests.Fixtures;

namespace Inquiry.Oracle.Tests;

public sealed class OracleContainerFixturePolicyTests
{
    [Theory]
    [InlineData(false, null, false)]
    [InlineData(false, "", false)]
    [InlineData(true, null, true)]
    [InlineData(false, "gvenzl/oracle-xe:test", true)]
    [InlineData(true, "gvenzl/oracle-xe:test", true)]
    public void ExplicitImageOverrideMakesOracleCapabilityRequired(
        bool dockerIsRequired,
        string? configuredImage,
        bool expected)
    {
        Assert.Equal(
            expected,
            OracleContainerFixture.IsCapabilityRequired(dockerIsRequired, configuredImage));
    }
}
