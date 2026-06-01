using Xunit;

namespace Inquiry.SqlServer.Tests.Fixtures;

[CollectionDefinition(Name)]
public sealed class SqlServerCollection : ICollectionFixture<SqlServerContainerFixture>
{
    public const string Name = "SqlServer";
}
