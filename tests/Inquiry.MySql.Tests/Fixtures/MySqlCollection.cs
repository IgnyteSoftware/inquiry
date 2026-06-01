using Xunit;

namespace Inquiry.MySql.Tests.Fixtures;

[CollectionDefinition(Name)]
public sealed class MySqlCollection : ICollectionFixture<MySqlContainerFixture>
{
    public const string Name = "MySql";
}
