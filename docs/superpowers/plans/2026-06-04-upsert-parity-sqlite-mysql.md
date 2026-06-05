# Generated-key Upsert Parity (SQLite + MySQL) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Bring SQLite + MySQL generated-key upsert to demonstrated parity with the Phase 1 engines, and make MySQL support a **database-generated GUID key** (`UpsertReturningAsync(Id = null)` returns a DB-generated GUID) via server-side `UUID()`.

**Architecture:** SQLite + the MySQL *integer* path already work — add tests. The MySQL *GUID* path is new: the builder emits `COALESCE(@key, UUID())` (non-returning) and a `SET @var = COALESCE(@key, UUID()); INSERT … VALUES(@var,…); SELECT … WHERE key = @var` batch (returning), and the connection factory enables `AllowUserVariables=true` so MySqlConnector honors the `@var`.

**Tech Stack:** C# source generator (Roslyn), xUnit + `SkippableFact`, Testcontainers (`mysql:8.4`), Microsoft.Data.Sqlite (shared in-memory), MySqlConnector.

**Spec:** `docs/superpowers/specs/2026-06-04-upsert-parity-sqlite-mysql-design.md`

---

## File Structure

**Tests (new):**
- `tests/Inquiry.MySql.Tests/Fixtures/GeneratedItem.cs` — integer `AUTO_INCREMENT` fixture entity.
- `tests/Inquiry.MySql.Tests/Fixtures/GeneratedItemStore.cs` — its store.
- `tests/Inquiry.MySql.Tests/GeneratedKeyUpsertTests.cs` — integer generated-key live tests.
- `tests/Inquiry.MySql.Tests/Fixtures/GuidItem.cs` — `Guid?` `UseDatabaseDefault` fixture entity.
- `tests/Inquiry.MySql.Tests/Fixtures/GuidItemStore.cs` — its store.
- `tests/Inquiry.MySql.Tests/GuidKeyUpsertTests.cs` — GUID generated-key live tests.

**Tests (modified):**
- `tests/Inquiry.Sqlite.Tests/Fixtures/GeneratedItemStore.cs` — add `SelectAllAsync`.
- `tests/Inquiry.Sqlite.Tests/GeneratedKeyUpsertIntegrationTests.cs` — add one single-row test.
- `tests/Inquiry.Generators.Tests/InquiryGeneratorTests.cs` — add MySQL GUID-key emission test.

**Production (modified):**
- `src/Inquiry.MySql.Analyzer/MySqlSqlBuilder.cs` — GUID-key upsert branch.
- `src/Inquiry.MySql/MySqlInquiryConnectionFactory.cs` — enable `AllowUserVariables`.

**Docs (modified):**
- `docs/site/develop/roadmap.md`, `docs/site/articles/features/crud.md`.

---

## Task 1: SQLite delta — single-row guarantee (tests only)

**Files:**
- Modify: `tests/Inquiry.Sqlite.Tests/Fixtures/GeneratedItemStore.cs`
- Test: `tests/Inquiry.Sqlite.Tests/GeneratedKeyUpsertIntegrationTests.cs`

- [ ] **Step 1: Add `SelectAllAsync` to the store**

Replace the entire contents of `tests/Inquiry.Sqlite.Tests/Fixtures/GeneratedItemStore.cs` with:

```csharp
using Inquiry.Stores;

namespace Inquiry.Sqlite.Tests.Fixtures;

public partial class GeneratedItemStore : InquiryStore<GeneratedItem>
{

    [InquirySelectOneByKey]
    public partial Task<GeneratedItem?> SelectByKeyAsync(int? id, CancellationToken cancellationToken = default);

    [InquiryUpsert(ReturnEntity = true)]
    public partial Task<GeneratedItem?> UpsertReturningAsync(GeneratedItem item, CancellationToken cancellationToken = default);

    [InquirySelectAll]
    public partial Task<IReadOnlyList<GeneratedItem>> SelectAllAsync(CancellationToken cancellationToken = default);
}
```

- [ ] **Step 2: Write the failing test**

Append this method inside the `GeneratedKeyUpsertIntegrationTests` class in
`tests/Inquiry.Sqlite.Tests/GeneratedKeyUpsertIntegrationTests.cs` (before the closing brace):

```csharp
    [Fact]
    public async Task ExplicitKeyUpsertInsertsThenUpdatesLeavingOneRow()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Schemas.GeneratedItem, "GeneratedKeyUpsert");
        var store = harness.GetRequiredService<GeneratedItemStore>();

        await store.UpsertReturningAsync(new GeneratedItem { Id = 5, Name = "A" });
        await store.UpsertReturningAsync(new GeneratedItem { Id = 5, Name = "B" });

        var all = await store.SelectAllAsync();
        Assert.Single(all);
        Assert.Equal("B", all[0].Name);
    }
```

- [ ] **Step 3: Run the test to verify it passes**

Run: `dotnet test tests/Inquiry.Sqlite.Tests/Inquiry.Sqlite.Tests.csproj --filter "FullyQualifiedName~ExplicitKeyUpsertInsertsThenUpdatesLeavingOneRow"`
Expected: PASS (1 passed). The generator emits `SelectAllAsync` from the new attribute; the upsert is `INSERT … ON CONFLICT`, so the second upsert updates rather than duplicates.

- [ ] **Step 4: Run the full SQLite suite to confirm no regressions**

Run: `dotnet test tests/Inquiry.Sqlite.Tests/Inquiry.Sqlite.Tests.csproj`
Expected: PASS (all green, 0 failed).

- [ ] **Step 5: Commit**

```bash
git add tests/Inquiry.Sqlite.Tests/Fixtures/GeneratedItemStore.cs tests/Inquiry.Sqlite.Tests/GeneratedKeyUpsertIntegrationTests.cs
git commit -m "test(sqlite): assert generated-key upsert leaves exactly one row"
```

---

## Task 2: MySQL integer generated-key tests (tests only)

**Files:**
- Create: `tests/Inquiry.MySql.Tests/Fixtures/GeneratedItem.cs`
- Create: `tests/Inquiry.MySql.Tests/Fixtures/GeneratedItemStore.cs`
- Test: `tests/Inquiry.MySql.Tests/GeneratedKeyUpsertTests.cs`

> **Note on MySQL test execution:** these are `[SkippableFact]` tests that run against a live `mysql:8.4`
> Testcontainer. Docker is available in this environment, so they run (not skip). To make a Docker failure
> fail loudly instead of silently skipping, set the env var before running. PowerShell:
> `$env:INQUIRY_REQUIRE_DOCKER='1'`. Bash: `export INQUIRY_REQUIRE_DOCKER=1`.

- [ ] **Step 1: Create the fixture entity**

Create `tests/Inquiry.MySql.Tests/Fixtures/GeneratedItem.cs`:

```csharp
using Inquiry.Entities;

namespace Inquiry.MySql.Tests.Fixtures;

[InquiryTable("TGeneratedItem")]
public sealed class GeneratedItem
{
    [InquiryKey("Id", IsGenerated = true)]
    public long? Id { get; set; }

    [InquiryColumn]
    public string Name { get; set; } = string.Empty;
}
```

- [ ] **Step 2: Create the store**

Create `tests/Inquiry.MySql.Tests/Fixtures/GeneratedItemStore.cs`:

```csharp
using Inquiry.Stores;

namespace Inquiry.MySql.Tests.Fixtures;

public partial class GeneratedItemStore : InquiryStore<GeneratedItem>
{
    [InquiryUpsert] public partial Task<int> UpsertAsync(GeneratedItem item, CancellationToken ct = default);
    [InquiryUpsert(ReturnEntity = true)] public partial Task<GeneratedItem?> UpsertReturningAsync(GeneratedItem item, CancellationToken ct = default);
    [InquirySelectOneByKey] public partial Task<GeneratedItem?> SelectByKeyAsync(long? id, CancellationToken ct = default);
    [InquirySelectAll] public partial Task<IReadOnlyList<GeneratedItem>> SelectAllAsync(CancellationToken ct = default);
}
```

- [ ] **Step 3: Write the tests**

Create `tests/Inquiry.MySql.Tests/GeneratedKeyUpsertTests.cs`:

```csharp
using Inquiry.MySql.Tests.Fixtures;

namespace Inquiry.MySql.Tests;

[Collection(MySqlCollection.Name)]
public sealed class GeneratedKeyUpsertTests
{
    private readonly MySqlContainerFixture _fixture;
    public GeneratedKeyUpsertTests(MySqlContainerFixture fixture) => _fixture = fixture;

    private const string Ddl = """
        CREATE TABLE TGeneratedItem (
            Id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
            Name VARCHAR(100) NOT NULL
        );
        """;

    [SkippableFact]
    public async Task NullKeyLetsDatabaseGenerateTheKey()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MySqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "gen_key_gen");
        var store = harness.GetRequiredService<GeneratedItemStore>();

        var saved = await store.UpsertReturningAsync(new GeneratedItem { Id = null, Name = "A" });
        Assert.NotNull(saved);
        Assert.NotNull(saved!.Id);
        Assert.True(saved.Id!.Value > 0);
    }

    [SkippableFact]
    public async Task ConcurrentUpsertsOfSameExplicitKeyAllSucceed()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MySqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "gen_key_conc");
        var store = harness.GetRequiredService<GeneratedItemStore>();

        const int parallelism = 10;
        var inputs = Enumerable.Range(0, parallelism).Select(i => new GeneratedItem { Id = 5, Name = "Co_" + i }).ToArray();

        await Task.WhenAll(inputs.Select(c => store.UpsertAsync(c)));

        var all = await store.SelectAllAsync();
        Assert.Single(all);
        Assert.Contains(all[0].Name, inputs.Select(i => i.Name));
    }
}
```

- [ ] **Step 4: Run the new tests (live)**

Run (PowerShell): `$env:INQUIRY_REQUIRE_DOCKER='1'; dotnet test tests/Inquiry.MySql.Tests/Inquiry.MySql.Tests.csproj --filter "FullyQualifiedName~GeneratedKeyUpsertTests"`
Expected: PASS (2 passed, 0 skipped). `NullKeyLetsDatabaseGenerateTheKey` confirms `AUTO_INCREMENT` → `LAST_INSERT_ID()` returning works; `ConcurrentUpsertsOfSameExplicitKeyAllSucceed` confirms `ON DUPLICATE KEY UPDATE` is atomic.

- [ ] **Step 5: Commit**

```bash
git add tests/Inquiry.MySql.Tests/Fixtures/GeneratedItem.cs tests/Inquiry.MySql.Tests/Fixtures/GeneratedItemStore.cs tests/Inquiry.MySql.Tests/GeneratedKeyUpsertTests.cs
git commit -m "test(mysql): prove integer generated-key upsert (generate + concurrency)"
```

---

## Task 3: MySQL builder — DB-generated GUID key (TDD via emission test)

**Files:**
- Test: `tests/Inquiry.Generators.Tests/InquiryGeneratorTests.cs`
- Modify: `src/Inquiry.MySql.Analyzer/MySqlSqlBuilder.cs`

- [ ] **Step 1: Write the failing emission test**

Append this method inside the test class in `tests/Inquiry.Generators.Tests/InquiryGeneratorTests.cs`
(place it next to the other MySQL emission tests, before the class's closing brace):

```csharp
    [Fact]
    public void MySqlGeneratedGuidKeyUpsertUsesServerSideUuidUserVariable()
    {
        // A database-generated GUID key (UseDatabaseDefault) cannot use LAST_INSERT_ID() (that only
        // tracks AUTO_INCREMENT). The builder generates the GUID server-side via UUID(), captured in a
        // @_inquiry_genkey user variable, so the emulated returning SELECT can read the row back by it.
        const string source = """
            using System;
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry;
            using Inquiry.Entities;
            using Inquiry.Stores;

            namespace Demo;

            [InquiryTable("TGuidItem")]
            public sealed class GuidItem
            {
                [InquiryKey("Id", UseDatabaseDefault = true)]
                public Guid? Id { get; set; }

                [InquiryColumn]
                public string Name { get; set; } = string.Empty;
            }

            public partial class GuidItemStore : InquiryStore<GuidItem>
            {
                [InquiryUpsert]
                public partial Task<int> UpsertAsync(GuidItem g, CancellationToken cancellationToken = default);

                [InquiryUpsert(ReturnEntity = true)]
                public partial Task<GuidItem?> UpsertReturningAsync(GuidItem g, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source, dialect: "MySql");
        var errors = result.Compilation.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error).ToArray();

        Assert.Empty(result.GeneratorDiagnostics);
        Assert.Empty(result.RunResult.Diagnostics);
        Assert.Empty(errors);

        var generatedStore = Assert.Single(
            result.RunResult.GeneratedTrees,
            static tree => tree.FilePath.EndsWith("GuidItemStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var generatedText = generatedStore.GetText().ToString();

        // Non-returning: COALESCE(@Id, UUID()) supplies the key (explicit passes through, null generates).
        Assert.Contains("private const string _sqlUpsert = \"INSERT INTO `TGuidItem` (`Id`, `Name`) VALUES (COALESCE(@Id, UUID()), @Name) ON DUPLICATE KEY UPDATE `Name` = VALUES(`Name`)\";", generatedText);
        // Returning: capture the key in a user variable, then SELECT the row back by it.
        Assert.Contains("private const string _sqlUpsertReturning = \"SET @_inquiry_genkey = COALESCE(@Id, UUID()); INSERT INTO `TGuidItem` (`Id`, `Name`) VALUES (@_inquiry_genkey, @Name) ON DUPLICATE KEY UPDATE `Name` = VALUES(`Name`); SELECT `Id`, `Name` FROM `TGuidItem` WHERE `Id` = @_inquiry_genkey\";", generatedText);
        // LAST_INSERT_ID() is only for AUTO_INCREMENT — it must NOT appear for a GUID key.
        Assert.DoesNotContain("LAST_INSERT_ID", generatedText);
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/Inquiry.Generators.Tests/Inquiry.Generators.Tests.csproj --filter "FullyQualifiedName~MySqlGeneratedGuidKeyUpsertUsesServerSideUuidUserVariable"`
Expected: FAIL — the current builder emits `VALUES (@Id, @Name)` and a `LAST_INSERT_ID()`-based returning batch, so the `COALESCE(@Id, UUID())` / `SET @_inquiry_genkey` assertions don't match.

- [ ] **Step 3: Implement the builder change**

In `src/Inquiry.MySql.Analyzer/MySqlSqlBuilder.cs`:

(a) Replace the `BuildUpsertSql` method (currently lines ~70-79) with:

```csharp
    public override string BuildUpsertSql(SqlBuildContext context)
    {
        if (DatabaseSuppliesGuidKey(context))
        {
            return BuildGuidKeyUpsertSql(context);
        }

        if (DatabaseMaySupplyKey(context))
        {
            return BuildGeneratedKeyUpsertSql(context);
        }

        return "INSERT INTO " + context.Table + " (" + context.InsertColumns + ") VALUES (" + context.InsertParameters + ") " +
            "ON DUPLICATE KEY UPDATE " + OnDuplicateKeyAssignments(context);
    }
```

(b) Replace the `BuildUpsertReturningSql` method (currently lines ~81-93) with:

```csharp
    public override string BuildUpsertReturningSql(SqlBuildContext context)
    {
        if (DatabaseSuppliesGuidKey(context))
        {
            return BuildGuidKeyUpsertReturningSql(context);
        }

        if (DatabaseMaySupplyKey(context))
        {
            // The trailing returning SELECT reads the row back via LAST_INSERT_ID(). On the INSERT branch
            // that is the freshly generated key; on the ON DUPLICATE KEY UPDATE branch no auto-increment
            // fires, so the upsert sets it explicitly with `key = LAST_INSERT_ID(key)` (the standard MySQL
            // trick) — LAST_INSERT_ID() then returns the existing row's key, so the SELECT finds it.
            return BuildGeneratedKeyUpsertSql(context, echoKeyForReturning: true) + "; " + BuildReturningSelect(context);
        }

        return BuildUpsertSql(context) + "; SELECT " + context.SelectColumns + " FROM " + context.Table + " WHERE " + context.KeyWhereClause;
    }
```

(c) Add these new members inside the `MySqlSqlBuilder` class — place them after the existing
`BuildReturningSelect` method (ends ~line 125) and before the `OnDuplicateKeyAssignments` comment block
(~line 127). The `@_inquiry_genkey` user-variable name is deliberately distinctive to avoid colliding with
any column or parameter:

```csharp
    /// <summary>The session user variable holding the (generated or explicit) GUID key for the returning batch.</summary>
    private const string GeneratedGuidKeyVariable = "@_inquiry_genkey";

    /// <summary>
    /// True when the single key is a database-supplied GUID. MySQL's LAST_INSERT_ID() returning trick only
    /// works for AUTO_INCREMENT, so a GUID key generated by the database needs a different mechanism.
    /// </summary>
    private static bool DatabaseSuppliesGuidKey(SqlBuildContext context)
        => DatabaseMaySupplyKey(context) && context.KeyColumns[0].TypeClass == DbTypeClass.Guid;

    // Non-returning GUID-key upsert: COALESCE(@key, UUID()) lets an explicit key pass through and a null
    // key be generated server-side. No user variable is needed because nothing is read back.
    private string BuildGuidKeyUpsertSql(SqlBuildContext context)
    {
        var keyColumn = context.QuotedKeyColumns[0];
        var keyValue = "COALESCE(" + context.KeyParameters[0] + ", UUID())";
        var insertColumns = JoinSql(keyColumn, context.InsertColumns);
        var insertValues = JoinSql(keyValue, context.InsertParameters);

        return "INSERT INTO " + context.Table + " (" + insertColumns + ") VALUES (" + insertValues + ") " +
            "ON DUPLICATE KEY UPDATE " + OnDuplicateKeyAssignments(context);
    }

    // Returning GUID-key upsert: capture the (generated or explicit) key in a user variable so the trailing
    // SELECT can read the new/updated row back by it. Requires AllowUserVariables on the connection.
    private string BuildGuidKeyUpsertReturningSql(SqlBuildContext context)
    {
        var keyColumn = context.QuotedKeyColumns[0];
        var insertColumns = JoinSql(keyColumn, context.InsertColumns);
        var insertValues = JoinSql(GeneratedGuidKeyVariable, context.InsertParameters);

        return "SET " + GeneratedGuidKeyVariable + " = COALESCE(" + context.KeyParameters[0] + ", UUID()); " +
            "INSERT INTO " + context.Table + " (" + insertColumns + ") VALUES (" + insertValues + ") " +
            "ON DUPLICATE KEY UPDATE " + OnDuplicateKeyAssignments(context) + "; " +
            "SELECT " + context.SelectColumns + " FROM " + context.Table + " WHERE " + keyColumn + " = " + GeneratedGuidKeyVariable;
    }
```

(`DbTypeClass` is already in scope via `using Inquiry.Generators.Abstractions;` at the top of the file.)

- [ ] **Step 4: Run the new emission test to verify it passes**

Run: `dotnet test tests/Inquiry.Generators.Tests/Inquiry.Generators.Tests.csproj --filter "FullyQualifiedName~MySqlGeneratedGuidKeyUpsertUsesServerSideUuidUserVariable"`
Expected: PASS (1 passed).

- [ ] **Step 5: Run the full generator suite (regression guard for unchanged paths)**

Run: `dotnet test tests/Inquiry.Generators.Tests/Inquiry.Generators.Tests.csproj`
Expected: PASS (all green). In particular `MySqlDialectEmitsLastInsertIdReturningForGeneratedKey` (integer key) and `MySqlDialectEmitsBacktickIdentifiersAndOnDuplicateKeyUpsertWithEmulatedReturning` (client key) must still pass — the new GUID branch only triggers for a `Guid` `DatabaseMaySupplyKey` key, leaving those paths byte-identical.

- [ ] **Step 6: Commit**

```bash
git add src/Inquiry.MySql.Analyzer/MySqlSqlBuilder.cs tests/Inquiry.Generators.Tests/InquiryGeneratorTests.cs
git commit -m "feat(mysql): emit server-side UUID() upsert for DB-generated GUID keys"
```

---

## Task 4: MySQL connection factory — enable AllowUserVariables

**Files:**
- Modify: `src/Inquiry.MySql/MySqlInquiryConnectionFactory.cs`

- [ ] **Step 1: Normalize the connection string in the constructor**

In `src/Inquiry.MySql/MySqlInquiryConnectionFactory.cs`, replace the constructor body's assignment
`_connectionString = connectionString;` so the full constructor reads:

```csharp
    public MySqlInquiryConnectionFactory(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string cannot be empty.", nameof(connectionString));
        }

        // Inquiry's emulated RETURNING for a database-generated GUID key captures the value in a
        // @_inquiry_genkey user variable; MySqlConnector only treats an unmatched @name as a user
        // variable when AllowUserVariables is enabled (otherwise it throws). All Inquiry SQL is
        // compile-time-constant text with bound parameters, so enabling this is safe.
        _connectionString = new MySqlConnectionStringBuilder(connectionString)
        {
            AllowUserVariables = true,
        }.ConnectionString;
    }
```

(`MySqlConnectionStringBuilder` is in `MySqlConnector`, already imported at the top of the file.)

- [ ] **Step 2: Build the MySQL runtime project to verify it compiles**

Run: `dotnet build src/Inquiry.MySql/Inquiry.MySql.csproj`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add src/Inquiry.MySql/MySqlInquiryConnectionFactory.cs
git commit -m "feat(mysql): enable AllowUserVariables for generated-GUID-key returning"
```

---

## Task 5: MySQL GUID generated-key live tests

**Files:**
- Create: `tests/Inquiry.MySql.Tests/Fixtures/GuidItem.cs`
- Create: `tests/Inquiry.MySql.Tests/Fixtures/GuidItemStore.cs`
- Test: `tests/Inquiry.MySql.Tests/GuidKeyUpsertTests.cs`

- [ ] **Step 1: Create the fixture entity**

Create `tests/Inquiry.MySql.Tests/Fixtures/GuidItem.cs`:

```csharp
using Inquiry.Entities;

namespace Inquiry.MySql.Tests.Fixtures;

[InquiryTable("TGuidItem")]
public sealed class GuidItem
{
    [InquiryKey("Id", UseDatabaseDefault = true)] public Guid? Id { get; set; }
    [InquiryColumn] public string Name { get; set; } = string.Empty;
}
```

- [ ] **Step 2: Create the store**

Create `tests/Inquiry.MySql.Tests/Fixtures/GuidItemStore.cs`:

```csharp
using Inquiry.Stores;

namespace Inquiry.MySql.Tests.Fixtures;

public partial class GuidItemStore : InquiryStore<GuidItem>
{
    [InquiryUpsert] public partial Task<int> UpsertAsync(GuidItem item, CancellationToken ct = default);
    [InquiryUpsert(ReturnEntity = true)] public partial Task<GuidItem?> UpsertReturningAsync(GuidItem item, CancellationToken ct = default);
    [InquirySelectOneByKey] public partial Task<GuidItem?> SelectByKeyAsync(Guid? id, CancellationToken ct = default);
    [InquirySelectAll] public partial Task<IReadOnlyList<GuidItem>> SelectAllAsync(CancellationToken ct = default);
}
```

- [ ] **Step 3: Write the tests**

Create `tests/Inquiry.MySql.Tests/GuidKeyUpsertTests.cs`:

```csharp
using Inquiry.MySql.Tests.Fixtures;

namespace Inquiry.MySql.Tests;

[Collection(MySqlCollection.Name)]
public sealed class GuidKeyUpsertTests
{
    private readonly MySqlContainerFixture _fixture;
    public GuidKeyUpsertTests(MySqlContainerFixture fixture) => _fixture = fixture;

    private const string Ddl = """
        CREATE TABLE TGuidItem (
            Id CHAR(36) NOT NULL DEFAULT (UUID()) PRIMARY KEY,
            Name VARCHAR(100) NOT NULL
        );
        """;

    [SkippableFact]
    public async Task NullKeyLetsDatabaseGenerateTheGuid()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MySqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "guid_gen");
        var store = harness.GetRequiredService<GuidItemStore>();

        var saved = await store.UpsertReturningAsync(new GuidItem { Id = null, Name = "A" });
        Assert.NotNull(saved);
        Assert.NotNull(saved!.Id);
        Assert.NotEqual(Guid.Empty, saved.Id!.Value);
    }

    [SkippableFact]
    public async Task ConcurrentUpsertsOfSameExplicitKeyAllSucceed()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MySqlTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "guid_conc");
        var store = harness.GetRequiredService<GuidItemStore>();

        var key = Guid.NewGuid();
        const int parallelism = 10;
        var inputs = Enumerable.Range(0, parallelism).Select(i => new GuidItem { Id = key, Name = "Co_" + i }).ToArray();

        await Task.WhenAll(inputs.Select(c => store.UpsertAsync(c)));

        var all = await store.SelectAllAsync();
        Assert.Single(all);
        Assert.Contains(all[0].Name, inputs.Select(i => i.Name));
    }
}
```

- [ ] **Step 4: Run the GUID tests (live)**

Run (PowerShell): `$env:INQUIRY_REQUIRE_DOCKER='1'; dotnet test tests/Inquiry.MySql.Tests/Inquiry.MySql.Tests.csproj --filter "FullyQualifiedName~GuidKeyUpsertTests"`
Expected: PASS (2 passed, 0 skipped). `NullKeyLetsDatabaseGenerateTheGuid` proves the server-side `UUID()`
+ user-variable returning works end-to-end (and that `CHAR(36)` round-trips to `Guid` — see the GuidFormat
note below). `ConcurrentUpsertsOfSameExplicitKeyAllSucceed` proves the explicit-GUID path is atomic.

> **If `NullKeyLetsDatabaseGenerateTheGuid` fails on the `Guid` round-trip** (e.g. the returned `Id` is
> `Guid.Empty` or a format error reading `CHAR(36)`): add `GuidFormat = MySqlGuidFormat.Char36` next to
> `AllowUserVariables = true` in `MySqlInquiryConnectionFactory` (Task 4), then re-run. This is the
> documented fallback in the spec (§5/§7). Re-commit Task 4 if changed.

- [ ] **Step 5: Run the full MySQL suite to confirm no regressions**

Run (PowerShell): `$env:INQUIRY_REQUIRE_DOCKER='1'; dotnet test tests/Inquiry.MySql.Tests/Inquiry.MySql.Tests.csproj`
Expected: PASS (all green, 0 skipped). Existing `UpsertConcurrencyTests` and `DefaultedColumnUpsertTests`
still pass (the factory now enables `AllowUserVariables`, which does not affect their parameterized SQL).

- [ ] **Step 6: Commit**

```bash
git add tests/Inquiry.MySql.Tests/Fixtures/GuidItem.cs tests/Inquiry.MySql.Tests/Fixtures/GuidItemStore.cs tests/Inquiry.MySql.Tests/GuidKeyUpsertTests.cs
git commit -m "test(mysql): prove DB-generated GUID key upsert (generate + concurrency)"
```

---

## Task 6: Docs — record MySQL GUID support + parity

**Files:**
- Modify: `docs/site/develop/roadmap.md`
- Modify: `docs/site/articles/features/crud.md`

- [ ] **Step 1: Update the Roadmap "Recently resolved" upsert bullet**

In `docs/site/develop/roadmap.md`, find the bullet that begins
`**Upsert atomicity (SQL Server + PostgreSQL):**` and replace that whole bullet with:

```markdown
- **Upsert atomicity & generated-key parity (all relational engines except Oracle):** generated-key upserts
  are atomic — SQL Server uses `MERGE … WITH (HOLDLOCK)` (client and generated key), PostgreSQL uses
  `INSERT … ON CONFLICT` — so concurrent same-key upserts no longer throw a spurious duplicate-key error;
  covered by live concurrency + `uniqueidentifier`/`gen_random_uuid()` key tests. SQLite + MySQL parity is
  now **test-proven** (live generate + concurrency tests). MySQL additionally supports a **database-generated
  GUID key**: a `Guid?` `UseDatabaseDefault` key is generated server-side via `UUID()` (captured in a
  `@_inquiry_genkey` user variable for the emulated returning), so Inquiry enables `AllowUserVariables=true`
  on MySQL connections by default. (Oracle generated-key upsert remains unsupported, tracked separately.)
```

- [ ] **Step 2: Update the crud.md upsert-concurrency table (MySQL row)**

In `docs/site/articles/features/crud.md`, in the "Upsert concurrency semantics" table, replace the **MySQL**
row (the line starting `| MySQL |`) with:

```markdown
| MySQL | `INSERT ... ON DUPLICATE KEY UPDATE` — single statement, atomic | Integer `AUTO_INCREMENT` key: same `ON DUPLICATE KEY UPDATE` with `LAST_INSERT_ID(key)` echo — atomic. GUID key (`UseDatabaseDefault`): generated server-side via `COALESCE(@key, UUID())`, captured in a `@_inquiry_genkey` user variable so the emulated returning can read it back — atomic. |
```

- [ ] **Step 3: Add a short note under the table about GUID keys on MySQL**

In `docs/site/articles/features/crud.md`, immediately after the paragraph that ends
`... wrap the upsert in an explicit transaction with an appropriate isolation level (\`SERIALIZABLE\`, or \`READ COMMITTED\` plus an advisory lock).` add a new paragraph:

```markdown
On **MySQL**, a database-generated GUID key (a `Guid?` property with `UseDatabaseDefault = true`, e.g. a
`CHAR(36) DEFAULT (UUID())` column) is supported: because MySQL has no `RETURNING` and `LAST_INSERT_ID()`
only tracks `AUTO_INCREMENT`, Inquiry generates the value server-side with `UUID()`, captures it in a
`@_inquiry_genkey` user variable, and selects the row back by it. Inquiry therefore enables
`AllowUserVariables=true` on MySQL connections automatically.
```

- [ ] **Step 4: Verify the docs build (optional but preferred)**

Run: `docfx docs/site/docfx.json`
Expected: Build succeeds with no new warnings about the edited files. (If `docfx` is not installed in the
environment, skip this step — the edits are plain Markdown within existing files.)

- [ ] **Step 5: Commit**

```bash
git add docs/site/develop/roadmap.md docs/site/articles/features/crud.md
git commit -m "docs: record MySQL DB-generated GUID upsert support and SQLite/MySQL parity"
```

---

## Final verification (after all tasks)

- [ ] **Run the three affected test suites** and confirm all green, 0 skipped on MySQL:

```
dotnet test tests/Inquiry.Generators.Tests/Inquiry.Generators.Tests.csproj
dotnet test tests/Inquiry.Sqlite.Tests/Inquiry.Sqlite.Tests.csproj
$env:INQUIRY_REQUIRE_DOCKER='1'; dotnet test tests/Inquiry.MySql.Tests/Inquiry.MySql.Tests.csproj
```

- [ ] **Confirm success criteria** from the spec §6: SQLite single-row test green; MySQL integer + GUID
  generate/concurrency green live; factory enables `AllowUserVariables`; emission asserts the new GUID SQL
  while integer/client + other dialects stay byte-identical; roadmap + crud.md updated.
```
