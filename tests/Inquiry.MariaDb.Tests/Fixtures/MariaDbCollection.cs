using Xunit;

namespace Inquiry.MariaDb.Tests.Fixtures;

[CollectionDefinition(Name)]
public sealed class MariaDbCollection : ICollectionFixture<MariaDbContainerFixture>
{
    public const string Name = "MariaDb";
}
