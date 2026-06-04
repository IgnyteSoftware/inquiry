# Atomic Generated-Key Upsert (Phase 1: PostgreSQL + SQL Server) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make SQL Server and PostgreSQL upserts atomic so concurrent same-key upserts can't throw a spurious duplicate-key error — including for `uniqueidentifier DEFAULT NEWSEQUENTIALID()` / `gen_random_uuid()` keys.

**Architecture:** SQL Server: add `WITH (HOLDLOCK)` to the client-key MERGE and replace the generated-key `IF EXISTS … UPDATE … ELSE INSERT` with a `MERGE … WITH (HOLDLOCK)` on the explicit key. PostgreSQL: replace the generated-key explicit `UPDATE` + `INSERT … WHERE NOT EXISTS` with a single `INSERT … ON CONFLICT (key) DO UPDATE` (non-returning = two `WHERE @key IS NULL/NOT NULL` statements; returning = a `ins_gen` / `ins_upsert` data-modifying CTE). Null-key generate branch stays a plain INSERT (race-free). New live concurrency + GUID-default-key tests.

**Tech Stack:** Roslyn source generator (C# builders), xUnit, Testcontainers (Docker is available locally — live tests run here).

**Branch:** `fix/upsert-atomicity` (already created; spec at `docs/superpowers/specs/2026-06-04-generated-key-upsert-atomicity-design.md`).

**Ground truth (verified):**
- `SqlServerSqlBuilder` (`src/Inquiry.SqlServer.Analyzer/SqlServerSqlBuilder.cs`): client-key MERGE at ~84-104 (`MERGE INTO <table> AS target …`, no HOLDLOCK); `BuildGeneratedKeyUpsertSql` at ~153-177 (`IF @key IS NULL … ELSE IF EXISTS … UPDATE … ELSE INSERT`). Has helpers `InsertedColumns`, `BuildSourceSelect`, `BuildSourceJoin`, `JoinSql`; `output = returning ? " OUTPUT " + InsertedColumns(context) : ""`.
- `PostgreSqlSqlBuilder` (`src/Inquiry.PostgreSql.Analyzer/PostgreSqlSqlBuilder.cs`): client-key `INSERT … ON CONFLICT` at ~77-78 (unchanged); `BuildGeneratedKeyUpsertSql` at ~91-126. Has `JoinKeyColumns`, `JoinSql`.
- Client-key emission tests: `tests/Inquiry.Generators.Tests/InquiryGeneratorTests.cs` — `SqlServerDialectEmitsBracketedIdentifiersAndMergeUpsert` (asserts `MERGE INTO [TOrganization] AS target`, ~line 1104) and `PostgreSqlDialectEmitsDoubleQuotedIdentifiersAndOnConflictUpsert` (~1109). **No** generated-key upsert emission test exists yet.
- Test harnesses expose `CreateFromDdlAsync(adminConnectionString, ddl, namePrefix)` (SQL Server + PostgreSQL) → throwaway DB from custom DDL + a DI `ServiceProvider`. Fixture model: `tests/Inquiry.MySql.Tests/Fixtures/DefaultedItem.cs` + `DefaultedItemStore.cs` + `DefaultedColumnUpsertTests.cs`.
- Existing concurrency tests: `tests/Inquiry.{SqlServer,PostgreSql}.Tests/UpsertConcurrencyTests.cs` (client-key only; SQL Server tolerates a `DbException`).

---

## File Structure

**Modify (production):**
- `src/Inquiry.SqlServer.Analyzer/SqlServerSqlBuilder.cs` — HOLDLOCK on client-key MERGE; atomic generated-key MERGE.
- `src/Inquiry.PostgreSql.Analyzer/PostgreSqlSqlBuilder.cs` — atomic generated-key ON CONFLICT.

**Modify (tests/docs):**
- `tests/Inquiry.Generators.Tests/InquiryGeneratorTests.cs` — update SQL Server client-key MERGE assertion; add generated-key emission tests (SQL Server + PostgreSQL).
- `tests/Inquiry.SqlServer.Tests/UpsertConcurrencyTests.cs` — tighten to expect all-succeed.
- `docs/site/articles/features/crud.md`, `docs/site/develop/roadmap.md`.

**Create (tests):**
- `tests/Inquiry.SqlServer.Tests/Fixtures/GuidItem.cs`, `GuidItemStore.cs`; `tests/Inquiry.SqlServer.Tests/GeneratedKeyUpsertConcurrencyTests.cs`.
- `tests/Inquiry.PostgreSql.Tests/Fixtures/GuidItem.cs`, `GuidItemStore.cs`; `tests/Inquiry.PostgreSql.Tests/GeneratedKeyUpsertConcurrencyTests.cs`.

---

## Task 1: SQL Server — HOLDLOCK client-key MERGE + atomic generated-key MERGE

**Files:** Modify `src/Inquiry.SqlServer.Analyzer/SqlServerSqlBuilder.cs`, `tests/Inquiry.Generators.Tests/InquiryGeneratorTests.cs`.

- [ ] **Step 1: Update the client-key MERGE emission assertion (red).** In `InquiryGeneratorTests.cs`, in `SqlServerDialectEmitsBracketedIdentifiersAndMergeUpsert`, change the assertion:
  ```csharp
  Assert.Contains("MERGE INTO [TOrganization] WITH (HOLDLOCK) AS target", generatedText);
  ```
  Run: `dotnet test tests/Inquiry.Generators.Tests -f net8.0 --filter "FullyQualifiedName~SqlServerDialectEmitsBracketedIdentifiersAndMergeUpsert" --nologo` → FAIL (current SQL has no `WITH (HOLDLOCK)`).

- [ ] **Step 2: Add `WITH (HOLDLOCK)` to both client-key MERGE builders.** In `SqlServerSqlBuilder.cs`, in `BuildUpsertSql` and `BuildUpsertReturningSql`, change `"MERGE INTO " + context.Table + " AS target "` to:
  ```csharp
  "MERGE INTO " + context.Table + " WITH (HOLDLOCK) AS target "
  ```
  (both occurrences — the non-returning and returning client-key paths).

- [ ] **Step 3: Replace the generated-key explicit branch with an atomic MERGE.** Replace the body of `BuildGeneratedKeyUpsertSql` (the `return "IF " + keyParameter + " IS NULL " … "END";` block) with:
  ```csharp
  return
      "IF " + keyParameter + " IS NULL " +
      "BEGIN " +
      generatedInsert +
      "END " +
      "ELSE " +
      "BEGIN " +
      "MERGE INTO " + context.Table + " WITH (HOLDLOCK) AS target " +
      "USING (SELECT " + keyParameter + " AS k0) AS source ON target." + keyColumn + " = source.k0 " +
      "WHEN MATCHED THEN UPDATE SET " + context.SetClauses + " " +
      "WHEN NOT MATCHED THEN INSERT (" + explicitInsertColumns + ") VALUES (" + explicitInsertParameters + ")" + output + "; " +
      "END";
  ```
  (Keep the existing locals `keyColumn`, `keyParameter`, `output`, `explicitInsertColumns`, `explicitInsertParameters`, `generatedInsert` exactly as they are above this block. The null-key branch is unchanged.)

- [ ] **Step 4: Run the client-key assertion (green).** Run the Step 1 command → PASS.

- [ ] **Step 5: Add a generated-key upsert emission test.** Add a new `[Fact]` in `InquiryGeneratorTests.cs` modeled on the existing dialect-emission tests (use `RunGenerator(source, dialect: "SqlServer")` and assert against the generated `…InquiryStore.g.cs` text). Source: an entity with a generated key + an upsert-returning store:
  ```csharp
  [Fact]
  public void SqlServerGeneratedKeyUpsertUsesAtomicMergeWithHoldlock()
  {
      const string source = """
          using System.Threading;
          using System.Threading.Tasks;
          using Inquiry;
          using Inquiry.Entities;
          using Inquiry.Stores;

          [assembly: InquiryDialect("SqlServer")]

          namespace Demo;

          [InquiryTable("TWidget")]
          public sealed class Widget
          {
              [InquiryKey("Id", IsGenerated = true)] public int? Id { get; set; }
              [InquiryColumn] public string Name { get; set; } = "";
          }

          public partial class WidgetStore : InquiryStore<Widget>
          {
              [InquiryUpsert(ReturnEntity = true)]
              public partial Task<Widget?> UpsertReturningAsync(Widget w, CancellationToken ct = default);
          }
          """;

      var result = RunGenerator(source, dialect: "SqlServer");
      var text = Assert.Single(result.RunResult.GeneratedTrees,
          t => t.FilePath.EndsWith("WidgetStore.InquiryStore.g.cs", System.StringComparison.Ordinal)).GetText().ToString();

      // Atomic explicit-key branch: MERGE + HOLDLOCK, no check-then-act EXISTS.
      Assert.Contains("MERGE INTO [TWidget] WITH (HOLDLOCK) AS target", text);
      Assert.DoesNotContain("ELSE IF EXISTS", text);
      // Null-key branch still generates.
      Assert.Contains("IF @Id IS NULL", text);
  }
  ```
  Adjust `RunGenerator`'s signature/usage to match the other dialect tests in this file (the controller will confirm the exact helper name + how the assembly dialect is set — some tests pass `dialect:` and omit the `[assembly:]` line). Verify the exact emitted substrings against the generated output and fix the literal strings if escaping differs.
  Run: `dotnet test tests/Inquiry.Generators.Tests -f net8.0 --filter "FullyQualifiedName~SqlServerGeneratedKeyUpsertUsesAtomicMergeWithHoldlock" --nologo` → PASS.

- [ ] **Step 6: Full generator suite (no regressions; other dialects byte-identical).** Run: `dotnet test tests/Inquiry.Generators.Tests -f net8.0 --nologo` → all pass.

- [ ] **Step 7: Commit.**
  ```bash
  git add src/Inquiry.SqlServer.Analyzer/SqlServerSqlBuilder.cs tests/Inquiry.Generators.Tests/InquiryGeneratorTests.cs
  git commit -F <bom-free-msg-file>
  ```
  Message: `fix(sqlserver): atomic upsert via MERGE + HOLDLOCK (client + generated key)`

---

## Task 2: PostgreSQL — atomic generated-key upsert via ON CONFLICT

**Files:** Modify `src/Inquiry.PostgreSql.Analyzer/PostgreSqlSqlBuilder.cs`, `tests/Inquiry.Generators.Tests/InquiryGeneratorTests.cs`.

- [ ] **Step 1: Add a generated-key emission test (red).** Add a `[Fact]` (mirror the SQL Server one, `dialect: "PostgreSql"`):
  ```csharp
  [Fact]
  public void PostgreSqlGeneratedKeyUpsertUsesOnConflict()
  {
      const string source = """
          using System.Threading;
          using System.Threading.Tasks;
          using Inquiry;
          using Inquiry.Entities;
          using Inquiry.Stores;

          [assembly: InquiryDialect("PostgreSql")]

          namespace Demo;

          [InquiryTable("TWidget")]
          public sealed class Widget
          {
              [InquiryKey("Id", IsGenerated = true)] public int? Id { get; set; }
              [InquiryColumn] public string Name { get; set; } = "";
          }

          public partial class WidgetStore : InquiryStore<Widget>
          {
              [InquiryUpsert(ReturnEntity = true)]
              public partial Task<Widget?> UpsertReturningAsync(Widget w, CancellationToken ct = default);
          }
          """;

      var result = RunGenerator(source, dialect: "PostgreSql");
      var text = Assert.Single(result.RunResult.GeneratedTrees,
          t => t.FilePath.EndsWith("WidgetStore.InquiryStore.g.cs", System.StringComparison.Ordinal)).GetText().ToString();

      Assert.Contains("ON CONFLICT", text);                 // atomic explicit-key arm
      Assert.Contains("ins_upsert AS", text);               // returning CTE arm
      Assert.DoesNotContain("NOT EXISTS", text);            // old check-then-act gone
  }
  ```
  Run the filtered test → FAIL (current SQL uses `NOT EXISTS`, no `ON CONFLICT` in the generated-key path).

- [ ] **Step 2: Rewrite `BuildGeneratedKeyUpsertSql`.** Replace the method body (keep the locals `keyColumn`, `keyParameter`, `explicitInsertColumns`, `explicitInsertParameters`, `generatedInsertColumns` as they are) so the non-returning and returning forms become:
  ```csharp
  if (!returning)
  {
      return
          "INSERT INTO " + context.Table + generatedInsertColumns + "; " +
          "INSERT INTO " + context.Table + " (" + explicitInsertColumns + ") " +
          "SELECT " + explicitInsertParameters + " WHERE " + keyParameter + " IS NOT NULL " +
          "ON CONFLICT (" + keyColumn + ") DO UPDATE SET " + context.SetClauses + ";";
  }

  return
      "WITH ins_gen AS (INSERT INTO " + context.Table + " (" + context.InsertColumns + ") " +
      "SELECT " + context.InsertParameters + " WHERE " + keyParameter + " IS NULL " +
      "RETURNING " + context.SelectColumns + "), " +
      "ins_upsert AS (INSERT INTO " + context.Table + " (" + explicitInsertColumns + ") " +
      "SELECT " + explicitInsertParameters + " WHERE " + keyParameter + " IS NOT NULL " +
      "ON CONFLICT (" + keyColumn + ") DO UPDATE SET " + context.SetClauses + " " +
      "RETURNING " + context.SelectColumns + ") " +
      "SELECT " + context.SelectColumns + " FROM ins_gen UNION ALL " +
      "SELECT " + context.SelectColumns + " FROM ins_upsert";
  ```
  (`generatedInsertColumns` already = `" (" + context.InsertColumns + ") SELECT " + context.InsertParameters + " WHERE " + keyParameter + " IS NULL"`, so the non-returning null-branch line reads `INSERT INTO t (cols) SELECT params WHERE @Id IS NULL;`.) The client-key path (`BuildUpsertSql`/`BuildUpsertReturningSql` non-generated branch) is unchanged.

- [ ] **Step 3: Run the emission test (green).** Filtered command → PASS. Fix literal substrings if escaping differs from the generated output.

- [ ] **Step 4: Full generator suite.** `dotnet test tests/Inquiry.Generators.Tests -f net8.0 --nologo` → all pass (SQLite/MySQL/Oracle unchanged; PostgreSQL client-key `ON CONFLICT` test still green).

- [ ] **Step 5: Commit.**
  ```bash
  git add src/Inquiry.PostgreSql.Analyzer/PostgreSqlSqlBuilder.cs tests/Inquiry.Generators.Tests/InquiryGeneratorTests.cs
  git commit -F <bom-free-msg-file>
  ```
  Message: `fix(postgres): atomic generated-key upsert via INSERT ... ON CONFLICT`

---

## Task 3: Live tests — GUID-default key + generated-key concurrency (SQL Server + PostgreSQL)

**Files:** Create the GUID fixtures + concurrency tests in both provider test projects; tighten the SQL Server client-key concurrency test.

- [ ] **Step 1: SQL Server GUID fixture.** Create `tests/Inquiry.SqlServer.Tests/Fixtures/GuidItem.cs`:
  ```csharp
  using System;
  using Inquiry.Entities;

  namespace Inquiry.SqlServer.Tests.Fixtures;

  [InquiryTable("TGuidItem")]
  public sealed class GuidItem
  {
      [InquiryKey("Id", UseDatabaseDefault = true)] public Guid? Id { get; set; }
      [InquiryColumn] public string Name { get; set; } = string.Empty;
  }
  ```
  And `tests/Inquiry.SqlServer.Tests/Fixtures/GuidItemStore.cs`:
  ```csharp
  using System;
  using System.Threading;
  using System.Threading.Tasks;
  using Inquiry.Stores;

  namespace Inquiry.SqlServer.Tests.Fixtures;

  public partial class GuidItemStore : InquiryStore<GuidItem>
  {
      [InquiryUpsert] public partial Task<int> UpsertAsync(GuidItem item, CancellationToken ct = default);
      [InquiryUpsert(ReturnEntity = true)] public partial Task<GuidItem?> UpsertReturningAsync(GuidItem item, CancellationToken ct = default);
      [InquirySelectOneByKey] public partial Task<GuidItem?> SelectByKeyAsync(Guid? id, CancellationToken ct = default);
      [InquirySelectAll] public partial Task<System.Collections.Generic.IReadOnlyList<GuidItem>> SelectAllAsync(CancellationToken ct = default);
  }
  ```
  (If the generator rejects `Guid? id` on `SelectByKey` for a nullable key, change it to match the key type exactly per the diagnostic — the controller resolves this against the build.)

- [ ] **Step 2: PostgreSQL GUID fixture.** Create the same two files under `tests/Inquiry.PostgreSql.Tests/Fixtures/` with namespace `Inquiry.PostgreSql.Tests.Fixtures` (identical entity + store).

- [ ] **Step 3: SQL Server live tests.** Create `tests/Inquiry.SqlServer.Tests/GeneratedKeyUpsertConcurrencyTests.cs`:
  ```csharp
  using System;
  using System.Linq;
  using System.Threading.Tasks;
  using Inquiry.SqlServer.Tests.Fixtures;

  namespace Inquiry.SqlServer.Tests;

  [Collection(SqlServerCollection.Name)]
  public sealed class GeneratedKeyUpsertConcurrencyTests
  {
      private readonly SqlServerContainerFixture _fixture;
      public GeneratedKeyUpsertConcurrencyTests(SqlServerContainerFixture fixture) => _fixture = fixture;

      private const string Ddl = """
          CREATE TABLE TGuidItem (
              Id uniqueidentifier NOT NULL DEFAULT NEWSEQUENTIALID() PRIMARY KEY,
              Name nvarchar(100) NOT NULL
          );
          """;

      [SkippableFact]
      public async Task NullKeyLetsDatabaseGenerateTheGuid()
      {
          Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
          await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "guid_gen");
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
          await using var harness = await SqlServerTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "guid_conc");
          var store = harness.GetRequiredService<GuidItemStore>();

          var key = Guid.NewGuid();
          const int parallelism = 10;
          var inputs = Enumerable.Range(0, parallelism)
              .Select(i => new GuidItem { Id = key, Name = "Co_" + i }).ToArray();

          // With MERGE + HOLDLOCK every parallel upsert of the same brand-new key succeeds (no duplicate-key error).
          await Task.WhenAll(inputs.Select(c => store.UpsertAsync(c)));

          var all = await store.SelectAllAsync();
          Assert.Single(all);
          Assert.Contains(all[0].Name, inputs.Select(i => i.Name));
      }
  }
  ```

- [ ] **Step 4: PostgreSQL live tests.** Create `tests/Inquiry.PostgreSql.Tests/GeneratedKeyUpsertConcurrencyTests.cs` — same as Step 3 but `PostgreSqlCollection`, `PostgreSqlContainerFixture`, `PostgreSqlTestHarness`, namespace `Inquiry.PostgreSql.Tests`, and DDL:
  ```sql
  CREATE TABLE "TGuidItem" (
      "Id" uuid NOT NULL DEFAULT gen_random_uuid() PRIMARY KEY,
      "Name" varchar(100) NOT NULL
  );
  ```
  (postgres:16 has `gen_random_uuid()` built in.)

- [ ] **Step 5: Tighten the SQL Server client-key concurrency test.** In `tests/Inquiry.SqlServer.Tests/UpsertConcurrencyTests.cs`, replace the try/catch-tolerating block with the all-succeed form (HOLDLOCK now prevents the race) and update the class summary:
  ```csharp
  // With MERGE + HOLDLOCK, every concurrent same-key upsert succeeds (the range lock serializes the
  // WHEN NOT MATCHED inserts), so none should throw.
  await Task.WhenAll(inputs.Select(c => store.UpsertAsync(c)));

  var loaded = await store.SelectByKeyAsync("CONC1");
  Assert.NotNull(loaded);
  Assert.Contains(loaded!.CompanyName, inputs.Select(i => i.CompanyName));
  ```
  (Remove the `results`/`Assert.Contains(true, results)` lines and the `using System.Data.Common;` if it becomes unused.)

- [ ] **Step 6: Run the live SQL Server + PostgreSQL suites (Docker is available).**
  Run: `dotnet test tests/Inquiry.SqlServer.Tests -f net8.0 --filter "FullyQualifiedName~Upsert" --nologo`
  Run: `dotnet test tests/Inquiry.PostgreSql.Tests -f net8.0 --filter "FullyQualifiedName~Upsert" --nologo`
  Expected: all upsert tests (incl. the new generated-key + GUID ones) pass; none skip (Docker up).
  If `SelectByKeyAsync(Guid? id)` failed to compile in Step 1, fix the signature per the generator diagnostic and re-run.

- [ ] **Step 7: Commit.**
  ```bash
  git add tests/Inquiry.SqlServer.Tests/Fixtures/GuidItem.cs tests/Inquiry.SqlServer.Tests/Fixtures/GuidItemStore.cs tests/Inquiry.SqlServer.Tests/GeneratedKeyUpsertConcurrencyTests.cs tests/Inquiry.SqlServer.Tests/UpsertConcurrencyTests.cs tests/Inquiry.PostgreSql.Tests/Fixtures/GuidItem.cs tests/Inquiry.PostgreSql.Tests/Fixtures/GuidItemStore.cs tests/Inquiry.PostgreSql.Tests/GeneratedKeyUpsertConcurrencyTests.cs
  git commit -F <bom-free-msg-file>
  ```
  Message: `test(upsert): live generated-key concurrency + GUID-default-key coverage (SQL Server + PostgreSQL)`

---

## Task 4: Docs + final verification

**Files:** Modify `docs/site/articles/features/crud.md`, `docs/site/develop/roadmap.md`.

- [ ] **Step 1: Update the crud.md upsert-concurrency table.** In the "Upsert concurrency semantics" table:
  - SQL Server **client key** cell → atomic (`MERGE … WITH (HOLDLOCK)`); drop the "racing call may get a duplicate-key error" caveat.
  - SQL Server **generated key** cell → "`MERGE … WITH (HOLDLOCK)` on the explicit-key branch — atomic" (was "IF/EXISTS check then UPDATE or INSERT — multi-statement, race window").
  - PostgreSQL **generated key** cell → "`INSERT … ON CONFLICT (key) DO UPDATE` (explicit-key arm) — atomic" (was the multi-statement CTE).
  Also update the paragraph that says SQL Server/Oracle "a duplicate-key failure on one parallel call is a known engine-level race" so it applies to **Oracle only** (SQL Server is now hardened).

- [ ] **Step 2: Update the Roadmap.** In `docs/site/develop/roadmap.md`, under "Performance & optimization", remove the "Harden generated-key upsert atomicity" bullet, and add to "Recently resolved":
  ```markdown
  - **Upsert atomicity (SQL Server + PostgreSQL):** generated-key upserts are now atomic — SQL Server uses
    `MERGE … WITH (HOLDLOCK)` (client and generated key), PostgreSQL uses `INSERT … ON CONFLICT` — so
    concurrent same-key upserts no longer throw a spurious duplicate-key error; covered by live
    concurrency + `uniqueidentifier`/`gen_random_uuid()` key tests. (SQLite/MySQL already atomic; Oracle
    generated-key upsert remains unsupported — tracked separately.)
  ```
  If a "Known issues" or "Planned" reference to upsert atomicity exists, reconcile it. (Leave a Planned bullet for Oracle generated-key upsert + the SQLite/MySQL parity-tests follow-up if you want them tracked — optional.)

- [ ] **Step 3: Non-Docker suites green.** Run: `dotnet test tests/Inquiry.Generators.Tests -f net8.0 --nologo` and `dotnet test tests/Inquiry.Sqlite.Tests -f net8.0 --nologo` → all pass.

- [ ] **Step 4: Confirm scope.** `git diff main...HEAD --stat` → only the two builders, the generator tests, the SQL Server/PostgreSQL fixtures + upsert tests, crud.md, roadmap.md, and the spec/plan docs. `git status --short` clean.

- [ ] **Step 5: Commit.**
  ```bash
  git add docs/site/articles/features/crud.md docs/site/develop/roadmap.md
  git commit -F <bom-free-msg-file>
  ```
  Message: `docs: record atomic SQL Server/PostgreSQL upsert in CRUD table + Roadmap`

- [ ] **Step 6: Final code review + finish.** Run the code-review skill over the branch diff; address Critical/Important findings; then finish via `superpowers:finishing-a-development-branch`.

---

## Self-Review (completed)

**Spec coverage:** ✓ SQL Server HOLDLOCK client+generated (Task 1) · PostgreSQL ON CONFLICT generated (Task 2) · generated-key concurrency + GUID/NEWSEQUENTIALID·gen_random_uuid tests + tightened client-key test (Task 3) · crud.md + Roadmap (Task 4) · non-Docker suites + byte-identical other dialects (Tasks 1,2,4). All spec §6 criteria map to a task. (Phase 2 SQLite/MySQL parity tests and Phase 3 Oracle are explicitly out of this plan per spec §7.)

**Placeholder scan:** Builder code is exact. Emission tests give concrete assertions with a note to confirm exact escaping against generated output (inherent to emission testing). Live tests + fixtures + DDL are complete. The one flagged unknown (`SelectByKeyAsync(Guid?)` signature) has an explicit resolution path.

**Type/name consistency:** `GuidItem`/`GuidItemStore`, `UpsertAsync`/`UpsertReturningAsync`/`SelectByKeyAsync`/`SelectAllAsync`, `CreateFromDdlAsync`, table `TGuidItem`, key `Id`, and the `MERGE … WITH (HOLDLOCK)` / `ON CONFLICT` SQL fragments are used consistently across builder, emission tests, and live tests.
