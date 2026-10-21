namespace Inquiry.MySql.Tests.Fixtures;

[CollectionDefinition(Name)]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1711", Justification = "xUnit collection definitions use the <Name>Collection convention; this type is not a collection.")]
public sealed class MySqlCollection : ICollectionFixture<MySqlContainerFixture>
{
    public const string Name = "MySql";
}
