# Live-runtime provider testing & benchmarking — Implementation Plan

> **STATUS (2026-06-02):** Phases 0–8 are **COMPLETE** and merged to `main`. The **Phase 8/9
> live-environment benchmark** is now delivered too — the cross-provider apples-to-apples benchmark
> buildout across all five engines (see [`../../STATUS.md`](../../STATUS.md) §3.F). The `- [ ]` checkboxes
> below were **not** maintained during execution; treat STATUS.md as the authoritative status.

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Verify every Inquiry provider's *own* generated SQL against a real database engine (provisioned by Testcontainers) over a faithful, fully-indexed Northwind schema, on every PR, with a catalog-introspection guardrail that fails if anything is missing.

**Architecture:** Each provider test project linked-compiles the shared Northwind source under its *own* dialect + analyzer, so it bakes that engine's SQL. A per-assembly xUnit collection fixture starts one container via Testcontainers (graceful skip if Docker is absent). A shared test-support library holds the canonical expected-schema contract and a fidelity comparator; per-provider introspectors read the live catalog and assert the schema matches. A schema stood up from Inquiry's own `InquiryGeneratedSchema.Ddl` is verified the same way. CI runs PG/MySQL/SQL Server per PR and Oracle nightly. The benchmark gains a dialect-selection MSBuild property that provisions via the same containers.

**Tech Stack:** .NET (net6.0–net10.0), xUnit, `Xunit.SkippableFact`, Testcontainers (`Testcontainers.PostgreSql`/`MySql`/`MsSql`/`Oracle`), Npgsql / MySqlConnector / Microsoft.Data.SqlClient / Oracle.ManagedDataAccess.Core, BenchmarkDotNet, GitHub Actions.

**Branch:** `feature/live-runtime-testing` (already checked out; spec at `docs/superpowers/specs/2026-06-01-live-runtime-testing-design.md`).

**Dialect strings (exact):** `Sqlite`, `SqlServer`, `PostgreSql`, `MySql`, `Oracle`.

**Convention for every "Commit" step:** write the message to a BOM-free file and commit via file, then delete it:
```powershell
[System.IO.File]::WriteAllText("$PWD\.git\CM.txt", "<message>`n`nCo-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>", (New-Object System.Text.UTF8Encoding $false))
git commit -q -F .git\CM.txt; Remove-Item .git\CM.txt
```

---

## File Structure

**New shared library — `tests/Inquiry.IntegrationTesting/`** (framework-agnostic, `netstandard2.0`):
- `Inquiry.IntegrationTesting.csproj` — no test-framework deps; pure data + comparison.
- `SchemaModel.cs` — `SchemaSnapshot`, `TableSnapshot`, `ColumnSnapshot`, `ForeignKeySnapshot`, `IndexSnapshot` records.
- `ExpectedNorthwindSchema.cs` — the canonical `SchemaSnapshot` for Northwind (the contract).
- `SchemaFidelity.cs` — `AssertMatches(expected, actual)`, case-insensitive identifier matching, throws `SchemaFidelityException` with a full discrepancy list.
- `ISchemaIntrospector.cs` — `Task<SchemaSnapshot> ReadAsync(DbConnection conn, ...)`.

**Per provider test project** (`tests/Inquiry.<Provider>.Tests/`):
- `<Provider>.csproj` — drop `Inquiry.Northwind` ProjectReference; linked-compile Northwind source; add Testcontainers + SkippableFact + IntegrationTesting refs.
- `AssemblyDialect.cs` — `[assembly: InquiryDialect("<Dialect>")]`.
- `Fixtures/<Provider>ContainerFixture.cs` — collection fixture; starts container; `IsAvailable`/`SkipReason`/`AdminConnectionString`.
- `Fixtures/<Provider>Collection.cs` — `[CollectionDefinition]`.
- `Fixtures/<Provider>TestHarness.cs` — refactor `CreateAsync` to take admin connection string.
- `Fixtures/<Provider>SchemaIntrospector.cs` — reads the live catalog into a `SchemaSnapshot`.
- `SchemaFidelityIntegrationTests.cs` — asserts hand-written DDL schema matches the contract.
- `GeneratedDdlIntegrationTests.cs` — stands up `InquiryGeneratedSchema.Ddl`; CRUD + fidelity.
- Existing CRUD/coverage tests — convert `[<Provider>Fact]` → `[SkippableFact]` + collection + guard.

**Modified shared source** (`samples/Inquiry.Northwind/`):
- `NorthwindSchema.cs` — add the classic secondary-index `CREATE INDEX` statements to all five DDLs.
- `Models/*.cs` — add `[InquiryColumn(Length=…, IsIndexed=…, Precision/Scale=…)]` metadata.

**SQLite test project** (`tests/Inquiry.Sqlite.Tests/`):
- Add `Fixtures/SqliteSchemaIntrospector.cs` + `NorthwindFidelityIntegrationTests.cs` (in-process, no Docker — the TDD anchor for WS2).
- Add a `ProjectReference` to `Inquiry.IntegrationTesting` and link the Northwind source for a SQLite fidelity check, OR reference `Inquiry.Northwind` + `NorthwindSchema.SqliteDdl` (it already can). Use `NorthwindSchema.SqliteDdl` (no linked source needed here).

**CI / build:**
- `.github/workflows/ci.yml`, `.github/workflows/nightly.yml`.

**Benchmark** (`benchmarks/Inquiry.Benchmarks/`):
- `Inquiry.Benchmarks.csproj` — `InquiryBenchProvider` property + conditional refs.
- `AssemblyDialect.*.cs` + provider benchmark wiring.

---

## The canonical Northwind contract (used verbatim in Task 2)

Tables, PKs, FK targets, nullability, and the classic secondary indexes:

- **Categories** PK `CategoryID`; cols: CategoryID(NN), CategoryName(NN), Description(NULL), Picture(NULL); idx: CategoryName.
- **Region** PK `RegionID`; cols: RegionID(NN), RegionDescription(NN).
- **Territories** PK `TerritoryID`; cols: TerritoryID(NN), TerritoryDescription(NN), RegionID(NN); FK (RegionID)->Region(RegionID).
- **Suppliers** PK `SupplierID`; cols: SupplierID(NN), CompanyName(NN), ContactName, ContactTitle, Address, City, Region, PostalCode, Country, Phone, Fax, HomePage (all NULL except first two); idx: CompanyName, PostalCode.
- **Customers** PK `CustomerID`; cols: CustomerID(NN), CompanyName(NN), ContactName, ContactTitle, Address, City, Region, PostalCode, Country, Phone, Fax (rest NULL); idx: City, CompanyName, PostalCode, Region.
- **CustomerDemographics** PK `CustomerTypeID`; cols: CustomerTypeID(NN), CustomerDesc(NULL).
- **CustomerCustomerDemo** PK `(CustomerID, CustomerTypeID)`; cols both NN; FK (CustomerID)->Customers(CustomerID), (CustomerTypeID)->CustomerDemographics(CustomerTypeID).
- **Employees** PK `EmployeeID`; cols: EmployeeID(NN), LastName(NN), FirstName(NN), Title, TitleOfCourtesy, BirthDate, HireDate, Address, City, Region, PostalCode, Country, HomePhone, Extension, Photo, Notes, ReportsTo, PhotoPath (rest NULL); FK (ReportsTo)->Employees(EmployeeID); idx: LastName, PostalCode.
- **EmployeeTerritories** PK `(EmployeeID, TerritoryID)`; cols both NN; FK (EmployeeID)->Employees(EmployeeID), (TerritoryID)->Territories(TerritoryID).
- **Shippers** PK `ShipperID`; cols: ShipperID(NN), CompanyName(NN), Phone(NULL).
- **Products** PK `ProductID`; cols: ProductID(NN), ProductName(NN), SupplierID, CategoryID, QuantityPerUnit, UnitPrice, UnitsInStock, UnitsOnOrder, ReorderLevel (NULL), Discontinued(NN); FK (SupplierID)->Suppliers(SupplierID), (CategoryID)->Categories(CategoryID); idx: CategoryID, ProductName, SupplierID.
- **Orders** PK `OrderID`; cols: OrderID(NN), CustomerID, EmployeeID, OrderDate, RequiredDate, ShippedDate, ShipVia, Freight, ShipName, ShipAddress, ShipCity, ShipRegion, ShipPostalCode, ShipCountry (all NULL except OrderID); FK (CustomerID)->Customers, (EmployeeID)->Employees, (ShipVia)->Shippers(ShipperID); idx: CustomerID, EmployeeID, OrderDate, ShippedDate, ShipVia, ShipPostalCode.
- **Order Details** PK `(OrderID, ProductID)`; cols: OrderID(NN), ProductID(NN), UnitPrice(NN), Quantity(NN), Discount(NN); FK (OrderID)->Orders(OrderID), (ProductID)->Products(ProductID); idx: OrderID, ProductID.

Identifier comparison in `SchemaFidelity` is **case-insensitive** (Oracle folds unquoted names to uppercase; PostgreSQL preserves quoted mixed case). An expected index on columns X is satisfied if any actual index's leading columns equal X (order-sensitive prefix); this lets a composite-PK index satisfy a leading-column expectation.

---

## Phase 0 — Packages

### Task 0: Add package versions

**Files:**
- Modify: `Directory.Packages.props`

- [ ] **Step 1: Add the new `PackageVersion` entries**

Insert these lines into the existing `<ItemGroup>` (keep the list alphabetical-ish; versions current as of 2026-06):

```xml
    <PackageVersion Include="Testcontainers.PostgreSql" Version="3.10.0" />
    <PackageVersion Include="Testcontainers.MySql" Version="3.10.0" />
    <PackageVersion Include="Testcontainers.MsSql" Version="3.10.0" />
    <PackageVersion Include="Testcontainers.Oracle" Version="3.10.0" />
    <PackageVersion Include="Xunit.SkippableFact" Version="1.4.13" />
```

- [ ] **Step 2: Verify the solution still restores**

Run: `dotnet restore`
Expected: restore succeeds (no version-conflict errors).

- [ ] **Step 3: Commit**

Message: `chore: add Testcontainers + SkippableFact package versions`

---

## Phase 1 — Shared test-support library

### Task 1: Create `Inquiry.IntegrationTesting` with the schema model

**Files:**
- Create: `tests/Inquiry.IntegrationTesting/Inquiry.IntegrationTesting.csproj`
- Create: `tests/Inquiry.IntegrationTesting/SchemaModel.cs`

- [ ] **Step 1: Create the csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
</Project>
```

- [ ] **Step 2: Create the model records**

```csharp
using System.Collections.Generic;

namespace Inquiry.IntegrationTesting;

public sealed record ColumnSnapshot(string Name, bool IsNullable);

public sealed record ForeignKeySnapshot(
    IReadOnlyList<string> Columns,
    string ReferencedTable,
    IReadOnlyList<string> ReferencedColumns);

/// <summary>An index described by its ordered column list; matched name-agnostically.</summary>
public sealed record IndexSnapshot(IReadOnlyList<string> Columns);

public sealed record TableSnapshot(
    string Name,
    IReadOnlyList<ColumnSnapshot> Columns,
    IReadOnlyList<string> PrimaryKey,
    IReadOnlyList<ForeignKeySnapshot> ForeignKeys,
    IReadOnlyList<IndexSnapshot> Indexes);

public sealed record SchemaSnapshot(IReadOnlyList<TableSnapshot> Tables);
```

- [ ] **Step 3: Build the project**

Run: `dotnet build tests/Inquiry.IntegrationTesting/Inquiry.IntegrationTesting.csproj`
Expected: build succeeds.

- [ ] **Step 4: Commit**

Message: `test(infra): add Inquiry.IntegrationTesting schema model`

### Task 2: Add the canonical expected Northwind contract

**Files:**
- Create: `tests/Inquiry.IntegrationTesting/ExpectedNorthwindSchema.cs`

- [ ] **Step 1: Encode the contract** (from the table above)

```csharp
using System.Collections.Generic;

namespace Inquiry.IntegrationTesting;

/// <summary>The single source of truth for what a faithful Northwind schema must contain.
/// Identifier comparison is case-insensitive, so engine casing differences do not matter.</summary>
public static class ExpectedNorthwindSchema
{
    private static ColumnSnapshot N(string name) => new(name, true);   // nullable
    private static ColumnSnapshot R(string name) => new(name, false);  // required (NOT NULL)
    private static ForeignKeySnapshot Fk(string col, string refTable, string refCol)
        => new(new[] { col }, refTable, new[] { refCol });
    private static IndexSnapshot Ix(params string[] cols) => new(cols);

    public static readonly SchemaSnapshot Schema = new(new[]
    {
        new TableSnapshot("Categories",
            new[] { R("CategoryID"), R("CategoryName"), N("Description"), N("Picture") },
            new[] { "CategoryID" },
            new ForeignKeySnapshot[0],
            new[] { Ix("CategoryName") }),

        new TableSnapshot("Region",
            new[] { R("RegionID"), R("RegionDescription") },
            new[] { "RegionID" },
            new ForeignKeySnapshot[0],
            new IndexSnapshot[0]),

        new TableSnapshot("Territories",
            new[] { R("TerritoryID"), R("TerritoryDescription"), R("RegionID") },
            new[] { "TerritoryID" },
            new[] { Fk("RegionID", "Region", "RegionID") },
            new IndexSnapshot[0]),

        new TableSnapshot("Suppliers",
            new[] { R("SupplierID"), R("CompanyName"), N("ContactName"), N("ContactTitle"),
                    N("Address"), N("City"), N("Region"), N("PostalCode"), N("Country"),
                    N("Phone"), N("Fax"), N("HomePage") },
            new[] { "SupplierID" },
            new ForeignKeySnapshot[0],
            new[] { Ix("CompanyName"), Ix("PostalCode") }),

        new TableSnapshot("Customers",
            new[] { R("CustomerID"), R("CompanyName"), N("ContactName"), N("ContactTitle"),
                    N("Address"), N("City"), N("Region"), N("PostalCode"), N("Country"),
                    N("Phone"), N("Fax") },
            new[] { "CustomerID" },
            new ForeignKeySnapshot[0],
            new[] { Ix("City"), Ix("CompanyName"), Ix("PostalCode"), Ix("Region") }),

        new TableSnapshot("CustomerDemographics",
            new[] { R("CustomerTypeID"), N("CustomerDesc") },
            new[] { "CustomerTypeID" },
            new ForeignKeySnapshot[0],
            new IndexSnapshot[0]),

        new TableSnapshot("CustomerCustomerDemo",
            new[] { R("CustomerID"), R("CustomerTypeID") },
            new[] { "CustomerID", "CustomerTypeID" },
            new[] { Fk("CustomerID", "Customers", "CustomerID"),
                    Fk("CustomerTypeID", "CustomerDemographics", "CustomerTypeID") },
            new IndexSnapshot[0]),

        new TableSnapshot("Employees",
            new[] { R("EmployeeID"), R("LastName"), R("FirstName"), N("Title"), N("TitleOfCourtesy"),
                    N("BirthDate"), N("HireDate"), N("Address"), N("City"), N("Region"),
                    N("PostalCode"), N("Country"), N("HomePhone"), N("Extension"), N("Photo"),
                    N("Notes"), N("ReportsTo"), N("PhotoPath") },
            new[] { "EmployeeID" },
            new[] { Fk("ReportsTo", "Employees", "EmployeeID") },
            new[] { Ix("LastName"), Ix("PostalCode") }),

        new TableSnapshot("EmployeeTerritories",
            new[] { R("EmployeeID"), R("TerritoryID") },
            new[] { "EmployeeID", "TerritoryID" },
            new[] { Fk("EmployeeID", "Employees", "EmployeeID"),
                    Fk("TerritoryID", "Territories", "TerritoryID") },
            new IndexSnapshot[0]),

        new TableSnapshot("Shippers",
            new[] { R("ShipperID"), R("CompanyName"), N("Phone") },
            new[] { "ShipperID" },
            new ForeignKeySnapshot[0],
            new IndexSnapshot[0]),

        new TableSnapshot("Products",
            new[] { R("ProductID"), R("ProductName"), N("SupplierID"), N("CategoryID"),
                    N("QuantityPerUnit"), N("UnitPrice"), N("UnitsInStock"), N("UnitsOnOrder"),
                    N("ReorderLevel"), R("Discontinued") },
            new[] { "ProductID" },
            new[] { Fk("SupplierID", "Suppliers", "SupplierID"),
                    Fk("CategoryID", "Categories", "CategoryID") },
            new[] { Ix("CategoryID"), Ix("ProductName"), Ix("SupplierID") }),

        new TableSnapshot("Orders",
            new[] { R("OrderID"), N("CustomerID"), N("EmployeeID"), N("OrderDate"), N("RequiredDate"),
                    N("ShippedDate"), N("ShipVia"), N("Freight"), N("ShipName"), N("ShipAddress"),
                    N("ShipCity"), N("ShipRegion"), N("ShipPostalCode"), N("ShipCountry") },
            new[] { "OrderID" },
            new[] { Fk("CustomerID", "Customers", "CustomerID"),
                    Fk("EmployeeID", "Employees", "EmployeeID"),
                    Fk("ShipVia", "Shippers", "ShipperID") },
            new[] { Ix("CustomerID"), Ix("EmployeeID"), Ix("OrderDate"),
                    Ix("ShippedDate"), Ix("ShipVia"), Ix("ShipPostalCode") }),

        new TableSnapshot("Order Details",
            new[] { R("OrderID"), R("ProductID"), R("UnitPrice"), R("Quantity"), R("Discount") },
            new[] { "OrderID", "ProductID" },
            new[] { Fk("OrderID", "Orders", "OrderID"),
                    Fk("ProductID", "Products", "ProductID") },
            new[] { Ix("OrderID"), Ix("ProductID") }),
    });
}
```

- [ ] **Step 2: Build**

Run: `dotnet build tests/Inquiry.IntegrationTesting/Inquiry.IntegrationTesting.csproj`
Expected: build succeeds.

- [ ] **Step 3: Commit**

Message: `test(infra): add canonical expected Northwind schema contract`

### Task 3: Add the fidelity comparator (TDD)

**Files:**
- Create: `tests/Inquiry.IntegrationTesting/SchemaFidelity.cs`
- Create: `tests/Inquiry.IntegrationTesting/ISchemaIntrospector.cs`
- Test: a temporary unit test in `tests/Inquiry.Tests/SchemaFidelityTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/Inquiry.Tests/SchemaFidelityTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using Inquiry.IntegrationTesting;
using Xunit;

namespace Inquiry.Tests;

public sealed class SchemaFidelityTests
{
    private static SchemaSnapshot OneTable(params IndexSnapshot[] indexes) => new(new[]
    {
        new TableSnapshot("Categories",
            new[] { new ColumnSnapshot("CategoryID", false), new ColumnSnapshot("CategoryName", false) },
            new[] { "CategoryID" }, new ForeignKeySnapshot[0], indexes),
    });

    [Fact]
    public void IdenticalSchemasMatch()
    {
        var s = OneTable(new IndexSnapshot(new[] { "CategoryName" }));
        SchemaFidelity.AssertMatches(s, s); // does not throw
    }

    [Fact]
    public void CaseInsensitiveIdentifiersMatch()
    {
        var expected = OneTable(new IndexSnapshot(new[] { "CategoryName" }));
        var actual = new SchemaSnapshot(new[]
        {
            new TableSnapshot("CATEGORIES",
                new[] { new ColumnSnapshot("CATEGORYID", false), new ColumnSnapshot("CATEGORYNAME", false) },
                new[] { "CATEGORYID" }, new ForeignKeySnapshot[0],
                new[] { new IndexSnapshot(new[] { "CATEGORYNAME" }) }),
        });
        SchemaFidelity.AssertMatches(expected, actual); // does not throw
    }

    [Fact]
    public void MissingIndexThrows()
    {
        var expected = OneTable(new IndexSnapshot(new[] { "CategoryName" }));
        var actual = OneTable(); // no secondary index
        var ex = Assert.Throws<SchemaFidelityException>(() => SchemaFidelity.AssertMatches(expected, actual));
        Assert.Contains("CategoryName", ex.Message);
    }

    [Fact]
    public void NullabilityMismatchThrows()
    {
        var expected = OneTable();
        var actual = new SchemaSnapshot(new[]
        {
            new TableSnapshot("Categories",
                new[] { new ColumnSnapshot("CategoryID", false), new ColumnSnapshot("CategoryName", true) },
                new[] { "CategoryID" }, new ForeignKeySnapshot[0], new IndexSnapshot[0]),
        });
        Assert.Throws<SchemaFidelityException>(() => SchemaFidelity.AssertMatches(expected, actual));
    }

    [Fact]
    public void CompositePkIndexSatisfiesLeadingColumnExpectation()
    {
        var expected = OneTable(new IndexSnapshot(new[] { "CategoryID" }));
        var actual = new SchemaSnapshot(new[]
        {
            new TableSnapshot("Categories",
                new[] { new ColumnSnapshot("CategoryID", false), new ColumnSnapshot("CategoryName", false) },
                new[] { "CategoryID" }, new ForeignKeySnapshot[0],
                new[] { new IndexSnapshot(new[] { "CategoryID", "CategoryName" }) }), // composite leads with CategoryID
        });
        SchemaFidelity.AssertMatches(expected, actual); // prefix match → ok
    }
}
```

Add a ProjectReference so the test compiles. Modify `tests/Inquiry.Tests/Inquiry.Tests.csproj` to add:
```xml
    <ProjectReference Include="..\Inquiry.IntegrationTesting\Inquiry.IntegrationTesting.csproj" />
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Inquiry.Tests/Inquiry.Tests.csproj --filter SchemaFidelityTests`
Expected: FAIL — `SchemaFidelity` / `SchemaFidelityException` not defined.

- [ ] **Step 3: Implement `ISchemaIntrospector` and `SchemaFidelity`**

`tests/Inquiry.IntegrationTesting/ISchemaIntrospector.cs`:
```csharp
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace Inquiry.IntegrationTesting;

/// <summary>Reads the live catalog of an already-created schema into a <see cref="SchemaSnapshot"/>.</summary>
public interface ISchemaIntrospector
{
    Task<SchemaSnapshot> ReadAsync(DbConnection connection, CancellationToken cancellationToken = default);
}
```

`tests/Inquiry.IntegrationTesting/SchemaFidelity.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.Linq;

namespace Inquiry.IntegrationTesting;

public sealed class SchemaFidelityException : Exception
{
    public SchemaFidelityException(string message) : base(message) { }
}

public static class SchemaFidelity
{
    private static readonly StringComparer Id = StringComparer.OrdinalIgnoreCase;

    /// <summary>Asserts every expected table/column/PK/FK/index is present in <paramref name="actual"/>.
    /// Extra tables/indexes in actual are allowed. Throws with a full discrepancy list on mismatch.</summary>
    public static void AssertMatches(SchemaSnapshot expected, SchemaSnapshot actual)
    {
        var problems = new List<string>();
        foreach (var et in expected.Tables)
        {
            var at = actual.Tables.FirstOrDefault(t => Id.Equals(t.Name, et.Name));
            if (at is null) { problems.Add($"Missing table '{et.Name}'."); continue; }

            foreach (var ec in et.Columns)
            {
                var ac = at.Columns.FirstOrDefault(c => Id.Equals(c.Name, ec.Name));
                if (ac is null) { problems.Add($"{et.Name}: missing column '{ec.Name}'."); continue; }
                if (ac.IsNullable != ec.IsNullable)
                    problems.Add($"{et.Name}.{ec.Name}: nullability expected {ec.IsNullable}, found {ac.IsNullable}.");
            }

            if (!SameColumns(et.PrimaryKey, at.PrimaryKey))
                problems.Add($"{et.Name}: PK expected ({Join(et.PrimaryKey)}), found ({Join(at.PrimaryKey)}).");

            foreach (var efk in et.ForeignKeys)
            {
                var ok = at.ForeignKeys.Any(afk =>
                    SameColumns(afk.Columns, efk.Columns) &&
                    Id.Equals(afk.ReferencedTable, efk.ReferencedTable) &&
                    SameColumns(afk.ReferencedColumns, efk.ReferencedColumns));
                if (!ok)
                    problems.Add($"{et.Name}: missing FK ({Join(efk.Columns)}) -> {efk.ReferencedTable}({Join(efk.ReferencedColumns)}).");
            }

            foreach (var eix in et.Indexes)
            {
                var ok = at.Indexes.Any(aix => LeadsWith(aix.Columns, eix.Columns));
                if (!ok)
                    problems.Add($"{et.Name}: missing index on ({Join(eix.Columns)}).");
            }
        }

        if (problems.Count > 0)
            throw new SchemaFidelityException(
                "Schema fidelity check failed:" + Environment.NewLine +
                string.Join(Environment.NewLine, problems.Select(p => "  - " + p)));
    }

    private static bool SameColumns(IReadOnlyList<string> a, IReadOnlyList<string> b)
        => a.Count == b.Count && a.Zip(b, (x, y) => Id.Equals(x, y)).All(eq => eq);

    /// <summary>True when actual index columns start with the expected column sequence (order-sensitive prefix).</summary>
    private static bool LeadsWith(IReadOnlyList<string> actual, IReadOnlyList<string> expected)
        => actual.Count >= expected.Count &&
           expected.Select((c, i) => Id.Equals(actual[i], c)).All(eq => eq);

    private static string Join(IReadOnlyList<string> cols) => string.Join(", ", cols);
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/Inquiry.Tests/Inquiry.Tests.csproj --filter SchemaFidelityTests`
Expected: PASS (all 5).

- [ ] **Step 5: Commit**

Message: `test(infra): add schema-fidelity comparator with unit tests`

---

## Phase 2 — WS2 faithful schema (SQLite TDD anchor + entity annotations)

### Task 4: SQLite catalog introspector

**Files:**
- Create: `tests/Inquiry.Sqlite.Tests/Fixtures/SqliteSchemaIntrospector.cs`
- Modify: `tests/Inquiry.Sqlite.Tests/Inquiry.Sqlite.Tests.csproj` (add IntegrationTesting reference)

- [ ] **Step 1: Add the project reference**

Add to `tests/Inquiry.Sqlite.Tests/Inquiry.Sqlite.Tests.csproj` `<ItemGroup>`:
```xml
    <ProjectReference Include="..\Inquiry.IntegrationTesting\Inquiry.IntegrationTesting.csproj" />
```

- [ ] **Step 2: Implement the introspector** using SQLite PRAGMAs

```csharp
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Inquiry.IntegrationTesting;

namespace Inquiry.Sqlite.Tests.Fixtures;

public sealed class SqliteSchemaIntrospector : ISchemaIntrospector
{
    public async Task<SchemaSnapshot> ReadAsync(DbConnection connection, CancellationToken ct = default)
    {
        var tableNames = new List<string>();
        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%';";
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct)) tableNames.Add(r.GetString(0));
        }

        var tables = new List<TableSnapshot>();
        foreach (var table in tableNames)
        {
            var columns = new List<ColumnSnapshot>();
            var pk = new List<(int Seq, string Col)>();
            await using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = $"PRAGMA table_info(\"{table}\");";
                await using var r = await cmd.ExecuteReaderAsync(ct);
                while (await r.ReadAsync(ct))
                {
                    var name = r.GetString(1);
                    var notNull = r.GetInt32(3) == 1;
                    var pkOrd = r.GetInt32(5);
                    columns.Add(new ColumnSnapshot(name, !notNull));
                    if (pkOrd > 0) pk.Add((pkOrd, name));
                }
            }
            pk.Sort((a, b) => a.Seq.CompareTo(b.Seq));

            var fks = new List<ForeignKeySnapshot>();
            await using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = $"PRAGMA foreign_key_list(\"{table}\");";
                await using var r = await cmd.ExecuteReaderAsync(ct);
                while (await r.ReadAsync(ct))
                    fks.Add(new ForeignKeySnapshot(new[] { r.GetString(3) }, r.GetString(2), new[] { r.GetString(4) }));
            }

            var indexes = new List<IndexSnapshot>();
            var indexNames = new List<string>();
            await using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = $"PRAGMA index_list(\"{table}\");";
                await using var r = await cmd.ExecuteReaderAsync(ct);
                while (await r.ReadAsync(ct)) indexNames.Add(r.GetString(1));
            }
            foreach (var ix in indexNames)
            {
                var cols = new List<string>();
                await using var cmd = connection.CreateCommand();
                cmd.CommandText = $"PRAGMA index_info(\"{ix}\");";
                await using var r = await cmd.ExecuteReaderAsync(ct);
                while (await r.ReadAsync(ct)) cols.Add(r.GetString(2));
                if (cols.Count > 0) indexes.Add(new IndexSnapshot(cols));
            }

            tables.Add(new TableSnapshot(table, columns, pk.ConvertAll(x => x.Col), fks, indexes));
        }

        return new SchemaSnapshot(tables);
    }
}
```

- [ ] **Step 3: Build the test project**

Run: `dotnet build tests/Inquiry.Sqlite.Tests/Inquiry.Sqlite.Tests.csproj -f net8.0`
Expected: build succeeds.

- [ ] **Step 4: Commit**

Message: `test(sqlite): add SQLite catalog introspector`

### Task 5: SQLite Northwind fidelity test (fails until indexes added)

**Files:**
- Create: `tests/Inquiry.Sqlite.Tests/NorthwindFidelityIntegrationTests.cs`

Note: this requires `NorthwindSchema.SqliteDdl`. The SQLite test project already references `Inquiry.Northwind` (verify; if not, add the ProjectReference to it). This test runs **in-process, no Docker**.

- [ ] **Step 1: Write the failing test**

```csharp
using System.Threading.Tasks;
using Inquiry.IntegrationTesting;
using Inquiry.Northwind;
using Inquiry.Sqlite.Tests.Fixtures;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Inquiry.Sqlite.Tests;

/// <summary>WS2/WS4 anchor: the hand-written SQLite Northwind DDL must produce a schema that
/// matches the canonical contract — including the full classic secondary-index set.</summary>
public sealed class NorthwindFidelityIntegrationTests
{
    [Fact]
    public async Task SqliteNorthwindMatchesExpectedContract()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(NorthwindSchema.SqliteDdl, "Fidelity");
        await using var conn = new SqliteConnection(harness.ConnectionString);
        await conn.OpenAsync();

        var actual = await new SqliteSchemaIntrospector().ReadAsync(conn);
        SchemaFidelity.AssertMatches(ExpectedNorthwindSchema.Schema, actual);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/Inquiry.Sqlite.Tests/Inquiry.Sqlite.Tests.csproj -f net8.0 --filter NorthwindFidelityIntegrationTests`
Expected: FAIL — `SchemaFidelityException` listing missing indexes (CategoryName, Customers.City, etc.).

- [ ] **Step 3: Add the classic indexes to `NorthwindSchema.SqliteDdl`**

In `samples/Inquiry.Northwind/NorthwindSchema.cs`, append these statements to the **end** of the `SqliteDdl` string (before the closing `"""`):

```sql
        CREATE INDEX IF NOT EXISTS IX_Categories_CategoryName ON Categories (CategoryName);
        CREATE INDEX IF NOT EXISTS IX_Suppliers_CompanyName ON Suppliers (CompanyName);
        CREATE INDEX IF NOT EXISTS IX_Suppliers_PostalCode ON Suppliers (PostalCode);
        CREATE INDEX IF NOT EXISTS IX_Customers_City ON Customers (City);
        CREATE INDEX IF NOT EXISTS IX_Customers_CompanyName ON Customers (CompanyName);
        CREATE INDEX IF NOT EXISTS IX_Customers_PostalCode ON Customers (PostalCode);
        CREATE INDEX IF NOT EXISTS IX_Customers_Region ON Customers (Region);
        CREATE INDEX IF NOT EXISTS IX_Employees_LastName ON Employees (LastName);
        CREATE INDEX IF NOT EXISTS IX_Employees_PostalCode ON Employees (PostalCode);
        CREATE INDEX IF NOT EXISTS IX_Products_CategoryID ON Products (CategoryID);
        CREATE INDEX IF NOT EXISTS IX_Products_ProductName ON Products (ProductName);
        CREATE INDEX IF NOT EXISTS IX_Products_SupplierID ON Products (SupplierID);
        CREATE INDEX IF NOT EXISTS IX_Orders_CustomerID ON Orders (CustomerID);
        CREATE INDEX IF NOT EXISTS IX_Orders_EmployeeID ON Orders (EmployeeID);
        CREATE INDEX IF NOT EXISTS IX_Orders_OrderDate ON Orders (OrderDate);
        CREATE INDEX IF NOT EXISTS IX_Orders_ShippedDate ON Orders (ShippedDate);
        CREATE INDEX IF NOT EXISTS IX_Orders_ShipVia ON Orders (ShipVia);
        CREATE INDEX IF NOT EXISTS IX_Orders_ShipPostalCode ON Orders (ShipPostalCode);
        CREATE INDEX IF NOT EXISTS "IX_Order_Details_OrderID" ON "Order Details" (OrderID);
        CREATE INDEX IF NOT EXISTS "IX_Order_Details_ProductID" ON "Order Details" (ProductID);
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/Inquiry.Sqlite.Tests/Inquiry.Sqlite.Tests.csproj -f net8.0 --filter NorthwindFidelityIntegrationTests`
Expected: PASS.

- [ ] **Step 5: Run the full SQLite suite (no regressions)**

Run: `dotnet test tests/Inquiry.Sqlite.Tests/Inquiry.Sqlite.Tests.csproj -f net8.0`
Expected: PASS.

- [ ] **Step 6: Commit**

Message: `test(sqlite): add Northwind fidelity test; add classic indexes to SQLite DDL`

### Task 6: Annotate Northwind entities for faithful generated DDL

This makes Inquiry's own `InquiryGeneratedSchema.Ddl` (W7/W7b) produce the bounded lengths + indexes, used by the WS5 generated-DDL tests later. It does not affect the hand-written DDL.

**Files:**
- Modify: `samples/Inquiry.Northwind/Models/*.cs`

- [ ] **Step 1: Add index + length + precision metadata**

For each indexed column from the contract, add `IsIndexed = true` to its `[InquiryColumn(...)]`. For bounded string keys add `Length`. For decimals add `Precision`/`Scale`. Example — `samples/Inquiry.Northwind/Models/Customer.cs` `City`:
```csharp
    [InquiryColumn("City", IsIndexed = true)]
    public string? City { get; set; }
```
Apply to: Categories.CategoryName; Suppliers.CompanyName, Suppliers.PostalCode; Customers.City/CompanyName/PostalCode/Region; Employees.LastName/PostalCode; Products.CategoryID/ProductName/SupplierID; Orders.CustomerID/EmployeeID/OrderDate/ShippedDate/ShipVia/ShipPostalCode; OrderDetail.OrderID/ProductID. For `Product.UnitPrice`, `Order.Freight`, `OrderDetail.UnitPrice`: add `Precision = 19, Scale = 4`. For string PKs (`Customer.CustomerID` Length=5, `CustomerDemographic.CustomerTypeID` Length=10, `Territory.TerritoryID` Length=40): add `Length`.

(Property/column names are in each model file; open the file, find the `[InquiryColumn]` for the listed column, and add the named argument.)

- [ ] **Step 2: Build the Northwind sample (SQLite-baked) — annotations must compile**

Run: `dotnet build samples/Inquiry.Northwind/Inquiry.Northwind.csproj`
Expected: build succeeds (no analyzer diagnostics).

- [ ] **Step 3: Verify generated SQLite DDL now includes an index** (sanity, in-process)

Run: `dotnet test tests/Inquiry.Sqlite.Tests/Inquiry.Sqlite.Tests.csproj -f net8.0`
Expected: PASS (existing `SchemaIndexIntegrationTests`/`SchemaDdlIntegrationTests` still green).

- [ ] **Step 4: Commit**

Message: `feat(northwind): annotate entities with index/length/precision metadata`

---

## Phase 3 — PostgreSQL end-to-end (the template)

### Task 7: Switch the PG test project to per-dialect compilation

**Files:**
- Modify: `tests/Inquiry.PostgreSql.Tests/Inquiry.PostgreSql.Tests.csproj`
- Create: `tests/Inquiry.PostgreSql.Tests/AssemblyDialect.cs`

- [ ] **Step 1: Replace the Northwind ProjectReference with linked source + add packages**

Edit the csproj `<ItemGroup>`: remove the line
`<ProjectReference Include="..\..\samples\Inquiry.Northwind\Inquiry.Northwind.csproj" />`
and add:
```xml
    <PackageReference Include="Testcontainers.PostgreSql" />
    <PackageReference Include="Xunit.SkippableFact" />
    <ProjectReference Include="..\Inquiry.IntegrationTesting\Inquiry.IntegrationTesting.csproj" />

    <Compile Include="..\..\samples\Inquiry.Northwind\Models\**\*.cs" LinkBase="Northwind\Models" />
    <Compile Include="..\..\samples\Inquiry.Northwind\Stores\**\*.cs" LinkBase="Northwind\Stores" />
    <Compile Include="..\..\samples\Inquiry.Northwind\NorthwindSchema.cs" LinkBase="Northwind" />
```
(Keep the existing `Inquiry` and `Inquiry.PostgreSql` ProjectReferences and `Npgsql`.)

- [ ] **Step 2: Add the dialect attribute**

`tests/Inquiry.PostgreSql.Tests/AssemblyDialect.cs`:
```csharp
using Inquiry;

[assembly: InquiryDialect("PostgreSql")]
```

- [ ] **Step 3: Build — confirm the PG analyzer bakes PG SQL with no errors**

Run: `dotnet build tests/Inquiry.PostgreSql.Tests/Inquiry.PostgreSql.Tests.csproj -f net8.0`
Expected: build succeeds. (A failure here means a store shape the PG analyzer can't emit — a real provider gap to fix before continuing.)

- [ ] **Step 4: Commit**

Message: `test(pg): compile Northwind under the PostgreSql dialect`

### Task 8: PostgreSQL container fixture + harness refactor

**Files:**
- Create: `tests/Inquiry.PostgreSql.Tests/Fixtures/PostgreSqlContainerFixture.cs`
- Create: `tests/Inquiry.PostgreSql.Tests/Fixtures/PostgreSqlCollection.cs`
- Modify: `tests/Inquiry.PostgreSql.Tests/Fixtures/PostgreSqlTestHarness.cs`
- Delete: `tests/Inquiry.PostgreSql.Tests/Fixtures/PostgreSqlFactAttribute.cs`

- [ ] **Step 1: Create the container fixture**

```csharp
using System;
using System.Threading.Tasks;
using Testcontainers.PostgreSql;
using Xunit;

namespace Inquiry.PostgreSql.Tests.Fixtures;

/// <summary>Starts one PostgreSQL container for the whole assembly. If Docker is unreachable,
/// <see cref="IsAvailable"/> stays false and tests skip rather than fail.</summary>
public sealed class PostgreSqlContainerFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _container;

    public bool IsAvailable { get; private set; }
    public string? SkipReason { get; private set; }
    public string AdminConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        try
        {
            _container = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();
            await _container.StartAsync();
            AdminConnectionString = _container.GetConnectionString();
            IsAvailable = true;
        }
        catch (Exception ex)
        {
            IsAvailable = false;
            SkipReason = "PostgreSQL container unavailable (is Docker running?): " + ex.Message;
        }
    }

    public async Task DisposeAsync()
    {
        if (_container is not null) await _container.DisposeAsync();
    }
}
```

- [ ] **Step 2: Create the collection definition**

```csharp
using Xunit;

namespace Inquiry.PostgreSql.Tests.Fixtures;

[CollectionDefinition(Name)]
public sealed class PostgreSqlCollection : ICollectionFixture<PostgreSqlContainerFixture>
{
    public const string Name = "PostgreSql";
}
```

- [ ] **Step 3: Refactor `PostgreSqlTestHarness.CreateAsync` to take the admin connection string**

Replace the env-var lookup. New signature and body (keep DB creation, DDL run, DI build, and disposal logic — only the source of `adminConnectionString` changes):
```csharp
    public static async Task<PostgreSqlTestHarness> CreateAsync(string adminConnectionString, string? namePrefix = null)
    {
        var prefix = (namePrefix ?? "inquiry").ToLowerInvariant();
        var databaseName = prefix + "_" + Guid.NewGuid().ToString("N");
        // ... unchanged: CREATE DATABASE, build connection string, run NorthwindSchema.PostgreSqlDdl,
        //     build ServiceProvider, return new PostgreSqlTestHarness(...).
```
Remove the `ConnectionStringEnvironmentVariable` constant and the env-var `throw`.

- [ ] **Step 4: Delete the obsolete Fact attribute**

Delete `tests/Inquiry.PostgreSql.Tests/Fixtures/PostgreSqlFactAttribute.cs`.

- [ ] **Step 5: Build**

Run: `dotnet build tests/Inquiry.PostgreSql.Tests/Inquiry.PostgreSql.Tests.csproj -f net8.0`
Expected: build FAILS — existing tests still use `[PostgreSqlFact]` and the old `CreateAsync`. Fixed in Task 9.

- [ ] **Step 6: Commit**

Message: `test(pg): add Testcontainers fixture; harness takes admin connection string`

### Task 9: Convert existing PG tests to SkippableFact + collection

**Files:**
- Modify: `tests/Inquiry.PostgreSql.Tests/NorthwindCrudIntegrationTests.cs`
- Modify: `tests/Inquiry.PostgreSql.Tests/NorthwindCoverageIntegrationTests.cs`
- Modify: `tests/Inquiry.PostgreSql.Tests/PostgreSqlProviderIntegrationTests.cs`

For each test class that uses `[PostgreSqlFact]` and `PostgreSqlTestHarness.CreateAsync(...)`:

- [ ] **Step 1: Add the collection + fixture injection** (per class)

Apply this transformation to each class (example for `NorthwindCrudIntegrationTests`):
```csharp
using Inquiry.PostgreSql.Tests.Fixtures;
using Xunit;

namespace Inquiry.PostgreSql.Tests;

[Collection(PostgreSqlCollection.Name)]
public sealed class NorthwindCrudIntegrationTests
{
    private readonly PostgreSqlContainerFixture _fixture;
    public NorthwindCrudIntegrationTests(PostgreSqlContainerFixture fixture) => _fixture = fixture;
    // ... tests below
}
```

- [ ] **Step 2: Convert each fact** — replace `[PostgreSqlFact]` with `[SkippableFact]`, add a guard, and pass the fixture's connection string. Example:
```csharp
    [SkippableFact]
    public async Task StringKeyEntitySupportsFullCrud()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await PostgreSqlTestHarness.CreateAsync(_fixture.AdminConnectionString, "crud_string");
        // ... unchanged assertions
    }
```
Add `using Xunit;` (for `Skip`) where missing. Repeat for every fact in all three files.

- [ ] **Step 3: Build**

Run: `dotnet build tests/Inquiry.PostgreSql.Tests/Inquiry.PostgreSql.Tests.csproj -f net8.0`
Expected: build succeeds.

- [ ] **Step 4: Run with Docker available**

Run: `dotnet test tests/Inquiry.PostgreSql.Tests/Inquiry.PostgreSql.Tests.csproj -f net8.0`
Expected: PASS — the PG analyzer's own SQL now runs green against real PostgreSQL. (If Docker is off, all skip — also acceptable for this step; re-run with Docker before committing.)

- [ ] **Step 5: Commit**

Message: `test(pg): run live CRUD against real PostgreSQL via Testcontainers`

### Task 10: PostgreSQL catalog introspector

**Files:**
- Create: `tests/Inquiry.PostgreSql.Tests/Fixtures/PostgreSqlSchemaIntrospector.cs`

- [ ] **Step 1: Implement using `information_schema` + `pg_*`**

```csharp
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Inquiry.IntegrationTesting;

namespace Inquiry.PostgreSql.Tests.Fixtures;

public sealed class PostgreSqlSchemaIntrospector : ISchemaIntrospector
{
    public async Task<SchemaSnapshot> ReadAsync(DbConnection conn, CancellationToken ct = default)
    {
        // columns + nullability
        var cols = new Dictionary<string, List<ColumnSnapshot>>();
        await Query(conn, ct,
            @"SELECT table_name, column_name, is_nullable
              FROM information_schema.columns
              WHERE table_schema = 'public' ORDER BY table_name, ordinal_position;",
            r =>
            {
                var t = r.GetString(0);
                if (!cols.TryGetValue(t, out var list)) cols[t] = list = new();
                list.Add(new ColumnSnapshot(r.GetString(1), r.GetString(2) == "YES"));
            });

        // primary keys
        var pks = new Dictionary<string, List<string>>();
        await Query(conn, ct,
            @"SELECT tc.table_name, kcu.column_name, kcu.ordinal_position
              FROM information_schema.table_constraints tc
              JOIN information_schema.key_column_usage kcu
                ON tc.constraint_name = kcu.constraint_name AND tc.table_schema = kcu.table_schema
              WHERE tc.table_schema='public' AND tc.constraint_type='PRIMARY KEY'
              ORDER BY tc.table_name, kcu.ordinal_position;",
            r => { var t = r.GetString(0); (pks.TryGetValue(t, out var l) ? l : pks[t] = new()).Add(r.GetString(1)); });

        // foreign keys (single-column FKs in Northwind)
        var fks = new Dictionary<string, List<ForeignKeySnapshot>>();
        await Query(conn, ct,
            @"SELECT tc.table_name, kcu.column_name, ccu.table_name AS ref_table, ccu.column_name AS ref_col
              FROM information_schema.table_constraints tc
              JOIN information_schema.key_column_usage kcu
                ON tc.constraint_name = kcu.constraint_name AND tc.table_schema = kcu.table_schema
              JOIN information_schema.constraint_column_usage ccu
                ON ccu.constraint_name = tc.constraint_name AND ccu.table_schema = tc.table_schema
              WHERE tc.table_schema='public' AND tc.constraint_type='FOREIGN KEY';",
            r =>
            {
                var t = r.GetString(0);
                (fks.TryGetValue(t, out var l) ? l : fks[t] = new())
                    .Add(new ForeignKeySnapshot(new[] { r.GetString(1) }, r.GetString(2), new[] { r.GetString(3) }));
            });

        // indexes (ordered columns) via pg_index
        var idx = new Dictionary<string, List<IndexSnapshot>>();
        await Query(conn, ct,
            @"SELECT t.relname AS table_name,
                     array_to_string(array_agg(a.attname ORDER BY k.ord), ',') AS cols
              FROM pg_index ix
              JOIN pg_class i ON i.oid = ix.indexrelid
              JOIN pg_class t ON t.oid = ix.indrelid
              JOIN pg_namespace n ON n.oid = t.relnamespace
              JOIN unnest(ix.indkey) WITH ORDINALITY AS k(attnum, ord) ON true
              JOIN pg_attribute a ON a.attrelid = t.oid AND a.attnum = k.attnum
              WHERE n.nspname='public'
              GROUP BY i.relname, t.relname;",
            r =>
            {
                var t = r.GetString(0);
                var c = r.GetString(1).Split(',');
                (idx.TryGetValue(t, out var l) ? l : idx[t] = new()).Add(new IndexSnapshot(c));
            });

        var tables = cols.Keys.Select(t => new TableSnapshot(
            t, cols[t],
            pks.TryGetValue(t, out var p) ? p : new List<string>(),
            fks.TryGetValue(t, out var f) ? f : new List<ForeignKeySnapshot>(),
            idx.TryGetValue(t, out var ii) ? ii : new List<IndexSnapshot>())).ToList();

        return new SchemaSnapshot(tables);
    }

    private static async Task Query(DbConnection conn, CancellationToken ct, string sql, System.Action<DbDataReader> onRow)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) onRow(r);
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build tests/Inquiry.PostgreSql.Tests/Inquiry.PostgreSql.Tests.csproj -f net8.0`
Expected: build succeeds.

- [ ] **Step 3: Commit**

Message: `test(pg): add PostgreSQL catalog introspector`

### Task 11: PG schema-fidelity + generated-DDL integration tests

**Files:**
- Create: `tests/Inquiry.PostgreSql.Tests/SchemaFidelityIntegrationTests.cs`
- Create: `tests/Inquiry.PostgreSql.Tests/GeneratedDdlIntegrationTests.cs`

- [ ] **Step 1: Write the hand-written-DDL fidelity test**

```csharp
using System.Threading.Tasks;
using Inquiry.IntegrationTesting;
using Inquiry.PostgreSql.Tests.Fixtures;
using Npgsql;
using Xunit;

namespace Inquiry.PostgreSql.Tests;

[Collection(PostgreSqlCollection.Name)]
public sealed class SchemaFidelityIntegrationTests
{
    private readonly PostgreSqlContainerFixture _fixture;
    public SchemaFidelityIntegrationTests(PostgreSqlContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task HandWrittenNorthwindMatchesContract()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await PostgreSqlTestHarness.CreateAsync(_fixture.AdminConnectionString, "fidelity");
        await using var conn = new NpgsqlConnection(harness.ConnectionString);
        await conn.OpenAsync();
        var actual = await new PostgreSqlSchemaIntrospector().ReadAsync(conn);
        SchemaFidelity.AssertMatches(ExpectedNorthwindSchema.Schema, actual);
    }
}
```

- [ ] **Step 2: Run — expect failure (PG DDL has no secondary indexes yet)**

Run: `dotnet test tests/Inquiry.PostgreSql.Tests/Inquiry.PostgreSql.Tests.csproj -f net8.0 --filter SchemaFidelityIntegrationTests`
Expected: FAIL (Docker on) with missing-index discrepancies.

- [ ] **Step 3: Add the classic indexes to `NorthwindSchema.PostgreSqlDdl`**

Append to the end of the `PostgreSqlDdl` string (quoted identifiers):
```sql
        CREATE INDEX IF NOT EXISTS "IX_Categories_CategoryName" ON "Categories" ("CategoryName");
        CREATE INDEX IF NOT EXISTS "IX_Suppliers_CompanyName" ON "Suppliers" ("CompanyName");
        CREATE INDEX IF NOT EXISTS "IX_Suppliers_PostalCode" ON "Suppliers" ("PostalCode");
        CREATE INDEX IF NOT EXISTS "IX_Customers_City" ON "Customers" ("City");
        CREATE INDEX IF NOT EXISTS "IX_Customers_CompanyName" ON "Customers" ("CompanyName");
        CREATE INDEX IF NOT EXISTS "IX_Customers_PostalCode" ON "Customers" ("PostalCode");
        CREATE INDEX IF NOT EXISTS "IX_Customers_Region" ON "Customers" ("Region");
        CREATE INDEX IF NOT EXISTS "IX_Employees_LastName" ON "Employees" ("LastName");
        CREATE INDEX IF NOT EXISTS "IX_Employees_PostalCode" ON "Employees" ("PostalCode");
        CREATE INDEX IF NOT EXISTS "IX_Products_CategoryID" ON "Products" ("CategoryID");
        CREATE INDEX IF NOT EXISTS "IX_Products_ProductName" ON "Products" ("ProductName");
        CREATE INDEX IF NOT EXISTS "IX_Products_SupplierID" ON "Products" ("SupplierID");
        CREATE INDEX IF NOT EXISTS "IX_Orders_CustomerID" ON "Orders" ("CustomerID");
        CREATE INDEX IF NOT EXISTS "IX_Orders_EmployeeID" ON "Orders" ("EmployeeID");
        CREATE INDEX IF NOT EXISTS "IX_Orders_OrderDate" ON "Orders" ("OrderDate");
        CREATE INDEX IF NOT EXISTS "IX_Orders_ShippedDate" ON "Orders" ("ShippedDate");
        CREATE INDEX IF NOT EXISTS "IX_Orders_ShipVia" ON "Orders" ("ShipVia");
        CREATE INDEX IF NOT EXISTS "IX_Orders_ShipPostalCode" ON "Orders" ("ShipPostalCode");
        CREATE INDEX IF NOT EXISTS "IX_Order_Details_OrderID" ON "Order Details" ("OrderID");
        CREATE INDEX IF NOT EXISTS "IX_Order_Details_ProductID" ON "Order Details" ("ProductID");
```

- [ ] **Step 4: Write the generated-DDL test**

```csharp
using System.Threading.Tasks;
using Inquiry.Generated;
using Inquiry.IntegrationTesting;
using Inquiry.Northwind.Models;
using Inquiry.Northwind.Stores;
using Inquiry.PostgreSql.Tests.Fixtures;
using Npgsql;
using Xunit;

namespace Inquiry.PostgreSql.Tests;

[Collection(PostgreSqlCollection.Name)]
public sealed class GeneratedDdlIntegrationTests
{
    private readonly PostgreSqlContainerFixture _fixture;
    public GeneratedDdlIntegrationTests(PostgreSqlContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task InquiryGeneratedSchemaStandsUpAndRoundTripsCrud()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await PostgreSqlTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString, InquiryGeneratedSchema.Ddl, "gends");

        var categories = harness.GetRequiredService<CategoryStore>();
        var inserted = await categories.InsertReturningAsync(new Category { CategoryName = "Beverages" });
        Assert.NotNull(inserted);
        Assert.True(inserted!.CategoryID > 0);

        await using var conn = new NpgsqlConnection(harness.ConnectionString);
        await conn.OpenAsync();
        var actual = await new PostgreSqlSchemaIntrospector().ReadAsync(conn);
        SchemaFidelity.AssertMatches(ExpectedNorthwindSchema.Schema, actual);
    }
}
```

- [ ] **Step 5: Add `CreateFromDdlAsync` to the harness**

In `PostgreSqlTestHarness`, add an overload that runs caller-supplied DDL instead of `NorthwindSchema.PostgreSqlDdl` (refactor `CreateAsync` to delegate to it):
```csharp
    public static Task<PostgreSqlTestHarness> CreateAsync(string adminConnectionString, string? namePrefix = null)
        => CreateFromDdlAsync(adminConnectionString, NorthwindSchema.PostgreSqlDdl, namePrefix);

    public static async Task<PostgreSqlTestHarness> CreateFromDdlAsync(string adminConnectionString, string ddl, string? namePrefix = null)
    {
        // ... same body as CreateAsync, but execute `ddl` instead of NorthwindSchema.PostgreSqlDdl.
    }
```

- [ ] **Step 6: Run both new tests (Docker on)**

Run: `dotnet test tests/Inquiry.PostgreSql.Tests/Inquiry.PostgreSql.Tests.csproj -f net8.0 --filter "SchemaFidelityIntegrationTests|GeneratedDdlIntegrationTests"`
Expected: PASS. (If the generated DDL is missing an index/FK the contract requires, the failure message names it — fix by adding the corresponding entity annotation in Task 6's set, or accept the gap as a real W7 limitation and note it.)

- [ ] **Step 7: Run the full PG suite**

Run: `dotnet test tests/Inquiry.PostgreSql.Tests/Inquiry.PostgreSql.Tests.csproj -f net8.0`
Expected: PASS.

- [ ] **Step 8: Commit**

Message: `test(pg): add schema-fidelity + generated-DDL verification; index PG DDL`

---

## Phase 4 — MySQL

### Task 12: MySQL per-dialect compilation + fixture + harness

**Files:**
- Modify: `tests/Inquiry.MySql.Tests/Inquiry.MySql.Tests.csproj`
- Create: `tests/Inquiry.MySql.Tests/AssemblyDialect.cs`
- Create: `tests/Inquiry.MySql.Tests/Fixtures/MySqlContainerFixture.cs`
- Create: `tests/Inquiry.MySql.Tests/Fixtures/MySqlCollection.cs`
- Modify: `tests/Inquiry.MySql.Tests/Fixtures/MySqlTestHarness.cs`
- Delete: `tests/Inquiry.MySql.Tests/Fixtures/MySqlFactAttribute.cs`

- [ ] **Step 1: csproj** — remove the `Inquiry.Northwind` ProjectReference; add:
```xml
    <PackageReference Include="Testcontainers.MySql" />
    <PackageReference Include="Xunit.SkippableFact" />
    <ProjectReference Include="..\Inquiry.IntegrationTesting\Inquiry.IntegrationTesting.csproj" />

    <Compile Include="..\..\samples\Inquiry.Northwind\Models\**\*.cs" LinkBase="Northwind\Models" />
    <Compile Include="..\..\samples\Inquiry.Northwind\Stores\**\*.cs" LinkBase="Northwind\Stores" />
    <Compile Include="..\..\samples\Inquiry.Northwind\NorthwindSchema.cs" LinkBase="Northwind" />
```

- [ ] **Step 2: AssemblyDialect.cs**
```csharp
using Inquiry;

[assembly: InquiryDialect("MySql")]
```

- [ ] **Step 3: Container fixture** (`MySqlContainerFixture.cs`)
```csharp
using System;
using System.Threading.Tasks;
using Testcontainers.MySql;
using Xunit;

namespace Inquiry.MySql.Tests.Fixtures;

public sealed class MySqlContainerFixture : IAsyncLifetime
{
    private MySqlContainer? _container;
    public bool IsAvailable { get; private set; }
    public string? SkipReason { get; private set; }
    public string AdminConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        try
        {
            _container = new MySqlBuilder().WithImage("mysql:8.4").Build();
            await _container.StartAsync();
            AdminConnectionString = _container.GetConnectionString();
            IsAvailable = true;
        }
        catch (Exception ex)
        {
            IsAvailable = false;
            SkipReason = "MySQL container unavailable (is Docker running?): " + ex.Message;
        }
    }

    public async Task DisposeAsync()
    {
        if (_container is not null) await _container.DisposeAsync();
    }
}
```

- [ ] **Step 4: Collection** (`MySqlCollection.cs`)
```csharp
using Xunit;

namespace Inquiry.MySql.Tests.Fixtures;

[CollectionDefinition(Name)]
public sealed class MySqlCollection : ICollectionFixture<MySqlContainerFixture>
{
    public const string Name = "MySql";
}
```

- [ ] **Step 5: Harness** — refactor to `CreateFromDdlAsync(adminConnectionString, ddl, namePrefix)` + `CreateAsync` delegating with `NorthwindSchema.MySqlDdl`; drop the env-var constant/throw. Keep `AllowUserVariables = true`. **Remove** the `SET SESSION sql_mode ... ANSI_QUOTES` block — under the MySql dialect the generated SQL uses backticks, so ANSI_QUOTES is no longer needed.

- [ ] **Step 6: Delete** `MySqlFactAttribute.cs`.

- [ ] **Step 7: Build**

Run: `dotnet build tests/Inquiry.MySql.Tests/Inquiry.MySql.Tests.csproj -f net8.0`
Expected: build succeeds (MySql analyzer bakes backtick SQL).

- [ ] **Step 8: Commit**

Message: `test(mysql): per-dialect compile + Testcontainers fixture`

### Task 13: Convert MySQL tests, add introspector + fidelity/generated-DDL, index DDL

**Files:**
- Modify: `tests/Inquiry.MySql.Tests/NorthwindCrudIntegrationTests.cs`, `MySqlProviderIntegrationTests.cs`
- Create: `tests/Inquiry.MySql.Tests/Fixtures/MySqlSchemaIntrospector.cs`
- Create: `tests/Inquiry.MySql.Tests/SchemaFidelityIntegrationTests.cs`, `GeneratedDdlIntegrationTests.cs`
- Modify: `samples/Inquiry.Northwind/NorthwindSchema.cs` (`MySqlDdl`)

- [ ] **Step 1: Convert facts** — same transformation as Task 9: `[Collection(MySqlCollection.Name)]` on each class, inject `MySqlContainerFixture`, `[MySqlFact]`→`[SkippableFact]` + `Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason)`, pass `_fixture.AdminConnectionString` into `CreateAsync`.

- [ ] **Step 2: Implement `MySqlSchemaIntrospector`** using `information_schema` (the active schema is the throwaway DB — filter by `DATABASE()`):
```csharp
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Inquiry.IntegrationTesting;

namespace Inquiry.MySql.Tests.Fixtures;

public sealed class MySqlSchemaIntrospector : ISchemaIntrospector
{
    public async Task<SchemaSnapshot> ReadAsync(DbConnection conn, CancellationToken ct = default)
    {
        var cols = new Dictionary<string, List<ColumnSnapshot>>();
        await Query(conn, ct,
            @"SELECT TABLE_NAME, COLUMN_NAME, IS_NULLABLE
              FROM information_schema.COLUMNS
              WHERE TABLE_SCHEMA = DATABASE() ORDER BY TABLE_NAME, ORDINAL_POSITION;",
            r => { var t = r.GetString(0); (cols.TryGetValue(t, out var l) ? l : cols[t] = new())
                       .Add(new ColumnSnapshot(r.GetString(1), r.GetString(2) == "YES")); });

        var pks = new Dictionary<string, List<string>>();
        await Query(conn, ct,
            @"SELECT TABLE_NAME, COLUMN_NAME
              FROM information_schema.KEY_COLUMN_USAGE
              WHERE TABLE_SCHEMA = DATABASE() AND CONSTRAINT_NAME = 'PRIMARY'
              ORDER BY TABLE_NAME, ORDINAL_POSITION;",
            r => { var t = r.GetString(0); (pks.TryGetValue(t, out var l) ? l : pks[t] = new()).Add(r.GetString(1)); });

        var fks = new Dictionary<string, List<ForeignKeySnapshot>>();
        await Query(conn, ct,
            @"SELECT TABLE_NAME, COLUMN_NAME, REFERENCED_TABLE_NAME, REFERENCED_COLUMN_NAME
              FROM information_schema.KEY_COLUMN_USAGE
              WHERE TABLE_SCHEMA = DATABASE() AND REFERENCED_TABLE_NAME IS NOT NULL;",
            r => { var t = r.GetString(0); (fks.TryGetValue(t, out var l) ? l : fks[t] = new())
                       .Add(new ForeignKeySnapshot(new[] { r.GetString(1) }, r.GetString(2), new[] { r.GetString(3) })); });

        // indexes: group STATISTICS by (table, index) ordered by SEQ_IN_INDEX
        var idxAcc = new Dictionary<(string T, string I), List<(int Seq, string Col)>>();
        await Query(conn, ct,
            @"SELECT TABLE_NAME, INDEX_NAME, SEQ_IN_INDEX, COLUMN_NAME
              FROM information_schema.STATISTICS
              WHERE TABLE_SCHEMA = DATABASE();",
            r => { var key = (r.GetString(0), r.GetString(1));
                   (idxAcc.TryGetValue(key, out var l) ? l : idxAcc[key] = new())
                       .Add((r.GetInt32(2), r.GetString(3))); });
        var idx = new Dictionary<string, List<IndexSnapshot>>();
        foreach (var kv in idxAcc)
        {
            var ordered = kv.Value.OrderBy(x => x.Seq).Select(x => x.Col).ToArray();
            (idx.TryGetValue(kv.Key.T, out var l) ? l : idx[kv.Key.T] = new()).Add(new IndexSnapshot(ordered));
        }

        var tables = cols.Keys.Select(t => new TableSnapshot(
            t, cols[t],
            pks.TryGetValue(t, out var p) ? p : new List<string>(),
            fks.TryGetValue(t, out var f) ? f : new List<ForeignKeySnapshot>(),
            idx.TryGetValue(t, out var ii) ? ii : new List<IndexSnapshot>())).ToList();
        return new SchemaSnapshot(tables);
    }

    private static async Task Query(DbConnection conn, CancellationToken ct, string sql, System.Action<DbDataReader> onRow)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) onRow(r);
    }
}
```

- [ ] **Step 3: Add `SchemaFidelityIntegrationTests` + `GeneratedDdlIntegrationTests`** mirroring Task 11's PG versions, but with `MySqlConnection`, `MySqlContainerFixture`, `MySqlCollection.Name`, `MySqlSchemaIntrospector`, and `PostgreSqlTestHarness`→`MySqlTestHarness`. (Use `MySqlConnector.MySqlConnection`.)

- [ ] **Step 4: Run fidelity (Docker on) — expect missing-index failure, then add indexes to `MySqlDdl`**

Append to the end of `MySqlDdl` (backticks):
```sql
        CREATE INDEX IX_Categories_CategoryName ON `Categories` (`CategoryName`);
        CREATE INDEX IX_Suppliers_CompanyName ON `Suppliers` (`CompanyName`);
        CREATE INDEX IX_Suppliers_PostalCode ON `Suppliers` (`PostalCode`(20));
        CREATE INDEX IX_Customers_City ON `Customers` (`City`(50));
        CREATE INDEX IX_Customers_CompanyName ON `Customers` (`CompanyName`);
        CREATE INDEX IX_Customers_PostalCode ON `Customers` (`PostalCode`(20));
        CREATE INDEX IX_Customers_Region ON `Customers` (`Region`(50));
        CREATE INDEX IX_Employees_LastName ON `Employees` (`LastName`);
        CREATE INDEX IX_Employees_PostalCode ON `Employees` (`PostalCode`(20));
        CREATE INDEX IX_Products_CategoryID ON `Products` (`CategoryID`);
        CREATE INDEX IX_Products_ProductName ON `Products` (`ProductName`);
        CREATE INDEX IX_Products_SupplierID ON `Products` (`SupplierID`);
        CREATE INDEX IX_Orders_CustomerID ON `Orders` (`CustomerID`);
        CREATE INDEX IX_Orders_EmployeeID ON `Orders` (`EmployeeID`);
        CREATE INDEX IX_Orders_OrderDate ON `Orders` (`OrderDate`);
        CREATE INDEX IX_Orders_ShippedDate ON `Orders` (`ShippedDate`);
        CREATE INDEX IX_Orders_ShipVia ON `Orders` (`ShipVia`);
        CREATE INDEX IX_Orders_ShipPostalCode ON `Orders` (`ShipPostalCode`(20));
        CREATE INDEX IX_Order_Details_OrderID ON `Order Details` (`OrderID`);
        CREATE INDEX IX_Order_Details_ProductID ON `Order Details` (`ProductID`);
```
Note: MySQL cannot index full `LONGTEXT`; the `(n)` prefix lengths above index a prefix of those columns. City/Region/PostalCode are `LONGTEXT` in `MySqlDdl`, hence the prefix. (CompanyName/ProductName/LastName/CategoryName are `VARCHAR(40)`, indexable in full.) MySQL also auto-creates indexes for FK columns; the introspector will surface those as additional indexes — allowed.

- [ ] **Step 5: Run the full MySQL suite (Docker on)**

Run: `dotnet test tests/Inquiry.MySql.Tests/Inquiry.MySql.Tests.csproj -f net8.0`
Expected: PASS.

- [ ] **Step 6: Commit**

Message: `test(mysql): live CRUD + fidelity + generated-DDL; index MySQL DDL`

---

## Phase 5 — SQL Server

### Task 14: SQL Server per-dialect compilation + fixture + harness

**Files:** mirror Task 12 for `tests/Inquiry.SqlServer.Tests/`.

- [ ] **Step 1: csproj** — remove `Inquiry.Northwind` ProjectReference; add:
```xml
    <PackageReference Include="Testcontainers.MsSql" />
    <PackageReference Include="Xunit.SkippableFact" />
    <ProjectReference Include="..\Inquiry.IntegrationTesting\Inquiry.IntegrationTesting.csproj" />

    <Compile Include="..\..\samples\Inquiry.Northwind\Models\**\*.cs" LinkBase="Northwind\Models" />
    <Compile Include="..\..\samples\Inquiry.Northwind\Stores\**\*.cs" LinkBase="Northwind\Stores" />
    <Compile Include="..\..\samples\Inquiry.Northwind\NorthwindSchema.cs" LinkBase="Northwind" />
```

- [ ] **Step 2: AssemblyDialect.cs** → `[assembly: InquiryDialect("SqlServer")]`.

- [ ] **Step 3: `SqlServerContainerFixture.cs`**
```csharp
using System;
using System.Threading.Tasks;
using Testcontainers.MsSql;
using Xunit;

namespace Inquiry.SqlServer.Tests.Fixtures;

public sealed class SqlServerContainerFixture : IAsyncLifetime
{
    private MsSqlContainer? _container;
    public bool IsAvailable { get; private set; }
    public string? SkipReason { get; private set; }
    public string AdminConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        try
        {
            _container = new MsSqlBuilder().Build(); // mcr.microsoft.com/mssql/server:2022-latest
            await _container.StartAsync();
            AdminConnectionString = _container.GetConnectionString();
            IsAvailable = true;
        }
        catch (Exception ex)
        {
            IsAvailable = false;
            SkipReason = "SQL Server container unavailable (is Docker running?): " + ex.Message;
        }
    }

    public async Task DisposeAsync()
    {
        if (_container is not null) await _container.DisposeAsync();
    }
}
```

- [ ] **Step 4: `SqlServerCollection.cs`** — same shape as PG/MySQL with `Name = "SqlServer"`.

- [ ] **Step 5: Harness** — refactor to `CreateFromDdlAsync` + `CreateAsync` delegating with `NorthwindSchema.SqlServerDdl`; drop env-var constant/throw.

- [ ] **Step 6: Delete** `SqlServerFactAttribute.cs`.

- [ ] **Step 7: Build**

Run: `dotnet build tests/Inquiry.SqlServer.Tests/Inquiry.SqlServer.Tests.csproj -f net8.0`
Expected: succeeds.

- [ ] **Step 8: Commit**

Message: `test(sqlserver): per-dialect compile + Testcontainers fixture`

### Task 15: Convert SQL Server tests, add introspector + fidelity/generated-DDL, index DDL

**Files:**
- Modify: existing SQL Server integration test files (convert `[SqlServerFact]`→`[SkippableFact]` + collection + fixture, per Task 9 recipe).
- Create: `tests/Inquiry.SqlServer.Tests/Fixtures/SqlServerSchemaIntrospector.cs`
- Create: `tests/Inquiry.SqlServer.Tests/SchemaFidelityIntegrationTests.cs`, `GeneratedDdlIntegrationTests.cs`
- Modify: `samples/Inquiry.Northwind/NorthwindSchema.cs` (`SqlServerDdl`)

- [ ] **Step 1: Implement `SqlServerSchemaIntrospector`** using `sys.*`:
```csharp
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Inquiry.IntegrationTesting;

namespace Inquiry.SqlServer.Tests.Fixtures;

public sealed class SqlServerSchemaIntrospector : ISchemaIntrospector
{
    public async Task<SchemaSnapshot> ReadAsync(DbConnection conn, CancellationToken ct = default)
    {
        var cols = new Dictionary<string, List<ColumnSnapshot>>();
        await Query(conn, ct,
            @"SELECT t.name, c.name, c.is_nullable
              FROM sys.tables t JOIN sys.columns c ON c.object_id = t.object_id
              ORDER BY t.name, c.column_id;",
            r => { var t = r.GetString(0); (cols.TryGetValue(t, out var l) ? l : cols[t] = new())
                       .Add(new ColumnSnapshot(r.GetString(1), r.GetBoolean(2))); });

        var pks = new Dictionary<string, List<string>>();
        await Query(conn, ct,
            @"SELECT t.name, c.name, ic.key_ordinal
              FROM sys.key_constraints kc
              JOIN sys.tables t ON t.object_id = kc.parent_object_id
              JOIN sys.index_columns ic ON ic.object_id = kc.parent_object_id AND ic.index_id = kc.unique_index_id
              JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
              WHERE kc.type = 'PK' ORDER BY t.name, ic.key_ordinal;",
            r => { var t = r.GetString(0); (pks.TryGetValue(t, out var l) ? l : pks[t] = new()).Add(r.GetString(1)); });

        var fks = new Dictionary<string, List<ForeignKeySnapshot>>();
        await Query(conn, ct,
            @"SELECT pt.name, pc.name, rt.name, rc.name
              FROM sys.foreign_keys fk
              JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
              JOIN sys.tables pt ON pt.object_id = fk.parent_object_id
              JOIN sys.columns pc ON pc.object_id = fkc.parent_object_id AND pc.column_id = fkc.parent_column_id
              JOIN sys.tables rt ON rt.object_id = fk.referenced_object_id
              JOIN sys.columns rc ON rc.object_id = fkc.referenced_object_id AND rc.column_id = fkc.referenced_column_id;",
            r => { var t = r.GetString(0); (fks.TryGetValue(t, out var l) ? l : fks[t] = new())
                       .Add(new ForeignKeySnapshot(new[] { r.GetString(1) }, r.GetString(2), new[] { r.GetString(3) })); });

        var idxAcc = new Dictionary<(string T, int I), List<(int Key, string Col)>>();
        await Query(conn, ct,
            @"SELECT t.name, i.index_id, ic.key_ordinal, c.name
              FROM sys.indexes i
              JOIN sys.tables t ON t.object_id = i.object_id
              JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
              JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
              WHERE i.type > 0 AND ic.is_included_column = 0;",
            r => { var key = (r.GetString(0), r.GetInt32(1));
                   (idxAcc.TryGetValue(key, out var l) ? l : idxAcc[key] = new()).Add((r.GetByte(2), r.GetString(3))); });
        var idx = new Dictionary<string, List<IndexSnapshot>>();
        foreach (var kv in idxAcc)
        {
            var ordered = kv.Value.OrderBy(x => x.Key).Select(x => x.Col).ToArray();
            (idx.TryGetValue(kv.Key.T, out var l) ? l : idx[kv.Key.T] = new()).Add(new IndexSnapshot(ordered));
        }

        var tables = cols.Keys.Select(t => new TableSnapshot(
            t, cols[t],
            pks.TryGetValue(t, out var p) ? p : new List<string>(),
            fks.TryGetValue(t, out var f) ? f : new List<ForeignKeySnapshot>(),
            idx.TryGetValue(t, out var ii) ? ii : new List<IndexSnapshot>())).ToList();
        return new SchemaSnapshot(tables);
    }

    private static async Task Query(DbConnection conn, CancellationToken ct, string sql, System.Action<DbDataReader> onRow)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) onRow(r);
    }
}
```

- [ ] **Step 2: Add `SchemaFidelityIntegrationTests` + `GeneratedDdlIntegrationTests`** mirroring Task 11 with `Microsoft.Data.SqlClient.SqlConnection`, `SqlServerContainerFixture`, `SqlServerCollection.Name`, `SqlServerSchemaIntrospector`, `SqlServerTestHarness`.

- [ ] **Step 3: Run fidelity (Docker on) — expect missing-index failure, then add indexes to `SqlServerDdl`**

Append to the end of `SqlServerDdl` (each guarded for idempotency):
```sql
        CREATE INDEX IX_Categories_CategoryName ON Categories (CategoryName);
        CREATE INDEX IX_Suppliers_CompanyName ON Suppliers (CompanyName);
        CREATE INDEX IX_Customers_CompanyName ON Customers (CompanyName);
        CREATE INDEX IX_Employees_LastName ON Employees (LastName);
        CREATE INDEX IX_Products_CategoryID ON Products (CategoryID);
        CREATE INDEX IX_Products_ProductName ON Products (ProductName);
        CREATE INDEX IX_Products_SupplierID ON Products (SupplierID);
        CREATE INDEX IX_Orders_CustomerID ON Orders (CustomerID);
        CREATE INDEX IX_Orders_EmployeeID ON Orders (EmployeeID);
        CREATE INDEX IX_Orders_OrderDate ON Orders (OrderDate);
        CREATE INDEX IX_Orders_ShippedDate ON Orders (ShippedDate);
        CREATE INDEX IX_Orders_ShipVia ON Orders (ShipVia);
        CREATE INDEX [IX_Order_Details_OrderID] ON [Order Details] (OrderID);
        CREATE INDEX [IX_Order_Details_ProductID] ON [Order Details] (ProductID);
```
Note: `Customers.City/PostalCode/Region`, `Suppliers.PostalCode`, `Employees.PostalCode`, `Orders.ShipPostalCode` are `NVARCHAR(MAX)` in `SqlServerDdl`, which **cannot** be indexed. To honor the contract on SQL Server, change those specific columns from `NVARCHAR(MAX)` to a bounded type (`NVARCHAR(20)` for postal codes, `NVARCHAR(60)` for City/Region) in the `CREATE TABLE` statements, then add their indexes:
```sql
        CREATE INDEX IX_Suppliers_PostalCode ON Suppliers (PostalCode);
        CREATE INDEX IX_Customers_City ON Customers (City);
        CREATE INDEX IX_Customers_PostalCode ON Customers (PostalCode);
        CREATE INDEX IX_Customers_Region ON Customers (Region);
        CREATE INDEX IX_Employees_PostalCode ON Employees (PostalCode);
        CREATE INDEX IX_Orders_ShipPostalCode ON Orders (ShipPostalCode);
```
Wrap each `CREATE INDEX` in an existence guard, e.g.:
`IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_Categories_CategoryName') CREATE INDEX IX_Categories_CategoryName ON Categories (CategoryName);`

- [ ] **Step 4: Run full SQL Server suite (Docker on)**

Run: `dotnet test tests/Inquiry.SqlServer.Tests/Inquiry.SqlServer.Tests.csproj -f net8.0`
Expected: PASS.

- [ ] **Step 5: Commit**

Message: `test(sqlserver): live CRUD + fidelity + generated-DDL; index + bound SQL Server DDL`

---

## Phase 6 — Oracle

### Task 16: Oracle per-dialect compilation + fixture + harness

**Files:** mirror Task 12 for `tests/Inquiry.Oracle.Tests/`.

- [ ] **Step 1: csproj** — remove `Inquiry.Northwind` ProjectReference; add `Testcontainers.Oracle`, `Xunit.SkippableFact`, the `Inquiry.IntegrationTesting` reference, and the three `<Compile Include>` lines (as in Task 12).

- [ ] **Step 2: AssemblyDialect.cs** → `[assembly: InquiryDialect("Oracle")]`.

- [ ] **Step 3: `OracleContainerFixture.cs`**
```csharp
using System;
using System.Threading.Tasks;
using Testcontainers.Oracle;
using Xunit;

namespace Inquiry.Oracle.Tests.Fixtures;

public sealed class OracleContainerFixture : IAsyncLifetime
{
    private OracleContainer? _container;
    public bool IsAvailable { get; private set; }
    public string? SkipReason { get; private set; }
    public string AdminConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        try
        {
            _container = new OracleBuilder().WithImage("gvenzl/oracle-free:23-slim-faststart").Build();
            await _container.StartAsync();
            AdminConnectionString = _container.GetConnectionString();
            IsAvailable = true;
        }
        catch (Exception ex)
        {
            IsAvailable = false;
            SkipReason = "Oracle container unavailable (is Docker running?): " + ex.Message;
        }
    }

    public async Task DisposeAsync()
    {
        if (_container is not null) await _container.DisposeAsync();
    }
}
```

- [ ] **Step 4: `OracleCollection.cs`** — `Name = "Oracle"`.

- [ ] **Step 5: Harness** — refactor to take the admin connection string; `CreateFromDdlAsync` runs the supplied DDL via the existing `SplitStatements` helper; `CreateAsync` delegates with `NorthwindSchema.OracleDdl`. Drop the env-var constant/throw. Keep the throwaway-schema (CREATE USER) logic — the admin connection string now comes from the fixture (the gvenzl image's `SYSTEM` user can create users).

- [ ] **Step 6: Delete** `OracleFactAttribute.cs`.

- [ ] **Step 7: Build**

Run: `dotnet build tests/Inquiry.Oracle.Tests/Inquiry.Oracle.Tests.csproj -f net8.0`
Expected: succeeds (Oracle analyzer bakes `:`-bind, uppercase-identifier SQL).

- [ ] **Step 8: Commit**

Message: `test(oracle): per-dialect compile + Testcontainers fixture`

### Task 17: Convert Oracle tests, add introspector + fidelity/generated-DDL, index DDL

**Files:**
- Modify: `tests/Inquiry.Oracle.Tests/NorthwindCrudIntegrationTests.cs` (+ any other Oracle integration test files) — convert per Task 9 recipe.
- Create: `tests/Inquiry.Oracle.Tests/Fixtures/OracleSchemaIntrospector.cs`
- Create: `tests/Inquiry.Oracle.Tests/SchemaFidelityIntegrationTests.cs`, `GeneratedDdlIntegrationTests.cs`
- Modify: `samples/Inquiry.Northwind/NorthwindSchema.cs` (`OracleDdl`)

- [ ] **Step 1: Implement `OracleSchemaIntrospector`** using `user_*` views. The throwaway schema user owns the objects, so `USER_*` views are scoped correctly:
```csharp
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Inquiry.IntegrationTesting;

namespace Inquiry.Oracle.Tests.Fixtures;

public sealed class OracleSchemaIntrospector : ISchemaIntrospector
{
    public async Task<SchemaSnapshot> ReadAsync(DbConnection conn, CancellationToken ct = default)
    {
        var cols = new Dictionary<string, List<ColumnSnapshot>>();
        await Query(conn, ct,
            @"SELECT table_name, column_name, nullable
              FROM user_tab_columns ORDER BY table_name, column_id",
            r => { var t = r.GetString(0); (cols.TryGetValue(t, out var l) ? l : cols[t] = new())
                       .Add(new ColumnSnapshot(r.GetString(1), r.GetString(2) == "Y")); });

        var pks = new Dictionary<string, List<string>>();
        await Query(conn, ct,
            @"SELECT cc.table_name, cc.column_name, cc.position
              FROM user_constraints c JOIN user_cons_columns cc ON c.constraint_name = cc.constraint_name
              WHERE c.constraint_type = 'P' ORDER BY cc.table_name, cc.position",
            r => { var t = r.GetString(0); (pks.TryGetValue(t, out var l) ? l : pks[t] = new()).Add(r.GetString(1)); });

        var fks = new Dictionary<string, List<ForeignKeySnapshot>>();
        await Query(conn, ct,
            @"SELECT cc.table_name, cc.column_name, rcc.table_name, rcc.column_name
              FROM user_constraints c
              JOIN user_cons_columns cc ON c.constraint_name = cc.constraint_name
              JOIN user_cons_columns rcc ON c.r_constraint_name = rcc.constraint_name AND cc.position = rcc.position
              WHERE c.constraint_type = 'R'",
            r => { var t = r.GetString(0); (fks.TryGetValue(t, out var l) ? l : fks[t] = new())
                       .Add(new ForeignKeySnapshot(new[] { r.GetString(1) }, r.GetString(2), new[] { r.GetString(3) })); });

        var idxAcc = new Dictionary<(string T, string I), List<(int Pos, string Col)>>();
        await Query(conn, ct,
            @"SELECT table_name, index_name, column_position, column_name
              FROM user_ind_columns",
            r => { var key = (r.GetString(0), r.GetString(1));
                   (idxAcc.TryGetValue(key, out var l) ? l : idxAcc[key] = new())
                       .Add((System.Convert.ToInt32(r.GetValue(2)), r.GetString(3))); });
        var idx = new Dictionary<string, List<IndexSnapshot>>();
        foreach (var kv in idxAcc)
        {
            var ordered = kv.Value.OrderBy(x => x.Pos).Select(x => x.Col).ToArray();
            (idx.TryGetValue(kv.Key.T, out var l) ? l : idx[kv.Key.T] = new()).Add(new IndexSnapshot(ordered));
        }

        var tables = cols.Keys.Select(t => new TableSnapshot(
            t, cols[t],
            pks.TryGetValue(t, out var p) ? p : new List<string>(),
            fks.TryGetValue(t, out var f) ? f : new List<ForeignKeySnapshot>(),
            idx.TryGetValue(t, out var ii) ? ii : new List<IndexSnapshot>())).ToList();
        return new SchemaSnapshot(tables);
    }

    private static async Task Query(DbConnection conn, CancellationToken ct, string sql, System.Action<DbDataReader> onRow)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) onRow(r);
    }
}
```
(Oracle folds unquoted names to uppercase, so introspected names are `CATEGORIES` etc.; `SchemaFidelity`'s case-insensitive matching handles this. `"Order Details"` is quoted and comes back as `Order Details`.)

- [ ] **Step 2: Add `SchemaFidelityIntegrationTests` + `GeneratedDdlIntegrationTests`** mirroring Task 11 with `Oracle.ManagedDataAccess.Client.OracleConnection`, `OracleContainerFixture`, `OracleCollection.Name`, `OracleSchemaIntrospector`, `OracleTestHarness`.

- [ ] **Step 3: Run fidelity (Docker on) — add indexes to `OracleDdl`**

Append `CREATE INDEX` statements (Oracle has no `IF NOT EXISTS`; the harness creates a fresh schema per run, and the index names are unique within the schema):
```sql
        CREATE INDEX IX_Categories_CategoryName ON Categories (CategoryName);
        CREATE INDEX IX_Suppliers_CompanyName ON Suppliers (CompanyName);
        CREATE INDEX IX_Suppliers_PostalCode ON Suppliers (PostalCode);
        CREATE INDEX IX_Customers_City ON Customers (City);
        CREATE INDEX IX_Customers_CompanyName ON Customers (CompanyName);
        CREATE INDEX IX_Customers_PostalCode ON Customers (PostalCode);
        CREATE INDEX IX_Customers_Region ON Customers (Region);
        CREATE INDEX IX_Employees_LastName ON Employees (LastName);
        CREATE INDEX IX_Employees_PostalCode ON Employees (PostalCode);
        CREATE INDEX IX_Products_CategoryID ON Products (CategoryID);
        CREATE INDEX IX_Products_ProductName ON Products (ProductName);
        CREATE INDEX IX_Products_SupplierID ON Products (SupplierID);
        CREATE INDEX IX_Orders_CustomerID ON Orders (CustomerID);
        CREATE INDEX IX_Orders_EmployeeID ON Orders (EmployeeID);
        CREATE INDEX IX_Orders_OrderDate ON Orders (OrderDate);
        CREATE INDEX IX_Orders_ShippedDate ON Orders (ShippedDate);
        CREATE INDEX IX_Orders_ShipVia ON Orders (ShipVia);
        CREATE INDEX IX_Orders_ShipPostalCode ON Orders (ShipPostalCode);
        CREATE INDEX IX_OrderDetails_OrderID ON "Order Details" (OrderID);
        CREATE INDEX IX_OrderDetails_ProductID ON "Order Details" (ProductID);
```
(`SplitStatements` already splits on `;`. Oracle identifier length limit is 128 on 23c; the `IX_…` names are within it.)

- [ ] **Step 4: Run full Oracle suite (Docker on; first run pulls a multi-GB image)**

Run: `dotnet test tests/Inquiry.Oracle.Tests/Inquiry.Oracle.Tests.csproj -f net8.0`
Expected: PASS. (This is also where the `BindByName` / `:`-bind behavior is empirically confirmed; if a CRUD test fails on parameter binding, fix the provider's `BindByName`/parameter-name handling and note it.)

- [ ] **Step 5: Commit**

Message: `test(oracle): live CRUD + fidelity + generated-DDL; index Oracle DDL`

---

## Phase 7 — CI workflows

### Task 18: PR CI workflow

**Files:**
- Create: `.github/workflows/ci.yml`

- [ ] **Step 1: Write the workflow**

```yaml
name: CI
on:
  pull_request:
    branches: [ main ]
  push:
    branches: [ main ]

jobs:
  build-and-unit:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: |
            6.0.x
            7.0.x
            8.0.x
            9.0.x
            10.0.x
      - run: dotnet restore
      - run: dotnet build --no-restore -c Release
      - name: Generator, unit & SQLite tests
        run: |
          dotnet test tests/Inquiry.Generators.Tests/Inquiry.Generators.Tests.csproj -c Release --no-build
          dotnet test tests/Inquiry.Tests/Inquiry.Tests.csproj -c Release --no-build
          dotnet test tests/Inquiry.Sqlite.Tests/Inquiry.Sqlite.Tests.csproj -c Release --no-build

  integration:
    runs-on: ubuntu-latest
    strategy:
      fail-fast: false
      matrix:
        provider: [PostgreSql, MySql, SqlServer]
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 8.0.x
      - name: Integration tests (${{ matrix.provider }})
        run: dotnet test tests/Inquiry.${{ matrix.provider }}.Tests/Inquiry.${{ matrix.provider }}.Tests.csproj -c Release -f net8.0
```

- [ ] **Step 2: Validate YAML locally** (optional)

Run: `python -c "import yaml,sys; yaml.safe_load(open('.github/workflows/ci.yml'))"`
Expected: no error.

- [ ] **Step 3: Commit**

Message: `ci: add PR workflow (build/unit + PG/MySQL/SQL Server integration matrix)`

### Task 19: Nightly Oracle workflow

**Files:**
- Create: `.github/workflows/nightly.yml`

- [ ] **Step 1: Write the workflow**

```yaml
name: Nightly
on:
  schedule:
    - cron: '0 6 * * *'
  workflow_dispatch:

jobs:
  oracle:
    runs-on: ubuntu-latest
    timeout-minutes: 40
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 8.0.x
      - name: Oracle integration tests
        run: dotnet test tests/Inquiry.Oracle.Tests/Inquiry.Oracle.Tests.csproj -c Release -f net8.0
```

- [ ] **Step 2: Commit**

Message: `ci: add nightly Oracle integration workflow`

---

## Phase 8 — Live-environment benchmarking

### Task 20: Dialect-parameterize the benchmark project

**Files:**
- Modify: `benchmarks/Inquiry.Benchmarks/Inquiry.Benchmarks.csproj`
- Create: `benchmarks/Inquiry.Benchmarks/AssemblyDialect.Sqlite.cs`
- Create: `benchmarks/Inquiry.Benchmarks/AssemblyDialect.PostgreSql.cs`

- [ ] **Step 1: Add the property + conditional refs** to the csproj

Add near the top of the first `<PropertyGroup>`:
```xml
    <InquiryBenchProvider Condition="'$(InquiryBenchProvider)' == ''">Sqlite</InquiryBenchProvider>
```
Add a conditional block (the default SQLite path keeps the current references; the PostgreSql path swaps in the PG package + Testcontainers + linked Northwind source):
```xml
  <Choose>
    <When Condition="'$(InquiryBenchProvider)' == 'PostgreSql'">
      <ItemGroup>
        <PackageReference Include="Npgsql" />
        <PackageReference Include="Testcontainers.PostgreSql" />
        <ProjectReference Include="..\..\src\Inquiry.PostgreSql\Inquiry.PostgreSql.csproj" />
        <Compile Include="..\..\samples\Inquiry.Northwind\Models\**\*.cs" LinkBase="Northwind\Models" />
        <Compile Include="..\..\samples\Inquiry.Northwind\Stores\**\*.cs" LinkBase="Northwind\Stores" />
        <Compile Include="..\..\samples\Inquiry.Northwind\NorthwindSchema.cs" LinkBase="Northwind" />
        <Compile Remove="AssemblyDialect.Sqlite.cs" />
      </ItemGroup>
    </When>
    <Otherwise>
      <ItemGroup>
        <Compile Remove="AssemblyDialect.PostgreSql.cs" />
      </ItemGroup>
    </Otherwise>
  </Choose>
```
For the `PostgreSql` path, also **remove** the existing `Inquiry.Northwind` ProjectReference from the unconditional ItemGroup (move it into the `<Otherwise>` so it only applies to the default SQLite build):
```xml
      <!-- inside <Otherwise> -->
      <ItemGroup>
        <ProjectReference Include="..\..\samples\Inquiry.Northwind\Inquiry.Northwind.csproj" />
        <Compile Remove="AssemblyDialect.PostgreSql.cs" />
      </ItemGroup>
```

- [ ] **Step 2: Add the dialect files**

`AssemblyDialect.Sqlite.cs`:
```csharp
using Inquiry;
[assembly: InquiryDialect("Sqlite")]
```
`AssemblyDialect.PostgreSql.cs`:
```csharp
using Inquiry;
[assembly: InquiryDialect("PostgreSql")]
```
(The default `Inquiry.Northwind` assembly already declares `Sqlite`, so when building the default path the project references that assembly and `AssemblyDialect.Sqlite.cs` is excluded — see Step 3 note. To avoid a duplicate-attribute error in the default build, also `Compile Remove="AssemblyDialect.Sqlite.cs"` in `<Otherwise>` since the dialect comes from the referenced Northwind assembly.)

- [ ] **Step 3: Verify the default build still works**

Run: `dotnet build benchmarks/Inquiry.Benchmarks/Inquiry.Benchmarks.csproj -c Release`
Expected: succeeds (SQLite path; no duplicate `InquiryDialect`).

- [ ] **Step 4: Verify the PostgreSql build works**

Run: `dotnet build benchmarks/Inquiry.Benchmarks/Inquiry.Benchmarks.csproj -c Release -p:InquiryBenchProvider=PostgreSql`
Expected: succeeds (PG analyzer bakes the Northwind stores).

- [ ] **Step 5: Commit**

Message: `bench: dialect-parameterize benchmark project (Sqlite default, PostgreSql opt-in)`

### Task 21: PostgreSQL live benchmark proof

**Files:**
- Create: `benchmarks/Inquiry.Benchmarks/PostgreSqlNorthwindBenchmarks.cs` (compiled only when `InquiryBenchProvider=PostgreSql`)
- Modify: `benchmarks/Inquiry.Benchmarks/Inquiry.Benchmarks.csproj` (include this file only in the PG path)

- [ ] **Step 1: Gate the benchmark file** — add to the `<When ... 'PostgreSql'>` ItemGroup:
```xml
        <Compile Include="PostgreSqlNorthwindBenchmarks.cs" />
```
and add `<Compile Remove="PostgreSqlNorthwindBenchmarks.cs" />` to the `<Otherwise>` ItemGroup (so the default SQLite build ignores it). Place `PostgreSqlNorthwindBenchmarks.cs` with a file-level `#if` guard as a second safety net:
```csharp
#if INQUIRY_BENCH_POSTGRES
// ... benchmark ...
#endif
```
and define the constant in the PG path via the csproj `<When>` PropertyGroup:
```xml
      <PropertyGroup><DefineConstants>$(DefineConstants);INQUIRY_BENCH_POSTGRES</DefineConstants></PropertyGroup>
```

- [ ] **Step 2: Write the benchmark** — start the container once in `[GlobalSetup]`, seed Northwind, benchmark a representative Inquiry query:
```csharp
#if INQUIRY_BENCH_POSTGRES
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Inquiry.DependencyInjection;
using Inquiry.Northwind;
using Inquiry.Northwind.Stores;
using Inquiry.PostgreSql.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Inquiry.Benchmarks;

[MemoryDiagnoser]
public class PostgreSqlNorthwindBenchmarks
{
    private PostgreSqlContainer _container = null!;
    private ServiceProvider _services = null!;
    private CustomerStore _store = null!;

    [GlobalSetup]
    public async Task Setup()
    {
        _container = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();
        await _container.StartAsync();
        var cs = _container.GetConnectionString();

        await using (var conn = new NpgsqlConnection(cs))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = NorthwindSchema.PostgreSqlDdl;
            await cmd.ExecuteNonQueryAsync();
        }

        _services = new ServiceCollection().AddInquiry().AddInquiryPostgreSql(cs).BuildServiceProvider();
        _store = _services.GetRequiredService<CustomerStore>();
        await _store.InsertAsync(new Inquiry.Northwind.Models.Customer { CustomerID = "BENCH", CompanyName = "Bench", Country = "USA" });
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        await _services.DisposeAsync();
        await _container.DisposeAsync();
    }

    [Benchmark]
    public async Task<int> SelectByCountry() => (await _store.SelectByCountryAsync("USA")).Count;
}
#endif
```
(Adjust `SelectByCountryAsync` to whatever read method `CustomerStore` exposes — confirm in `samples/Inquiry.Northwind/Stores/CustomerStore.cs`.)

- [ ] **Step 3: Build the PG benchmark**

Run: `dotnet build benchmarks/Inquiry.Benchmarks/Inquiry.Benchmarks.csproj -c Release -p:InquiryBenchProvider=PostgreSql`
Expected: succeeds.

- [ ] **Step 4: Smoke-run (Docker on)**

Run: `dotnet run -c Release -p:InquiryBenchProvider=PostgreSql --project benchmarks/Inquiry.Benchmarks -- --filter *PostgreSqlNorthwindBenchmarks* --job short`
Expected: BenchmarkDotNet completes and prints a results table.

- [ ] **Step 5: Commit**

Message: `bench: add PostgreSQL live-environment benchmark proof`

---

## Phase 9 — Wrap-up

### Task 22: Full local verification + docs note

- [ ] **Step 1: Run the whole non-DB suite**

Run: `dotnet test tests/Inquiry.Generators.Tests/Inquiry.Generators.Tests.csproj; dotnet test tests/Inquiry.Tests/Inquiry.Tests.csproj; dotnet test tests/Inquiry.Sqlite.Tests/Inquiry.Sqlite.Tests.csproj`
Expected: all PASS across target frameworks.

- [ ] **Step 2: Run all provider suites with Docker on**

Run: `dotnet test tests/Inquiry.PostgreSql.Tests/Inquiry.PostgreSql.Tests.csproj -f net8.0; dotnet test tests/Inquiry.MySql.Tests/Inquiry.MySql.Tests.csproj -f net8.0; dotnet test tests/Inquiry.SqlServer.Tests/Inquiry.SqlServer.Tests.csproj -f net8.0; dotnet test tests/Inquiry.Oracle.Tests/Inquiry.Oracle.Tests.csproj -f net8.0`
Expected: all PASS.

- [ ] **Step 3: Confirm graceful skip** — stop Docker, run one provider suite

Run: `dotnet test tests/Inquiry.PostgreSql.Tests/Inquiry.PostgreSql.Tests.csproj -f net8.0`
Expected: tests **skip** (not fail), with the "container unavailable" reason.

- [ ] **Step 4: Add a short testing/CI section to the docs**

Append a "Live integration testing" section to `docs/plans/README.md` (or create `docs/testing.md`) summarizing: Docker is the only prerequisite; `dotnet test` auto-provisions containers; PR CI runs PG/MySQL/SQL Server; Oracle is nightly; benchmarks use `-p:InquiryBenchProvider=`.

- [ ] **Step 5: Commit**

Message: `docs: document live integration testing & benchmarking workflow`

### Task 23: Code review + merge

- [ ] **Step 1: Request code review** (superpowers:requesting-code-review) over `main..feature/live-runtime-testing`. Fix Critical/Important findings.

- [ ] **Step 2: Merge to main** (no PR, per project workflow), once green and reviewed:
```powershell
git checkout main; git merge --no-ff feature/live-runtime-testing
```

- [ ] **Step 3: Update tasks** — mark task #12 (live MySQL integration) and #13 (W4 benchmark) progress; note remaining deferrals.

---

## Self-review notes (coverage map)

- Goal 1 (PR matrix + nightly Oracle) → Tasks 18, 19.
- Goal 2 (Docker-only) → Tasks 8/12/14/16 fixtures; Task 22 Step 3.
- Goal 3 (own dialect SQL) → Tasks 7/12/14/16 (per-dialect compile).
- Goal 4 (faithful schema incl. indexes) → Tasks 5, 11, 13, 15, 17 (DDL index additions); Task 6 (annotations).
- Goal 5 (fidelity guardrail) → Tasks 1–3 (model/comparator), 4/10/13/15/17 (introspectors), fidelity tests.
- Goal 6 (generated-DDL verification) → Tasks 11/13/15/17 `GeneratedDdlIntegrationTests`.
- Goal 7 (benchmarking) → Tasks 20, 21.
