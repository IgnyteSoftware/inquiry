# DLG Benchmark Integration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the SQL-Server-only DLG datalayer as a benchmark "leg" beside ADO.NET/Dapper/EF Core/Inquiry in `Inquiry.Benchmarks.SqlServer`, for Shipper CRUD plus the read features DLG supports (Count, offset pagination, LIKE search, parent-with-children eager loading), and document everything DLG cannot do as `NotSupported`.

**Architecture:** DLG (generated, stored-procedure-based) is multi-targeted to `net8.0;net10.0` and referenced by the net8 benchmark host. The existing Testcontainer SQL Server gains DLG's `gsp_*` procedures (from DLG's embedded `SQLScript.sql`, GO-split) and a runtime-written `Inquiry.Benchmarks.DLG.config` so DLG runs through its intended `new DatabaseHelper(null)` config path. Correctness is gated by xUnit smoke tests in the existing `Inquiry.SqlServer.Tests` project (Testcontainers, `[SkippableFact]`).

**Tech Stack:** .NET 8/10, BenchmarkDotNet 0.14, Testcontainers.MsSql, Microsoft.Data.SqlClient, xUnit + Xunit.SkippableFact, Central Package Management.

**Spec:** `docs/superpowers/specs/2026-06-04-dlg-benchmark-integration-design.md`

---

## File map

| File | Change | Responsibility |
|---|---|---|
| `Directory.Packages.props` | modify | add `PackageVersion`s DLG needs |
| `benchmarks/Inquiry.Benchmarks.DLG/Inquiry.Benchmarks.DLG.csproj` | modify | multi-target, package refs, embed `SQLScript.sql` |
| `Inquiry.slnx` | modify | register the DLG project |
| `benchmarks/Inquiry.Benchmarks.SqlServer/Inquiry.Benchmarks.SqlServer.csproj` | modify | `ProjectReference` DLG |
| `benchmarks/Inquiry.Benchmarks.SqlServer/Dlg/DlgSetup.cs` | create | apply DLG procs + prime DLG `.config` |
| `tests/Inquiry.SqlServer.Tests/Inquiry.SqlServer.Tests.csproj` | modify | ref DLG, link `DlgSetup.cs` |
| `tests/Inquiry.SqlServer.Tests/Dlg/DlgDatabaseFixture.cs` | create | shared DLG DB (DDL+procs+config+seed) |
| `tests/Inquiry.SqlServer.Tests/Dlg/DlgCollection.cs` | create | xUnit collection for the fixture |
| `tests/Inquiry.SqlServer.Tests/Dlg/DlgSmokeTests.cs` | create | per-capability correctness asserts |
| `benchmarks/Inquiry.Benchmarks.SqlServer/SqlServerBenchmarkDatabase.cs` | modify | call `DlgSetup`, seed Categories+Products |
| `benchmarks/Inquiry.Benchmarks.SqlServer/ShipperBenchmarks.cs` | modify | add DLG CRUD legs |
| `benchmarks/Inquiry.Benchmarks.SqlServer/Ef/SqlServerProductContext.cs` | create | EF Product/Category for the extras |
| `benchmarks/Inquiry.Benchmarks.SqlServer/ProductReadBenchmarks.cs` | create | Count / OffsetPage / Search legs |
| `benchmarks/Inquiry.Benchmarks.SqlServer/EagerLoadingBenchmarks.cs` | create | parent-with-children eager legs |
| `benchmarks/Inquiry.Benchmarks.SqlServer/DLG-PARITY.md` | create | support recap |

---

## Task 1: Make `Inquiry.Benchmarks.DLG` compile

DLG's csproj has zero package references and a single TFM; it cannot compile as-is. Add the packages
it uses, multi-target it, and embed `SQLScript.sql` so `DlgSetup` can read it from the assembly.

**Files:**
- Modify: `Directory.Packages.props`
- Modify: `benchmarks/Inquiry.Benchmarks.DLG/Inquiry.Benchmarks.DLG.csproj`

- [ ] **Step 1: Add the missing package versions to the central catalog**

In `Directory.Packages.props`, inside the existing `<ItemGroup>`, add (keep the list alphabetical-ish; exact placement doesn't matter):

```xml
    <PackageVersion Include="Microsoft.Extensions.Configuration" Version="8.0.0" />
    <PackageVersion Include="Microsoft.Extensions.Configuration.Json" Version="8.0.1" />
    <PackageVersion Include="System.Configuration.ConfigurationManager" Version="8.0.1" />
    <PackageVersion Include="System.Data.OleDb" Version="8.0.1" />
    <PackageVersion Include="System.Data.Odbc" Version="8.0.1" />
```

(`Microsoft.Data.SqlClient` is already cataloged at 7.0.1 — do not re-add it.)

- [ ] **Step 2: Rewrite the DLG csproj**

Replace the entire contents of `benchmarks/Inquiry.Benchmarks.DLG/Inquiry.Benchmarks.DLG.csproj` with:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <!-- Multi-target so the net8 benchmark host can reference it while standalone net10 use is preserved. -->
    <TargetFrameworks>net8.0;net10.0</TargetFrameworks>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <!-- Generated DLG code is not warning-clean; do not let it fail the build. -->
    <NoWarn>$(NoWarn);CS8600;CS8601;CS8602;CS8603;CS8604;CS8618;CS8625;CS0168;CS0219;CS0649</NoWarn>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Data.SqlClient" />
    <PackageReference Include="Microsoft.Extensions.Configuration" />
    <PackageReference Include="Microsoft.Extensions.Configuration.Json" />
    <PackageReference Include="System.Configuration.ConfigurationManager" />
    <PackageReference Include="System.Data.OleDb" />
    <PackageReference Include="System.Data.Odbc" />
  </ItemGroup>

  <ItemGroup>
    <!-- Embedded so DlgSetup can read it from the assembly (resource: Inquiry.Benchmarks.DLG.SQLScript.sql). -->
    <EmbeddedResource Include="SQLScript.sql" />
    <!-- The .config is written at runtime by DlgSetup.PrimeConfig; don't copy the static one to output. -->
    <None Update="Inquiry.Benchmarks.DLG.config" CopyToOutputDirectory="Never" />
  </ItemGroup>

</Project>
```

- [ ] **Step 3: Build both target frameworks**

Run: `dotnet build benchmarks/Inquiry.Benchmarks.DLG/Inquiry.Benchmarks.DLG.csproj -c Release`
Expected: `Build succeeded` for `net8.0` and `net10.0`. If a package version conflict appears
(e.g. a net10 downgrade warning escalated to error), bump that package's `PackageVersion` to a 9.x
or 10.x line and rebuild. If a generated-code compile error appears (not a warning), record the
exact error — it likely needs an additional `NoWarn` code or a tiny generated-code shim, but do not
edit generated `.cs` content beyond what is required to compile.

- [ ] **Step 4: Commit**

```bash
git add Directory.Packages.props benchmarks/Inquiry.Benchmarks.DLG/Inquiry.Benchmarks.DLG.csproj
git commit -m "build(dlg): make Inquiry.Benchmarks.DLG compile (multi-target + packages + embed SQL)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 2: Register DLG in the solution and reference it from the SqlServer benchmark

**Files:**
- Modify: `Inquiry.slnx`
- Modify: `benchmarks/Inquiry.Benchmarks.SqlServer/Inquiry.Benchmarks.SqlServer.csproj`

- [ ] **Step 1: Add DLG to the solution**

In `Inquiry.slnx`, inside `<Folder Name="/benchmarks/">`, add (keep the existing entries):

```xml
    <Project Path="benchmarks/Inquiry.Benchmarks.DLG/Inquiry.Benchmarks.DLG.csproj" />
```

- [ ] **Step 2: Reference DLG from the SqlServer benchmark project**

In `benchmarks/Inquiry.Benchmarks.SqlServer/Inquiry.Benchmarks.SqlServer.csproj`, in the
`<ItemGroup>` that holds the existing `<ProjectReference>`s (the one referencing `Inquiry.csproj`
and `Inquiry.SqlServer.csproj`), add:

```xml
    <ProjectReference Include="..\Inquiry.Benchmarks.DLG\Inquiry.Benchmarks.DLG.csproj" />
```

- [ ] **Step 3: Build the SqlServer benchmark project**

Run: `dotnet build benchmarks/Inquiry.Benchmarks.SqlServer/Inquiry.Benchmarks.SqlServer.csproj -c Release`
Expected: `Build succeeded`. NuGet resolves DLG's `net8.0` asset for the net8 host.

- [ ] **Step 4: Commit**

```bash
git add Inquiry.slnx benchmarks/Inquiry.Benchmarks.SqlServer/Inquiry.Benchmarks.SqlServer.csproj
git commit -m "build(dlg): add DLG to solution + reference from SqlServer benchmark

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 3: `DlgSetup` — apply procedures and prime the config

**Files:**
- Create: `benchmarks/Inquiry.Benchmarks.SqlServer/Dlg/DlgSetup.cs`

- [ ] **Step 1: Write `DlgSetup`**

Create `benchmarks/Inquiry.Benchmarks.SqlServer/Dlg/DlgSetup.cs`:

```csharp
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Data.SqlClient;

namespace Inquiry.Benchmarks.SqlServer.Dlg;

/// <summary>
/// Bridges the generated DLG datalayer into the benchmark's Testcontainer: applies DLG's stored
/// procedures, then writes the <c>.config</c> DLG self-loads via <c>new DatabaseHelper(null)</c>.
/// </summary>
public static class DlgSetup
{
    // Resource name = {RootNamespace}.{file}. DLG's root namespace is Inquiry.Benchmarks.DLG.
    private const string ScriptResourceName = "Inquiry.Benchmarks.DLG.SQLScript.sql";

    /// <summary>Reads DLG's embedded SQLScript.sql and runs it batch-by-batch (split on GO).</summary>
    public static async Task ApplyStoredProceduresAsync(string connectionString)
    {
        var script = ReadEmbeddedScript();
        var batches = Regex.Split(script, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase);

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        foreach (var batch in batches)
        {
            var trimmed = batch.Trim();
            if (trimmed.Length == 0) continue;

            await using var command = connection.CreateCommand();
            command.CommandText = trimmed;
            try
            {
                await command.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
            catch (SqlException ex)
            {
                var preview = trimmed.Length > 200 ? trimmed[..200] : trimmed;
                throw new InvalidOperationException(
                    $"DLG SQLScript.sql batch failed: {ex.Message}\n--- batch (first 200 chars) ---\n{preview}", ex);
            }
        }
    }

    /// <summary>
    /// Writes <c>Inquiry.Benchmarks.DLG.config</c> next to the running assembly so DLG's
    /// ConfigurationHelper self-loads this connection string. MUST run before the first DLG call —
    /// ConfigurationHelper caches statically. Uses providerName="Microsoft.Data.SqlClient"; DLG's
    /// provider switch throws on "System.Data.SqlClient".
    /// </summary>
    public static void PrimeConfig(string connectionString)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Inquiry.Benchmarks.DLG.config");

        var config = new XElement("configuration",
            new XElement("connectionStrings",
                ConnEntry("Development", connectionString),
                ConnEntry("BackupDevelopment", connectionString)),
            new XElement("appSettings",
                AppSetting("ConnectionStringToUse", "Development"),
                AppSetting("BackupConnectionStringToUse", "BackupDevelopment"),
                AppSetting("ShouldUseBackupServer", "false")));

        new XDocument(new XDeclaration("1.0", "utf-8", null), config).Save(path);
    }

    private static XElement ConnEntry(string name, string connectionString) =>
        new("add",
            new XAttribute("name", name),
            new XAttribute("connectionString", connectionString),
            new XAttribute("providerName", "Microsoft.Data.SqlClient"));

    private static XElement AppSetting(string key, string value) =>
        new("add", new XAttribute("key", key), new XAttribute("value", value));

    private static string ReadEmbeddedScript()
    {
        var assembly = typeof(Inquiry.Benchmarks.DLG.DatabaseHelper).Assembly;
        using var stream = assembly.GetManifestResourceStream(ScriptResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{ScriptResourceName}' not found in {assembly.GetName().Name}. " +
                "Ensure SQLScript.sql is <EmbeddedResource> in Inquiry.Benchmarks.DLG.csproj.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build benchmarks/Inquiry.Benchmarks.SqlServer/Inquiry.Benchmarks.SqlServer.csproj -c Release`
Expected: `Build succeeded`.

- [ ] **Step 3: Commit**

```bash
git add benchmarks/Inquiry.Benchmarks.SqlServer/Dlg/DlgSetup.cs
git commit -m "feat(dlg): add DlgSetup (apply stored procs + prime .config)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 4: DLG smoke-test infrastructure + first test

This is the correctness gate for `DlgSetup` and the proc/DDL fit. The fixture builds one shared DLG
database (DLG's config cache is process-static, so all DLG tests must share one connection string).

**Files:**
- Modify: `tests/Inquiry.SqlServer.Tests/Inquiry.SqlServer.Tests.csproj`
- Create: `tests/Inquiry.SqlServer.Tests/Dlg/DlgDatabaseFixture.cs`
- Create: `tests/Inquiry.SqlServer.Tests/Dlg/DlgCollection.cs`
- Create: `tests/Inquiry.SqlServer.Tests/Dlg/DlgSmokeTests.cs`

- [ ] **Step 1: Reference DLG and link `DlgSetup.cs` into the test project**

In `tests/Inquiry.SqlServer.Tests/Inquiry.SqlServer.Tests.csproj`, inside the main `<ItemGroup>`, add:

```xml
    <ProjectReference Include="..\..\benchmarks\Inquiry.Benchmarks.DLG\Inquiry.Benchmarks.DLG.csproj" />
    <Compile Include="..\..\benchmarks\Inquiry.Benchmarks.SqlServer\Dlg\DlgSetup.cs" LinkBase="Dlg" />
```

(`NorthwindSchema.cs` is already link-compiled by this project, and `Testcontainers.MsSql` +
`Xunit.SkippableFact` are already referenced.)

- [ ] **Step 2: Write the shared DLG database fixture**

Create `tests/Inquiry.SqlServer.Tests/Dlg/DlgDatabaseFixture.cs`:

```csharp
using Inquiry.Benchmarks.SqlServer.Dlg;
using Inquiry.Northwind;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;
using Xunit;

namespace Inquiry.SqlServer.Tests.Dlg;

/// <summary>
/// One SQL Server container + one Northwind database with DLG's stored procedures applied and a
/// known seed, shared by all DLG smoke tests. DLG's config is process-static, so a single primed
/// connection string serves every test — hence one shared database here.
/// </summary>
public sealed class DlgDatabaseFixture : IAsyncLifetime
{
    private MsSqlContainer? _container;

    public bool IsAvailable { get; private set; }
    public string? SkipReason { get; private set; }

    public const int SeededShippers = 3;
    public const int SeededProducts = 5;

    /// <summary>Category id → number of products seeded under it (for the eager assertion).</summary>
    public IReadOnlyDictionary<int, int> ProductCountByCategoryId { get; private set; } =
        new Dictionary<int, int>();

    public int FirstCategoryId { get; private set; }

    public async Task InitializeAsync()
    {
        try
        {
            _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04").Build();
            await _container.StartAsync();
            var cs = _container.GetConnectionString();

            await ApplySchemaAsync(cs);
            await DlgSetup.ApplyStoredProceduresAsync(cs);
            await SeedAsync(cs);
            DlgSetup.PrimeConfig(cs);

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

    private static async Task ApplySchemaAsync(string cs)
    {
        await using var connection = new SqlConnection(cs);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = NorthwindSchema.SqlServerDdl;
        await command.ExecuteNonQueryAsync();
    }

    private async Task SeedAsync(string cs)
    {
        await using var connection = new SqlConnection(cs);
        await connection.OpenAsync();

        for (int i = 0; i < SeededShippers; i++)
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "INSERT INTO Shippers (CompanyName, Phone) VALUES (@c, @p);";
            cmd.Parameters.AddWithValue("@c", $"Shipper {i}");
            cmd.Parameters.AddWithValue("@p", $"555-{i:0000}");
            await cmd.ExecuteNonQueryAsync();
        }

        // Two categories; all seeded products go to the first (clean eager assertion).
        var categoryIds = new List<int>();
        for (int i = 0; i < 2; i++)
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText =
                "INSERT INTO Categories (CategoryName, Description) OUTPUT inserted.CategoryID VALUES (@n, @d);";
            cmd.Parameters.AddWithValue("@n", $"Category {i}");
            cmd.Parameters.AddWithValue("@d", $"Desc {i}");
            categoryIds.Add((int)(await cmd.ExecuteScalarAsync())!);
        }
        FirstCategoryId = categoryIds[0];

        for (int i = 0; i < SeededProducts; i++)
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText =
                "INSERT INTO Products (ProductName, CategoryID, UnitPrice, Discontinued) VALUES (@n, @cat, @price, 0);";
            cmd.Parameters.AddWithValue("@n", $"Product {i}");
            cmd.Parameters.AddWithValue("@cat", categoryIds[0]);
            cmd.Parameters.AddWithValue("@price", 10m + i);
            await cmd.ExecuteNonQueryAsync();
        }

        ProductCountByCategoryId = new Dictionary<int, int>
        {
            [categoryIds[0]] = SeededProducts,
            [categoryIds[1]] = 0,
        };
    }
}
```

- [ ] **Step 3: Write the collection definition**

Create `tests/Inquiry.SqlServer.Tests/Dlg/DlgCollection.cs`:

```csharp
using Xunit;

namespace Inquiry.SqlServer.Tests.Dlg;

[CollectionDefinition(Name)]
public sealed class DlgCollection : ICollectionFixture<DlgDatabaseFixture>
{
    public const string Name = "Dlg";
}
```

- [ ] **Step 4: Write the first smoke test (SelectAll)**

Create `tests/Inquiry.SqlServer.Tests/Dlg/DlgSmokeTests.cs`:

```csharp
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
```

- [ ] **Step 5: Run the first test (drives DlgSetup to green)**

Run: `dotnet test tests/Inquiry.SqlServer.Tests/Inquiry.SqlServer.Tests.csproj -c Release --filter "FullyQualifiedName~DlgSmokeTests" -f net8.0`
Expected: PASS (or SKIPPED if Docker is unavailable — then start Docker and rerun; this test must
actually execute to validate the integration). If it FAILS, the failure is real (proc application,
config priming, or the provider-name path) — diagnose using the exception message
(`ApplyStoredProceduresAsync` includes the failing batch preview) and fix `DlgSetup` until green.

- [ ] **Step 6: Commit**

```bash
git add tests/Inquiry.SqlServer.Tests/Inquiry.SqlServer.Tests.csproj tests/Inquiry.SqlServer.Tests/Dlg/
git commit -m "test(dlg): smoke-test fixture + SelectAll, validating DlgSetup end-to-end

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 5: Remaining DLG capability smoke tests

Each asserts its own effect (no reliance on global row counts) so they are order-independent on the
shared database.

**Files:**
- Modify: `tests/Inquiry.SqlServer.Tests/Dlg/DlgSmokeTests.cs`

- [ ] **Step 1: Add CRUD + read-extra smoke tests**

Append these methods inside the `DlgSmokeTests` class (before the closing brace):

```csharp
    [SkippableFact]
    public async Task SelectByKey_ReturnsShipper()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);

        var shipper = await Shipper.SelectOneAsync(1);

        Assert.NotNull(shipper);
        Assert.Equal(1, shipper!.ShipperID);
    }

    [SkippableFact]
    public async Task Insert_AddsRow_FoundBySelectByField()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        var unique = "Ins-" + Guid.NewGuid().ToString("N")[..8];

        var ok = await new Shipper { CompanyName = unique, Phone = "555-7777" }.InsertAsync();
        Assert.True(ok);

        var found = await Shipper.SelectByFieldAsync(ShipperFields.CompanyName, unique);
        Assert.Single(found);
    }

    [SkippableFact]
    public async Task Update_ChangesRow()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        var unique = "Upd-" + Guid.NewGuid().ToString("N")[..8];

        // getBackValues:true repopulates the inserted entity with its IDENTITY key.
        var entity = new Shipper { CompanyName = unique, Phone = "555-0001" };
        await entity.InsertAsync(getBackValues: true);
        Assert.True(entity.ShipperID > 0);

        entity.Phone = "555-0002";
        Assert.True(await entity.UpdateAsync());

        var reloaded = await Shipper.SelectOneAsync(entity.ShipperID);
        Assert.Equal("555-0002", reloaded!.Phone);
    }

    [SkippableFact]
    public async Task Upsert_OnExistingKey_Updates()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        var unique = "Ups-" + Guid.NewGuid().ToString("N")[..8];

        var entity = new Shipper { CompanyName = "seed", Phone = "x" };
        await entity.InsertAsync(getBackValues: true);

        var changed = new Shipper { ShipperID = entity.ShipperID, CompanyName = unique, Phone = "555-0003" };
        Assert.True(await changed.UpsertAsync());

        var reloaded = await Shipper.SelectOneAsync(entity.ShipperID);
        Assert.Equal(unique, reloaded!.CompanyName);
    }

    [SkippableFact]
    public async Task Count_ReturnsSeededProducts()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);

        var count = await Product.SelectAllCountAsync();

        Assert.Equal(DlgDatabaseFixture.SeededProducts, count);
    }

    [SkippableFact]
    public async Task OffsetPage_ReturnsPageSizedSlice()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);

        var page = await Product.SelectAllPagedAsync(pageNumber: 1, pageSize: 2, orderByStatement: "ProductID");

        Assert.Equal(2, page.Count);
    }

    [SkippableFact]
    public async Task Search_Like_FindsSeededProducts()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);

        var matches = await Product.SelectByFieldAsync(
            ProductFields.ProductName, "%Product%", null, TypeOperation.Like);

        Assert.Equal(DlgDatabaseFixture.SeededProducts, matches.Count);
    }

    [SkippableFact]
    public async Task Eager_LoadsCategoryWithProducts()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        var catId = _fixture.FirstCategoryId;
        var expected = _fixture.ProductCountByCategoryId[catId];

        var category = await Category.SelectOneWithProductsUsingCategoryIDAsync(catId);

        Assert.NotNull(category);
        // Child collection navigation property mirrors Shipper's OrdersUsingShipVia.
        // Confirm the exact name in CategoryBase.cs (expected: ProductsUsingCategoryID).
        Assert.Equal(expected, category!.ProductsUsingCategoryID!.Count);
    }
```

- [ ] **Step 2: Run all DLG smoke tests**

Run: `dotnet test tests/Inquiry.SqlServer.Tests/Inquiry.SqlServer.Tests.csproj -c Release --filter "FullyQualifiedName~DlgSmokeTests" -f net8.0`
Expected: all PASS. If `Eager_LoadsCategoryWithProducts` fails to compile on the navigation property
name, open `benchmarks/Inquiry.Benchmarks.DLG/CategoryBase.cs`, find the child-collection property
(search `Products` / `SelectAllByForeignKeyCategoryID`), and use its exact name. If `Update`/`Upsert`
fail on the wrong row, inspect DLG's emitted SQL via the proc and adjust the test's expectations to
DLG's documented dirty-tracking semantics (only changed columns are sent).

- [ ] **Step 3: Commit**

```bash
git add tests/Inquiry.SqlServer.Tests/Dlg/DlgSmokeTests.cs
git commit -m "test(dlg): smoke tests for CRUD, count, paging, LIKE search, eager load

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 6: Wire DLG setup + extended seed into the benchmark database

**Files:**
- Modify: `benchmarks/Inquiry.Benchmarks.SqlServer/SqlServerBenchmarkDatabase.cs`

- [ ] **Step 1: Apply DLG procs + prime config in `CreateAsync`**

In `SqlServerBenchmarkDatabase.CreateAsync`, immediately after the existing
`await SeedAsync(connectionString, seedRows).ConfigureAwait(false);` line, add:

```csharp
                // DLG: create its stored procedures and write the .config it self-loads.
                await Dlg.DlgSetup.ApplyStoredProceduresAsync(connectionString).ConfigureAwait(false);
                Dlg.DlgSetup.PrimeConfig(connectionString);
```

(`Dlg` resolves to `Inquiry.Benchmarks.SqlServer.Dlg`; the file's namespace is
`Inquiry.Benchmarks.SqlServer`, so the simple name binds. If the build complains, fully-qualify as
`Inquiry.Benchmarks.SqlServer.Dlg.DlgSetup`.)

- [ ] **Step 2: Expose Product/Category Inquiry stores + extend the seed**

Still in `SqlServerBenchmarkDatabase.cs`:

Add `using Inquiry.Northwind.Models;` to the usings if not present.

Add these accessors next to the existing `public ShipperStore Shippers => ...`:

```csharp
    public ProductStore Products => _services!.GetRequiredService<ProductStore>();
    public CategoryStore Categories => _services!.GetRequiredService<CategoryStore>();
```

Replace the body of `SeedAsync` with the following (keeps the Shippers seed, adds Categories +
Products in the same transaction):

```csharp
    private static async Task SeedAsync(string connectionString, int rowCount)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var tx = await connection.BeginTransactionAsync().ConfigureAwait(false);

        // Shippers — IDENTITY PK; ShipperID is not supplied.
        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = (SqlTransaction)tx;
            insert.CommandText = "INSERT INTO Shippers (CompanyName, Phone) VALUES (@company, @phone);";
            var pCompany = insert.Parameters.Add("@company", System.Data.SqlDbType.NVarChar, 40);
            var pPhone   = insert.Parameters.Add("@phone",   System.Data.SqlDbType.NVarChar, -1);
            for (int i = 0; i < rowCount; i++)
            {
                pCompany.Value = $"Shipper {i}";
                pPhone.Value   = $"555-{i:0000}";
                await insert.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
        }

        // Categories — 10 fixed categories; capture their IDENTITY ids for product FKs.
        var categoryIds = new List<int>();
        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = (SqlTransaction)tx;
            insert.CommandText =
                "INSERT INTO Categories (CategoryName, Description) OUTPUT inserted.CategoryID VALUES (@name, @desc);";
            var pName = insert.Parameters.Add("@name", System.Data.SqlDbType.NVarChar, 15);
            var pDesc = insert.Parameters.Add("@desc", System.Data.SqlDbType.NVarChar, -1);
            for (int i = 0; i < 10; i++)
            {
                pName.Value = $"Category {i}";
                pDesc.Value = $"Description {i}";
                categoryIds.Add((int)(await insert.ExecuteScalarAsync().ConfigureAwait(false))!);
            }
        }

        // Products — rowCount rows spread across the categories; distinct names for LIKE search.
        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = (SqlTransaction)tx;
            insert.CommandText =
                "INSERT INTO Products (ProductName, CategoryID, UnitPrice, Discontinued) " +
                "VALUES (@name, @cat, @price, 0);";
            var pName  = insert.Parameters.Add("@name",  System.Data.SqlDbType.NVarChar, 40);
            var pCat   = insert.Parameters.Add("@cat",   System.Data.SqlDbType.Int);
            var pPrice = insert.Parameters.Add("@price", System.Data.SqlDbType.Decimal);
            for (int i = 0; i < rowCount; i++)
            {
                pName.Value  = $"Product {i}";
                pCat.Value   = categoryIds[i % categoryIds.Count];
                pPrice.Value = 10m + (i % 100);
                await insert.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
        }

        await tx.CommitAsync().ConfigureAwait(false);
    }
```

- [ ] **Step 3: Build and re-run the DLG smoke tests (still green)**

Run: `dotnet build benchmarks/Inquiry.Benchmarks.SqlServer/Inquiry.Benchmarks.SqlServer.csproj -c Release`
Expected: `Build succeeded`.

Run: `dotnet test tests/Inquiry.SqlServer.Tests/Inquiry.SqlServer.Tests.csproj -c Release --filter "FullyQualifiedName~DlgSmokeTests" -f net8.0`
Expected: all PASS (the fixture is independent of the benchmark seed, so this confirms no regression).

- [ ] **Step 4: Commit**

```bash
git add benchmarks/Inquiry.Benchmarks.SqlServer/SqlServerBenchmarkDatabase.cs
git commit -m "feat(dlg): wire DLG setup + seed Categories/Products into the SqlServer bench DB

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 7: Add the DLG leg to `ShipperBenchmarks`

**Files:**
- Modify: `benchmarks/Inquiry.Benchmarks.SqlServer/ShipperBenchmarks.cs`

- [ ] **Step 1: Alias the DLG namespace**

At the top of `ShipperBenchmarks.cs`, add with the other usings:

```csharp
using Dlg = Inquiry.Benchmarks.DLG;
```

- [ ] **Step 2: Add the six DLG benchmark methods**

Add one DLG method to each category. Place each next to the existing `*_Inquiry` method for that
category (so the grouped output reads ADO → Dapper → EF → Inquiry → DLG):

```csharp
    [BenchmarkCategory("SelectAll"), Benchmark]
    public async Task<int> SelectAll_Dlg()
    {
        var list = await Dlg.Shipper.SelectAllAsync();
        return list.Count;
    }

    [BenchmarkCategory("SelectByKey"), Benchmark]
    public async Task<Dlg.Shipper?> SelectByKey_Dlg()
        => await Dlg.Shipper.SelectOneAsync(TargetShipperId);

    [BenchmarkCategory("SelectByField"), Benchmark]
    public async Task<int> SelectByField_Dlg()
    {
        var list = await Dlg.Shipper.SelectByFieldAsync(Dlg.ShipperFields.CompanyName, TargetCompanyName);
        return list.Count;
    }

    [BenchmarkCategory("Insert"), Benchmark]
    public async Task<bool> Insert_Dlg()
        => await new Dlg.Shipper { CompanyName = "Bench Shipper", Phone = "555-0000" }.InsertAsync();

    [BenchmarkCategory("Update"), Benchmark]
    public async Task<bool> Update_Dlg()
        => await new Dlg.Shipper { ShipperID = TargetShipperId, CompanyName = "Updated Shipper", Phone = "555-9999" }.UpdateAsync();

    [BenchmarkCategory("Upsert"), Benchmark]
    public async Task<bool> Upsert_Dlg()
        => await new Dlg.Shipper { ShipperID = TargetShipperId, CompanyName = "Upserted Shipper", Phone = "555-1234" }.UpsertAsync();
```

- [ ] **Step 3: Build**

Run: `dotnet build benchmarks/Inquiry.Benchmarks.SqlServer/Inquiry.Benchmarks.SqlServer.csproj -c Release`
Expected: `Build succeeded`.

- [ ] **Step 4: Commit**

```bash
git add benchmarks/Inquiry.Benchmarks.SqlServer/ShipperBenchmarks.cs
git commit -m "feat(dlg): add DLG leg to SqlServer ShipperBenchmarks (CRUD)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 8: EF Product/Category context for the read extras

**Files:**
- Create: `benchmarks/Inquiry.Benchmarks.SqlServer/Ef/SqlServerProductContext.cs`

- [ ] **Step 1: Write the EF context + POCOs**

Create `benchmarks/Inquiry.Benchmarks.SqlServer/Ef/SqlServerProductContext.cs` (mirrors the existing
`SqlServerShipperContext` mapping style — unquoted Northwind identifiers, IDENTITY key):

```csharp
using Microsoft.EntityFrameworkCore;

namespace Inquiry.Benchmarks.SqlServer.Ef;

/// <summary>EF Core model for the Product/Category read-extra benchmarks (Count, OffsetPage, Search).</summary>
public sealed class SqlServerProductContext : DbContext
{
    public SqlServerProductContext(DbContextOptions<SqlServerProductContext> options) : base(options) { }

    public DbSet<SqlServerEfProduct> Products => Set<SqlServerEfProduct>();
    public DbSet<SqlServerEfCategory> Categories => Set<SqlServerEfCategory>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SqlServerEfProduct>(e =>
        {
            e.ToTable("Products");
            e.HasKey(x => x.ProductID);
            e.Property(x => x.ProductID).HasColumnName("ProductID").ValueGeneratedOnAdd();
            e.Property(x => x.ProductName).HasColumnName("ProductName");
            e.Property(x => x.CategoryID).HasColumnName("CategoryID");
            e.Property(x => x.UnitPrice).HasColumnName("UnitPrice");
            e.Property(x => x.Discontinued).HasColumnName("Discontinued");
        });

        modelBuilder.Entity<SqlServerEfCategory>(e =>
        {
            e.ToTable("Categories");
            e.HasKey(x => x.CategoryID);
            e.Property(x => x.CategoryID).HasColumnName("CategoryID").ValueGeneratedOnAdd();
            e.Property(x => x.CategoryName).HasColumnName("CategoryName");
            e.Property(x => x.Description).HasColumnName("Description");
        });
    }
}

public sealed class SqlServerEfProduct
{
    public int ProductID { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public int? CategoryID { get; set; }
    public decimal? UnitPrice { get; set; }
    public bool Discontinued { get; set; }
}

public sealed class SqlServerEfCategory
{
    public int CategoryID { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public string? Description { get; set; }
}
```

- [ ] **Step 2: Register the context factory**

In `SqlServerBenchmarkDatabase.cs`, in `CreateAsync`, where the existing
`.AddDbContextFactory<SqlServerShipperContext>(...)` is chained onto the `ServiceCollection`, add a
second factory right after it:

```csharp
                    .AddDbContextFactory<SqlServerProductContext>(options => options.UseSqlServer(connectionString))
```

Add a field + accessor mirroring `_dbContextFactory`/`DbContextFactory`:

```csharp
    private static IDbContextFactory<SqlServerProductContext>? _productContextFactory;

    public IDbContextFactory<SqlServerProductContext> ProductContextFactory => _productContextFactory!;
```

And assign it next to the existing `_dbContextFactory = ...` line:

```csharp
                _productContextFactory = services.GetRequiredService<IDbContextFactory<SqlServerProductContext>>();
```

(Ensure `using Inquiry.Benchmarks.SqlServer.Ef;` is present — it already is for the Shipper context.)

- [ ] **Step 3: Build**

Run: `dotnet build benchmarks/Inquiry.Benchmarks.SqlServer/Inquiry.Benchmarks.SqlServer.csproj -c Release`
Expected: `Build succeeded`.

- [ ] **Step 4: Commit**

```bash
git add benchmarks/Inquiry.Benchmarks.SqlServer/Ef/SqlServerProductContext.cs benchmarks/Inquiry.Benchmarks.SqlServer/SqlServerBenchmarkDatabase.cs
git commit -m "feat(bench): add EF Product/Category context for read-extra benchmarks

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 9: Read-extra benchmark classes (Count / OffsetPage / Search, and Eager)

**Files:**
- Create: `benchmarks/Inquiry.Benchmarks.SqlServer/ProductReadBenchmarks.cs`
- Create: `benchmarks/Inquiry.Benchmarks.SqlServer/EagerLoadingBenchmarks.cs`

- [ ] **Step 1: Write `ProductReadBenchmarks`**

Create `benchmarks/Inquiry.Benchmarks.SqlServer/ProductReadBenchmarks.cs`:

```csharp
using System.Data;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Dlg = Inquiry.Benchmarks.DLG;

namespace Inquiry.Benchmarks.SqlServer;

/// <summary>
/// Read comparison over the Northwind <c>Products</c> table against SQL Server — Count, offset
/// pagination, and a LIKE search — across ADO.NET (baseline), Dapper, EF Core, Inquiry, and DLG.
/// </summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class ProductReadBenchmarks
{
    private SqlServerBenchmarkDatabase _db = null!;

    [Params(1000)]
    public int Rows;

    private const int PageOffset = 20;
    private const int PageSize   = 20;
    private const string NamePattern = "%Product 1%";

    [GlobalSetup]
    public void GlobalSetup() => _db = SqlServerBenchmarkDatabase.CreateAsync(Rows).GetAwaiter().GetResult();

    [GlobalCleanup]
    public void GlobalCleanup() => _db.DisposeAsync().AsTask().GetAwaiter().GetResult();

    private SqlConnection OpenConnection() => new SqlConnection(_db.ConnectionString);

    // ---- Count --------------------------------------------------------------------------

    [BenchmarkCategory("Count"), Benchmark(Baseline = true)]
    public async Task<long> Count_AdoNet()
    {
        await using var connection = OpenConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT_BIG(*) FROM [Products]";
        return (long)(await command.ExecuteScalarAsync())!;
    }

    [BenchmarkCategory("Count"), Benchmark]
    public async Task<long> Count_Dapper()
    {
        await using var connection = OpenConnection();
        await connection.OpenAsync();
        return await connection.ExecuteScalarAsync<long>("SELECT COUNT_BIG(*) FROM [Products]");
    }

    [BenchmarkCategory("Count"), Benchmark]
    public async Task<int> Count_EfCore()
    {
        await using var ctx = await _db.ProductContextFactory.CreateDbContextAsync();
        return await ctx.Products.CountAsync();
    }

    [BenchmarkCategory("Count"), Benchmark]
    public async Task<long> Count_Inquiry() => await _db.Products.CountAsync();

    [BenchmarkCategory("Count"), Benchmark]
    public async Task<int> Count_Dlg() => await Dlg.Product.SelectAllCountAsync();

    // ---- OffsetPage ---------------------------------------------------------------------

    [BenchmarkCategory("OffsetPage"), Benchmark(Baseline = true)]
    public async Task<int> OffsetPage_AdoNet()
    {
        await using var connection = OpenConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT [ProductID] FROM [Products] ORDER BY [ProductID] OFFSET @off ROWS FETCH NEXT @lim ROWS ONLY";
        command.Parameters.AddWithValue("@off", PageOffset);
        command.Parameters.AddWithValue("@lim", PageSize);
        var n = 0;
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleResult | CommandBehavior.SequentialAccess);
        while (await reader.ReadAsync()) n++;
        return n;
    }

    [BenchmarkCategory("OffsetPage"), Benchmark]
    public async Task<int> OffsetPage_Dapper()
    {
        await using var connection = OpenConnection();
        await connection.OpenAsync();
        var list = (await connection.QueryAsync<int>(
            "SELECT [ProductID] FROM [Products] ORDER BY [ProductID] OFFSET @off ROWS FETCH NEXT @lim ROWS ONLY",
            new { off = PageOffset, lim = PageSize })).AsList();
        return list.Count;
    }

    [BenchmarkCategory("OffsetPage"), Benchmark]
    public async Task<int> OffsetPage_EfCore()
    {
        await using var ctx = await _db.ProductContextFactory.CreateDbContextAsync();
        var list = await ctx.Products.AsNoTracking().OrderBy(p => p.ProductID).Skip(PageOffset).Take(PageSize).ToListAsync();
        return list.Count;
    }

    [BenchmarkCategory("OffsetPage"), Benchmark]
    public async Task<int> OffsetPage_Inquiry()
    {
        var list = await _db.Products.PageByIdAsync(PageOffset, PageSize);
        return list.Count;
    }

    [BenchmarkCategory("OffsetPage"), Benchmark]
    public async Task<int> OffsetPage_Dlg()
    {
        // DLG paging is 1-based page numbers: page 2 @ size 20 == offset 20.
        var list = await Dlg.Product.SelectAllPagedAsync(pageNumber: 2, pageSize: PageSize, orderByStatement: "ProductID");
        return list.Count;
    }

    // ---- Search (LIKE) ------------------------------------------------------------------

    [BenchmarkCategory("Search"), Benchmark(Baseline = true)]
    public async Task<int> Search_AdoNet()
    {
        await using var connection = OpenConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT [ProductID] FROM [Products] WHERE [ProductName] LIKE @p";
        command.Parameters.AddWithValue("@p", NamePattern);
        var n = 0;
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleResult | CommandBehavior.SequentialAccess);
        while (await reader.ReadAsync()) n++;
        return n;
    }

    [BenchmarkCategory("Search"), Benchmark]
    public async Task<int> Search_Dapper()
    {
        await using var connection = OpenConnection();
        await connection.OpenAsync();
        var list = (await connection.QueryAsync<int>(
            "SELECT [ProductID] FROM [Products] WHERE [ProductName] LIKE @p", new { p = NamePattern })).AsList();
        return list.Count;
    }

    [BenchmarkCategory("Search"), Benchmark]
    public async Task<int> Search_EfCore()
    {
        await using var ctx = await _db.ProductContextFactory.CreateDbContextAsync();
        var list = await ctx.Products.AsNoTracking().Where(p => EF.Functions.Like(p.ProductName, NamePattern)).ToListAsync();
        return list.Count;
    }

    [BenchmarkCategory("Search"), Benchmark]
    public async Task<int> Search_Inquiry()
    {
        // SearchAsync ANDs UnitPrice >= minPrice with ProductName LIKE; minPrice 0 makes the price clause a no-op.
        var list = await _db.Products.SearchAsync(0m, NamePattern);
        return list.Count;
    }

    [BenchmarkCategory("Search"), Benchmark]
    public async Task<int> Search_Dlg()
    {
        var list = await Dlg.Product.SelectByFieldAsync(Dlg.ProductFields.ProductName, NamePattern, null, Dlg.TypeOperation.Like);
        return list.Count;
    }
}
```

- [ ] **Step 2: Write `EagerLoadingBenchmarks` (parent + children)**

Create `benchmarks/Inquiry.Benchmarks.SqlServer/EagerLoadingBenchmarks.cs`. EF is omitted here,
matching the core `EagerLoadingBenchmarks` precedent (its `Include` is not a like-for-like match to
an explicit parent+children load).

```csharp
using System.Data;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Dapper;
using Microsoft.Data.SqlClient;
using Dlg = Inquiry.Benchmarks.DLG;

namespace Inquiry.Benchmarks.SqlServer;

/// <summary>
/// Eager parent-with-children: load one <c>Category</c> together with its <c>Products</c> in a single
/// round-trip — the shape DLG supports natively (<c>SelectOneWithProductsUsingCategoryID</c>). Legs:
/// ADO.NET (baseline, two result sets), Dapper (multi-result), Inquiry (generated eager), DLG.
/// </summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class EagerLoadingBenchmarks
{
    private SqlServerBenchmarkDatabase _db = null!;

    [Params(1000)]
    public int Rows;

    // First category id under the benchmark seed. Categories are seeded first (10 rows), so id 1 exists.
    private const int TargetCategoryId = 1;

    [GlobalSetup]
    public void GlobalSetup() => _db = SqlServerBenchmarkDatabase.CreateAsync(Rows).GetAwaiter().GetResult();

    [GlobalCleanup]
    public void GlobalCleanup() => _db.DisposeAsync().AsTask().GetAwaiter().GetResult();

    private SqlConnection OpenConnection() => new SqlConnection(_db.ConnectionString);

    [BenchmarkCategory("EagerParentChildren"), Benchmark(Baseline = true)]
    public async Task<int> Eager_AdoNet()
    {
        await using var connection = OpenConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT [CategoryID], [CategoryName] FROM [Categories] WHERE [CategoryID] = @id; " +
            "SELECT [ProductID] FROM [Products] WHERE [CategoryID] = @id;";
        command.Parameters.AddWithValue("@id", TargetCategoryId);
        await using var reader = await command.ExecuteReaderAsync();
        var hasCategory = await reader.ReadAsync();
        await reader.NextResultAsync();
        var childCount = 0;
        while (await reader.ReadAsync()) childCount++;
        return hasCategory ? childCount : -1;
    }

    [BenchmarkCategory("EagerParentChildren"), Benchmark]
    public async Task<int> Eager_Dapper()
    {
        await using var connection = OpenConnection();
        await connection.OpenAsync();
        using var multi = await connection.QueryMultipleAsync(
            "SELECT [CategoryID], [CategoryName] FROM [Categories] WHERE [CategoryID] = @id; " +
            "SELECT [ProductID] FROM [Products] WHERE [CategoryID] = @id;",
            new { id = TargetCategoryId });
        _ = await multi.ReadFirstOrDefaultAsync<(int, string)>();
        var children = (await multi.ReadAsync<int>()).AsList();
        return children.Count;
    }

    [BenchmarkCategory("EagerParentChildren"), Benchmark]
    public async Task<int> Eager_Inquiry()
    {
        var category = await _db.Categories.SelectByKeyWithProductsAsync(TargetCategoryId);
        return category?.Products?.Count ?? -1;
    }

    [BenchmarkCategory("EagerParentChildren"), Benchmark]
    public async Task<int> Eager_Dlg()
    {
        var category = await Dlg.Category.SelectOneWithProductsUsingCategoryIDAsync(TargetCategoryId);
        // Child collection nav mirrors Shipper's OrdersUsingShipVia; confirm name in CategoryBase.cs.
        return category?.ProductsUsingCategoryID?.Count ?? -1;
    }
}
```

- [ ] **Step 3: Confirm the Inquiry eager child-collection property name**

The `Eager_Inquiry` leg uses `category.Products`. Open
`samples/Inquiry.Northwind/Models/Category.cs` and confirm the products navigation property name
(it may be `Products`). Adjust if different. Do the same for DLG's `ProductsUsingCategoryID` against
`benchmarks/Inquiry.Benchmarks.DLG/CategoryBase.cs`.

- [ ] **Step 4: Build**

Run: `dotnet build benchmarks/Inquiry.Benchmarks.SqlServer/Inquiry.Benchmarks.SqlServer.csproj -c Release`
Expected: `Build succeeded`. Fix any nav-property name mismatch surfaced here.

- [ ] **Step 5: Commit**

```bash
git add benchmarks/Inquiry.Benchmarks.SqlServer/ProductReadBenchmarks.cs benchmarks/Inquiry.Benchmarks.SqlServer/EagerLoadingBenchmarks.cs
git commit -m "feat(dlg): add Count/OffsetPage/Search + eager parent-children benchmarks with DLG leg

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Task 10: Support recap + final verification

**Files:**
- Create: `benchmarks/Inquiry.Benchmarks.SqlServer/DLG-PARITY.md`
- Modify: `docs/superpowers/specs/2026-06-04-dlg-benchmark-integration-design.md` (status line)

- [ ] **Step 1: Write the recap**

Create `benchmarks/Inquiry.Benchmarks.SqlServer/DLG-PARITY.md`:

```markdown
# DLG benchmark parity (SQL Server)

DLG is the legacy stored-procedure datalayer (`benchmarks/Inquiry.Benchmarks.DLG`). It is
SQL-Server-only, so its benchmark legs live in `Inquiry.Benchmarks.SqlServer` beside the ADO.NET,
Dapper, EF Core, and Inquiry legs. Run with Docker available and `--inProcess`.

## Supported — has a live DLG benchmark leg (Phase 1)

| Class | Category | DLG call |
|---|---|---|
| ShipperBenchmarks | SelectAll | `Shipper.SelectAllAsync()` |
| ShipperBenchmarks | SelectByKey | `Shipper.SelectOneAsync(id)` |
| ShipperBenchmarks | SelectByField | `Shipper.SelectByFieldAsync(ShipperFields.CompanyName, name)` |
| ShipperBenchmarks | Insert | `new Shipper{…}.InsertAsync()` |
| ShipperBenchmarks | Update | `new Shipper{…}.UpdateAsync()` (XML dirty-diff) |
| ShipperBenchmarks | Upsert | `new Shipper{…}.UpsertAsync()` |
| ProductReadBenchmarks | Count | `Product.SelectAllCountAsync()` |
| ProductReadBenchmarks | OffsetPage | `Product.SelectAllPagedAsync(pageNo, size, "ProductID")` |
| ProductReadBenchmarks | Search (LIKE) | `Product.SelectByFieldAsync(ProductFields.ProductName, "%x%", null, TypeOperation.Like)` |
| EagerLoadingBenchmarks | EagerParentChildren | `Category.SelectOneWithProductsUsingCategoryIDAsync(id)` |

## NotSupported — DLG has no first-class API

| Suite category | Why |
|---|---|
| Pagination — KeysetPage | DLG offers only offset paging (`SelectAllPaged`); no keyset/cursor. |
| Predicate — InList (`IN`) | `TypeOperation` is `{ Like, Less, Greater, Equal, NotEqual }` — no `IN`. |
| Aggregate — Sum / Avg / Min / Max | DLG exposes only `SelectAllCount`; no other aggregates. |
| Projection (subset columns → DTO) | DLG always materializes the full generated entity. |
| EagerLoading — all rows + parent (stitch) | DLG's only eager primitive is one-parent-with-children; the all-rows-with-parent shape is lazy per-row. |
| Batch — BatchInsert | DLG performs single-row Insert/Update/Upsert/Delete only. |

## N/A — not meaningful for DLG

| Suite category | Why |
|---|---|
| ParameterBinding (bind micro-benchmarks) | DLG binds only through stored-procedure parameters; no comparable low-level surface. |
| CrossDialectRead | DLG is single-dialect (SQL Server only). |

## Deferred to Phase 2 (DLG-capable, not yet wired)

Customer and Product full-CRUD classes on SQL Server (DLG supports the same CRUD surface as Shipper).
```

- [ ] **Step 2: Run a short benchmark smoke run (Docker required)**

Run: `dotnet run -c Release --project benchmarks/Inquiry.Benchmarks.SqlServer -- --filter "*ShipperBenchmarks*" --job dry --inProcess`
Expected: completes without exception; the summary table lists a `*_Dlg` row in each Shipper
category alongside the other legs. Then spot-check the extras:

Run: `dotnet run -c Release --project benchmarks/Inquiry.Benchmarks.SqlServer -- --filter "*ProductReadBenchmarks*" "*EagerLoadingBenchmarks*" --job dry --inProcess`
Expected: completes; `*_Dlg` rows appear under Count, OffsetPage, Search, and EagerParentChildren.
If `--inProcess` is not recognized by this BenchmarkDotNet version, drop it (the `--job dry` run
still validates execution); shared-container reuse only matters for full measurement runs.

- [ ] **Step 3: Run the full test suite for the touched test project**

Run: `dotnet test tests/Inquiry.SqlServer.Tests/Inquiry.SqlServer.Tests.csproj -c Release -f net8.0`
Expected: all pass (DLG smoke tests + existing tests); DLG tests skip only if Docker is down.

- [ ] **Step 4: Flip the spec status**

In `docs/superpowers/specs/2026-06-04-dlg-benchmark-integration-design.md`, change the
`**Status:**` line to:

```markdown
**Status:** Implemented (Phase 1)
```

- [ ] **Step 5: Commit**

```bash
git add benchmarks/Inquiry.Benchmarks.SqlServer/DLG-PARITY.md docs/superpowers/specs/2026-06-04-dlg-benchmark-integration-design.md
git commit -m "docs(dlg): add DLG parity recap; mark Phase 1 implemented

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Self-review notes

- **Spec coverage:** build/multi-target/packages (T1), slnx+ref (T2), DlgSetup procs+config (T3),
  seeding Categories+Products (T6), Shipper CRUD leg (T7), Count/OffsetPage/Search (T9), eager
  parent-children (T9), recap with NotSupported/N/A (T10), proc/DDL-fit + Update gates (T4–T5).
- **Provider-name fix** (`Microsoft.Data.SqlClient`) is in `DlgSetup.PrimeConfig` (T3).
- **Static-config ordering**: `PrimeConfig` runs before the first DLG call in both the fixture (T4)
  and the benchmark DB (T6).
- **Known confirm-on-execute items** (called out inline, not placeholders): DLG `Category` child
  nav property name (`ProductsUsingCategoryID`) and Inquiry `Category.Products` nav name — both
  verified against the model/base file during T5/T9 and fixed if different.
- **EF for extras**: included for Count/OffsetPage/Search via a new `SqlServerProductContext` (T8);
  omitted for eager per the core `EagerLoadingBenchmarks` precedent.
```
