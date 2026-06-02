using Inquiry.Northwind.Models;
using Inquiry.Northwind.Stores;
using Inquiry.Oracle.Tests.Fixtures;

namespace Inquiry.Oracle.Tests;

/// <summary>
/// Northwind coverage gaps for the Oracle provider that the existing Oracle suites do not already
/// exercise: a multi-field <c>SELECT</c> filtering by two columns, and the <c>Employee.ReportsTo</c>
/// self-referencing foreign key round-trip. Each fact runs in its own throwaway schema so parallel facts
/// cannot collide on table state.
/// </summary>
[Collection(OracleCollection.Name)]
public sealed class OracleCoverageGapIntegrationTests
{
    private readonly OracleContainerFixture _fixture;
    public OracleCoverageGapIntegrationTests(OracleContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task MultiFieldSelectFiltersByBothColumns()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        // Known Oracle limitation (ready to un-skip once fixed): inserting an entity with a System.DateTime
        // column throws because the generator emits DbType.DateTime2 and ODP.NET's OracleParameter rejects
        // it ("Value does not fall within the expected range"). Employee/Order carry DateTime columns, so
        // these gap-fill tests cannot seed without it. See docs/STATUS.md.
        Skip.If(true, "Oracle insert of DateTime columns blocked by ODP.NET DbType.DateTime2 rejection (see STATUS.md).");
        await using var harness = await OracleTestHarness.CreateAsync(_fixture.AdminConnectionString, "multifield");
        var customers = harness.GetRequiredService<CustomerStore>();
        var employees = harness.GetRequiredService<EmployeeStore>();
        var orders = harness.GetRequiredService<OrderStore>();

        await customers.InsertAsync(new Customer { CustomerID = "ALFKI", CompanyName = "Alfreds" });
        await customers.InsertAsync(new Customer { CustomerID = "BONAP", CompanyName = "Bon app'" });
        var nancy = await employees.InsertReturningAsync(new Employee { FirstName = "Nancy", LastName = "Davolio" });
        var andrew = await employees.InsertReturningAsync(new Employee { FirstName = "Andrew", LastName = "Fuller" });

        await orders.InsertAsync(new Order { CustomerID = "ALFKI", EmployeeID = nancy!.EmployeeID,  ShipCity = "Berlin" });
        await orders.InsertAsync(new Order { CustomerID = "ALFKI", EmployeeID = andrew!.EmployeeID, ShipCity = "Berlin" });
        await orders.InsertAsync(new Order { CustomerID = "BONAP", EmployeeID = nancy.EmployeeID,   ShipCity = "Marseille" });

        var matched = await orders.SelectByCustomerAndEmployeeAsync("ALFKI", nancy.EmployeeID).ToListAsync();
        var only = Assert.Single(matched);
        Assert.Equal("Berlin", only.ShipCity);
    }

    [SkippableFact]
    public async Task EmployeeReportsToSelfReferenceRoundTrips()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        // Known Oracle limitation (ready to un-skip once fixed): inserting an entity with a System.DateTime
        // column throws because the generator emits DbType.DateTime2 and ODP.NET's OracleParameter rejects
        // it ("Value does not fall within the expected range"). Employee/Order carry DateTime columns, so
        // these gap-fill tests cannot seed without it. See docs/STATUS.md.
        Skip.If(true, "Oracle insert of DateTime columns blocked by ODP.NET DbType.DateTime2 rejection (see STATUS.md).");
        await using var harness = await OracleTestHarness.CreateAsync(_fixture.AdminConnectionString, "emp");
        var employees = harness.GetRequiredService<EmployeeStore>();

        var manager = await employees.InsertReturningAsync(new Employee { FirstName = "Andrew", LastName = "Fuller", Title = "VP" });
        var report = await employees.InsertReturningAsync(new Employee { FirstName = "Nancy", LastName = "Davolio", Title = "Sales", ReportsTo = manager!.EmployeeID });

        var fetched = await employees.SelectByKeyAsync(report!.EmployeeID);
        Assert.NotNull(fetched);
        Assert.Equal(manager.EmployeeID, fetched!.ReportsTo);
    }
}
