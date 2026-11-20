using System.Data.Common;
using Inquiry.Commands;
using Inquiry.Generated;
using Microsoft.Extensions.DependencyInjection;

namespace Inquiry.Journeys;

// Each journey runs on the data the journeys before it left, and returns its output line.
public sealed class JourneyRunner(IServiceProvider services)
{
    private readonly IInquiry _inquiry = services.GetRequiredService<IInquiry>();
    private long _customerId;
    private long _orderId;

    public async Task<string> NewSchemaAsync()
    {
        Check(!InquiryGeneratedSchema.Ddl.Contains("legacy_supplier", StringComparison.Ordinal), "The DDL includes a GenerateDdl = false table.");
        await _inquiry.ExecuteAsync(new InquiryCommand(InquiryGeneratedSchema.Ddl));
        var tables = await _inquiry.ExecuteScalarAsync<long>(
            $"SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite%'");
        Check(tables == 8, $"Expected 8 generated tables, found {tables}.");

        var customers = services.GetRequiredService<CustomerStore>();
        var saved = await customers.InsertReturningAsync(new Customer { Name = "Northwind Traders" })
            ?? throw new InvalidOperationException("The insert did not return a customer.");
        _customerId = saved.Id!.Value;
        var loaded = await customers.SelectByKeyAsync(_customerId);
        Check(loaded is { Name: "Northwind Traders", Preferences: null }, "The customer did not round-trip.");

        // The generated foreign keys must reach the database, so a line for a missing order fails.
        var orphan = "accepted";
        try
        {
            await services.GetRequiredService<OrderLineStore>().InsertAllAsync(
                [new OrderLine { OrderId = 999, Sku = "NONE", Quantity = 1, UnitPriceCents = 1 }]);
        }
        catch (DbException ex) when (ex.Message.Contains("FOREIGN KEY constraint failed", StringComparison.Ordinal))
        {
            orphan = "rejected";
        }

        Check(orphan == "rejected", "The generated foreign key did not reject an orphan line.");
        return $"new-schema: created {tables} tables from InquiryGeneratedSchema.Ddl; customer {_customerId} round-tripped; orphan line {orphan}";
    }

    public async Task<string> ExistingSchemaAsync()
    {
        // Stands in for a table that a migration or another team owns.
        await _inquiry.ExecuteAsync(new InquiryCommand("""
            CREATE TABLE legacy_supplier (
                supplier_no INTEGER PRIMARY KEY AUTOINCREMENT,
                company_nm TEXT NOT NULL,
                active_flag INTEGER NOT NULL)
            """));
        var suppliers = services.GetRequiredService<SupplierStore>();
        var exotic = await suppliers.InsertReturningAsync(new Supplier { Name = "Exotic Liquids", IsActive = true })
            ?? throw new InvalidOperationException("The insert did not return a supplier.");
        var tokyo = await suppliers.InsertReturningAsync(new Supplier { Name = "Tokyo Traders", IsActive = true })
            ?? throw new InvalidOperationException("The insert did not return a supplier.");

        var loaded = await suppliers.SelectByKeyAsync(exotic.Id)
            ?? throw new InvalidOperationException("The supplier was not found.");
        loaded.Name = "Exotic Liquids Ltd";
        loaded.IsActive = false;
        Check(await suppliers.UpdateAsync(loaded), "The supplier update affected no row.");
        Check(await suppliers.DeleteAsync(tokyo.Id), "The supplier delete affected no row.");

        var physical = await _inquiry.ExecuteScalarAsync<string>(
            $"SELECT company_nm FROM legacy_supplier WHERE supplier_no = {exotic.Id} AND active_flag = 0");
        var remaining = await suppliers.SelectAllAsync();
        Check(physical == "Exotic Liquids Ltd" && remaining.Count == 1, "The legacy table does not hold the expected row.");
        return $"existing-schema: insert, read, update and delete on legacy_supplier; {remaining.Count} row left";
    }

    public async Task<string> OrderAndLinesAsync()
    {
        var orders = services.GetRequiredService<SalesOrderStore>();
        var lines = services.GetRequiredService<OrderLineStore>();

        // Stores resolved from this scope enlist in the transaction that the facade opens.
        _orderId = await _inquiry.ExecuteInTransactionAsync(async _ =>
        {
            var order = await orders.InsertReturningAsync(new SalesOrder { CustomerId = _customerId, Reference = "SO-1001" })
                ?? throw new InvalidOperationException("The insert did not return an order.");
            var id = order.Id!.Value;
            await lines.InsertAllAsync(
            [
                new OrderLine { OrderId = id, Sku = "CHAI", Quantity = 2, UnitPriceCents = 1800 },
                new OrderLine { OrderId = id, Sku = "CHANG", Quantity = 1, UnitPriceCents = 1900 },
                new OrderLine { OrderId = id, Sku = "TOFU", Quantity = 3, UnitPriceCents = 250 },
            ]);
            return id;
        });
        var committed = await orders.SelectWithRelationsAsync(_orderId);
        Check(committed is { Lines.Count: 3 }, "The committed order does not have 3 lines.");

        // The third line breaks the Quantity > 0 check after the order row is written.
        var failed = false;
        try
        {
            await _inquiry.ExecuteInTransactionAsync(async _ =>
            {
                var order = await orders.InsertReturningAsync(new SalesOrder { CustomerId = _customerId, Reference = "SO-1002" })
                    ?? throw new InvalidOperationException("The insert did not return an order.");
                var id = order.Id!.Value;
                await lines.InsertAllAsync(
                [
                    new OrderLine { OrderId = id, Sku = "CHAI", Quantity = 1, UnitPriceCents = 1800 },
                    new OrderLine { OrderId = id, Sku = "IKURA", Quantity = 0, UnitPriceCents = 3100 },
                ]);
            });
        }
        catch (DbException ex) when (ex.Message.Contains("CHECK constraint failed", StringComparison.Ordinal))
        {
            failed = true;
        }

        var leftOrders = await _inquiry.ExecuteScalarAsync<long>($"SELECT COUNT(*) FROM SalesOrder WHERE Reference = 'SO-1002'");
        var leftLines = await _inquiry.ExecuteScalarAsync<long>($"SELECT COUNT(*) FROM OrderLine WHERE OrderId <> {_orderId}");
        Check(failed, "The invalid line did not fail.");
        Check(leftOrders + leftLines == 0, "The failed transaction left rows behind.");
        return $"order-and-lines: committed 1 order with {committed!.Lines.Count} lines; rollback left {leftOrders + leftLines} rows";
    }

    public async Task<string> RoleLinksAsync()
    {
        var users = services.GetRequiredService<UserStore>();
        var roles = services.GetRequiredService<RoleStore>();
        var links = services.GetRequiredService<UserRoleStore>();
        await users.InsertAsync(new User { Id = 1, Name = "ada" });
        await users.InsertAsync(new User { Id = 2, Name = "bob" });
        await roles.InsertAllAsync([new Role { Id = 1, Name = "admin" }, new Role { Id = 2, Name = "editor" }, new Role { Id = 3, Name = "viewer" }]);
        foreach (var roleId in new[] { 1, 2, 3 })
        {
            await links.LinkAsync(new UserRole { UserId = 1, RoleId = roleId });
        }

        await links.LinkAsync(new UserRole { UserId = 2, RoleId = 3 });

        var linked = await users.SelectWithRolesAsync(1);
        Check(linked is { Roles.Count: 3 }, "ada does not have 3 roles.");
        Check(await links.UnlinkAsync(1, 2), "The unlink affected no row.");
        var after = await users.SelectWithRolesAsync(1)
            ?? throw new InvalidOperationException("ada was not found.");
        var bob = await users.SelectWithRolesAsync(2);
        var names = after.Roles.Select(r => r.Name).Order(StringComparer.Ordinal).ToArray();
        Check(names is ["admin", "viewer"], "ada has the wrong roles after the unlink.");
        Check(bob is { Roles: [{ Name: "viewer" }] }, "bob's roles changed.");
        return $"role-links: linked {linked!.Roles.Count} roles to ada; unlinked editor; ada has {string.Join(", ", names)}";
    }

    public async Task<string> ConcurrentEditsAsync()
    {
        var articles = services.GetRequiredService<ArticleStore>();
        await articles.InsertAsync(new Article { Id = 1, Title = "Draft" });
        var first = await articles.SelectByKeyAsync(1) ?? throw new InvalidOperationException("The article was not found.");
        var second = await articles.SelectByKeyAsync(1) ?? throw new InvalidOperationException("The article was not found.");

        var before = first.Version;
        first.Title = "Edited by first";
        Check(await articles.UpdateAsync(first), "The first update was rejected.");
        second.Title = "Edited by second";
        string outcome;
        try
        {
            await articles.UpdateAsync(second);
            outcome = "accepted";
        }
        catch (InquiryConcurrencyException)
        {
            outcome = "rejected with InquiryConcurrencyException";
        }

        var stored = await articles.SelectByKeyAsync(1);
        Check(outcome != "accepted", "The stale update was accepted.");
        Check(stored is { Title: "Edited by first", Version: 1 }, "The stored article is not the first edit.");
        return $"concurrent-edits: first update won (version {before} -> {stored!.Version}); second update {outcome}";
    }

    public async Task<string> TenantIsolationAsync()
    {
        var documents = services.GetRequiredService<DocumentStore>();

        // Inserts do not apply the filter, so the application stamps the tenant itself.
        await documents.InsertAllAsync(
        [
            new Document { Id = 1, Title = "A plan", TenantId = 1 },
            new Document { Id = 2, Title = "A budget", TenantId = 1 },
            new Document { Id = 3, Title = "B secrets", TenantId = 2 },
        ]);

        int visible;
        bool foreignRead, foreignUpdate, foreignDelete, ownUpdate, ownDelete;
        Document? ownEdited, ownDeleted;
        using (InquiryFilterContext.BeginScope(new Dictionary<string, object> { ["TenantId"] = 1 }))
        {
            visible = (await documents.SelectAllAsync()).Count;
            foreignRead = await documents.SelectByKeyAsync(3) is not null;
            foreignUpdate = await documents.UpdateAsync(new Document { Id = 3, Title = "Hijacked", TenantId = 1 });
            foreignDelete = await documents.DeleteAsync(3);

            // Writes to the tenant's own rows must still succeed, so the 0-row results above come from the filter.
            ownUpdate = await documents.UpdateAsync(new Document { Id = 1, Title = "A plan v2", TenantId = 1 });
            ownDelete = await documents.DeleteAsync(2);
            ownEdited = await documents.SelectByKeyAsync(1);
            ownDeleted = await documents.SelectByKeyAsync(2);
        }

        Document? untouched;
        using (InquiryFilterContext.BeginScope(new Dictionary<string, object> { ["TenantId"] = 2 }))
        {
            untouched = await documents.SelectByKeyAsync(3);
        }

        var unscoped = "ran";
        try
        {
            await documents.SelectAllAsync();
        }
        catch (InquiryFilterValueMissingException)
        {
            unscoped = "throws InquiryFilterValueMissingException";
        }

        Check(visible == 2, $"Tenant 1 sees {visible} documents.");
        Check(!foreignRead && !foreignUpdate && !foreignDelete, "Tenant 1 reached tenant 2's document.");
        Check(ownUpdate && ownDelete && ownEdited is { Title: "A plan v2" } && ownDeleted is null, "Tenant 1 could not change its own documents.");
        Check(untouched is { Title: "B secrets", TenantId: 2 }, "Tenant 2's document changed.");
        Check(unscoped != "ran", "A read without a tenant scope ran.");
        return $"tenant-isolation: tenant 1 sees {visible} of 3 documents; read, update and delete of tenant 2's document reached 0 rows; its own update and delete succeeded; no scope {unscoped}";
    }

    public async Task<string> SelectiveEagerAsync()
    {
        var orders = services.GetRequiredService<SalesOrderStore>();
        var selective = await orders.SelectWithLinesAsync(_orderId)
            ?? throw new InvalidOperationException("The order was not found.");
        var full = await orders.SelectWithRelationsAsync(_orderId)
            ?? throw new InvalidOperationException("The order was not found.");
        Check(selective is { Lines.Count: 3, Customer: null }, "The selective load did not load only Lines.");
        Check(full is { Lines.Count: 3, Customer.Name: "Northwind Traders" }, "The default eager load did not load both relations.");
        return $"selective-eager: [InquiryInclude(Lines)] loaded {selective.Lines.Count} lines and left Customer unloaded; the default eager load loaded both";
    }

    public async Task<string> HandwrittenSqlAsync()
    {
        // The aliases match the DTO properties by name, in a different order from the class.
        var report = await _inquiry.QueryListAsync<CustomerSales>($"""
            SELECT SUM(l.Quantity * l.UnitPriceCents) AS TotalCents,
                   COUNT(DISTINCT o.Id) AS OrderCount,
                   c.Name AS Customer,
                   SUM(l.Quantity) AS Units
            FROM Customer c
            JOIN SalesOrder o ON o.CustomerId = c.Id
            JOIN OrderLine l ON l.OrderId = o.Id
            WHERE c.Id = {_customerId}
            GROUP BY c.Name
            """);
        Check(report is [{ Customer: "Northwind Traders", OrderCount: 1, Units: 6, TotalCents: 6250 }], "The sales report is wrong.");

        // A result without a column for every property fails before the first row, so an empty result still fails.
        var validation = "accepted";
        try
        {
            await _inquiry.QueryListAsync<CustomerSales>($"SELECT Name AS Customer FROM Customer WHERE 1 = 0");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("'OrderCount'", StringComparison.Ordinal))
        {
            validation = "rejected";
        }

        Check(validation == "rejected", "A query with missing columns was accepted.");
        var row = report[0];
        return $"handwritten-sql: {row.Customer} has {row.OrderCount} order, {row.Units} units, {row.TotalCents} cents by alias; a query missing columns was {validation}";
    }

    public async Task<string> AotJsonAsync()
    {
        var customers = services.GetRequiredService<CustomerStore>();
        var saved = await customers.InsertReturningAsync(new Customer
        {
            Name = "Alfreds Futterkiste",
            Preferences = new ShippingPreferences { Carrier = "DHL", DeliveryWindows = ["Mon 09-12", "Thu 13-17"], GiftWrap = true },
        }) ?? throw new InvalidOperationException("The insert did not return a customer.");
        var loaded = await customers.SelectByKeyAsync(saved.Id);
        var stored = await _inquiry.ExecuteScalarAsync<string>($"SELECT Preferences FROM Customer WHERE Id = {saved.Id}");
        Check(
            loaded?.Preferences is { Carrier: "DHL", DeliveryWindows: ["Mon 09-12", "Thu 13-17"], GiftWrap: true },
            "The preferences did not round-trip.");
        Check(stored == """{"Carrier":"DHL","DeliveryWindows":["Mon 09-12","Thu 13-17"],"GiftWrap":true}""", $"Unexpected stored JSON: {stored}");
        return $"aot-json: Preferences round-tripped through JourneyJsonContext as {stored}";
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
