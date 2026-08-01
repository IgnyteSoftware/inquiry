using Xunit;

namespace Inquiry.SqlServer.Tests.Dlg;

[CollectionDefinition(Name)]
public sealed class DlgCollection : ICollectionFixture<DlgDatabaseFixture>
{
    public const string Name = "Dlg";
}
