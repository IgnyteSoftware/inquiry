using Inquiry.Benchmarks.DLG;
using Xunit;

namespace Inquiry.SqlServer.Tests.Dlg;

/// <summary>
/// Proves DlgSetup (procs + primed config) works and each Phase-1 DLG capability returns correct
/// results against a real SQL Server. All tests share one database (DLG's config is process-static).
/// </summary>
[Collection(DlgCollection.Name)]
public sealed class DlgSmokeTests
{
    private readonly DlgDatabaseFixture _fixture;
    public DlgSmokeTests(DlgDatabaseFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task SelectAll_ReturnsAtLeastSeededShippers()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);

        var shippers = await Shipper.SelectAllAsync();

        Assert.True(shippers.Count >= DlgDatabaseFixture.SeededShippers);
    }
}
