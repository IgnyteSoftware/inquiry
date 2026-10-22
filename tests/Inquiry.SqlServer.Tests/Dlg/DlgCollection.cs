namespace Inquiry.SqlServer.Tests.Dlg;

[CollectionDefinition(Name)]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1711", Justification = "xUnit collection definitions use the <Name>Collection convention; this type is not a collection.")]
public sealed class DlgCollection : ICollectionFixture<DlgDatabaseFixture>
{
    public const string Name = "Dlg";
}
