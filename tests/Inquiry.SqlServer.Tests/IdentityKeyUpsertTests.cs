using Inquiry.Northwind.Models;
using Inquiry.Northwind.Stores;
using Inquiry.SqlServer.Tests.Fixtures;

namespace Inquiry.SqlServer.Tests;

/// <summary>
/// Regression coverage for IDENTITY (database-generated <c>int</c> key) upsert on SQL Server. The
/// generated MERGE previously listed the IDENTITY column in its <c>WHEN NOT MATCHED THEN INSERT</c>,
/// which SQL Server rejects (error 544) even when that branch is never taken — so any supplied-key
/// upsert of an IDENTITY-keyed entity threw. The existing generated-key upsert tests only exercise
/// GUID keys (DEFAULT NEWSEQUENTIALID), which legally accept explicit values, so the IDENTITY case
/// went uncovered.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class IdentityKeyUpsertTests
{
    private readonly SqlServerContainerFixture _fixture;
    public IdentityKeyUpsertTests(SqlServerContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task UpsertWithSuppliedIdentityKeyUpdatesExistingRow()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateAsync(_fixture.AdminConnectionString, "idupsert_upd");
        var shippers = harness.GetRequiredService<ShipperStore>();

        var inserted = await shippers.InsertReturningAsync(new Shipper { CompanyName = "Before", Phone = "1" });
        var id = inserted!.ShipperID!.Value;

        // Supplied (non-null) key → MERGE; the row exists → UPDATE branch. Previously threw 544 because
        // SQL Server bound the never-taken NOT MATCHED INSERT of the IDENTITY column.
        await shippers.UpsertAsync(new Shipper { ShipperID = id, CompanyName = "After", Phone = "2" });

        var reloaded = await shippers.SelectByKeyAsync(id);
        Assert.Equal("After", reloaded!.CompanyName);
        Assert.Equal("2", reloaded.Phone);
    }

    [SkippableFact]
    public async Task UpsertWithNullIdentityKeyInsertsRow()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await SqlServerTestHarness.CreateAsync(_fixture.AdminConnectionString, "idupsert_ins");
        var shippers = harness.GetRequiredService<ShipperStore>();

        // Null key → fast-path INSERT lets the database assign the identity.
        await shippers.UpsertAsync(new Shipper { ShipperID = null, CompanyName = "Fresh", Phone = "9" });

        var all = await shippers.SelectAllAsync();
        Assert.Contains(all, s => s.CompanyName == "Fresh");
    }
}
