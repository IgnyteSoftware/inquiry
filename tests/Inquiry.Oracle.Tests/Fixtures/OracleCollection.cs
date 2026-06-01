using Xunit;

namespace Inquiry.Oracle.Tests.Fixtures;

[CollectionDefinition(Name)]
public sealed class OracleCollection : ICollectionFixture<OracleContainerFixture>
{
    public const string Name = "Oracle";
}
