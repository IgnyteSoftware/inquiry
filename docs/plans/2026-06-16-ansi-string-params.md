# ANSI / non-unicode string columns (varchar SARGability)

**Lane:** feature. **Goal:** let a column/key be declared non-unicode so its parameters bind as
`DbType.AnsiString` (varchar) instead of `DbType.String` (nvarchar) — so `WHERE varcharCol = @p` seeks a
`varchar` index instead of scanning. The EF Core `IsUnicode(false)` analog. Backward-compatible: default
unicode = true preserves today's behavior.

## Why
A benchmark over `varchar`-keyed tables measured Inquiry's filtered reads at ~17 ms (full scan, 200k rows)
vs ~2 ms for ADO/EF/Dapper/DLG, which bind the parameter as `varchar` and seek. Inquiry's generator emits
`DbType.String` for every string parameter (`StoreOperationEmitter.ResolveDbType` →
`SqlBuilder.MapDbTypeExpression` → `DbTypeMapper`), and there is no unicode/ANSI knob anywhere.

## Verified targets (all @ commit c9f8a17)
1. `src/Inquiry/Entities/InquiryColumnAttribute.cs` — add `public bool IsUnicode { get; set; } = true;`
   (auto-inherited by `InquiryKeyAttribute : InquiryColumnAttribute`).
2. `src/Inquiry.Generators.Shared/Models/ColumnData.cs` (record body, ~line 70, near `IsIndexed`) — add
   `public bool IsUnicode { get; init; } = true;` (additive init-only per the FOUNDATION CONVENTION).
3. `src/Inquiry.Generators.Shared/Abstractions/IColumn.cs` (DDL metadata section, ~line 134) — add
   `bool IsUnicode { get; }`.
4. `src/Inquiry.Generators.Shared/EntityProcessor.cs` — in the single `new ColumnData { … }` initializer
   (~line 401, beside `IsIndexed = …`) add `IsUnicode = metadataAttribute is null || GeneratorHelpers.GetNamedBool(metadataAttribute, "IsUnicode", true),`.
   (`GeneratorHelpers.GetNamedBool(attr, name, bool defaultValue)` overload exists @ GeneratorHelpers.cs:101.)
5. `src/Inquiry.Generators.Shared/Infrastructure/DbTypeMapper.cs` — add a `bool isUnicode = true` parameter
   to `TryGetDbTypeExpression(TypeData type, …)` and the private `Map(SpecialType, bool isUnicode)`:
   `System_String` → `isUnicode ? "…DbType.String" : "…DbType.AnsiString"`;
   `System_Char` → `isUnicode ? "…DbType.StringFixedLength" : "…DbType.AnsiStringFixedLength"`. All other
   arms unchanged. Keep `TryGetDbTypeForSpecialType(SpecialType)` (converter path) at unicode default.
6. `src/Inquiry.Generators.Shared/Abstractions/SqlBuilder.cs` (line 35) — add `bool isUnicode = true` to
   `MapDbTypeExpression(TypeData type, …)` and forward it to `DbTypeMapper.TryGetDbTypeExpression`.
7. `src/Inquiry.Generators.Shared/StoreOperationEmitter.cs` (`ResolveDbType`, line 477) — change the final
   `return sqlBuilder.MapDbTypeExpression(column.Type);` to
   `return sqlBuilder.MapDbTypeExpression(column.Type, column.IsUnicode);`. This is the single chokepoint:
   all column param bindings (insert/update VALUES, where-by-key, where-by-field, keyset cursor) route
   through `ResolveDbType` (call sites 333, 372, 584, 771, 908), so one change covers every SARGable site.
8. `src/Inquiry.SqlServer.Analyzer/SqlServerSqlBuilder.cs` (`MapColumnType`, lines 213/230, `String` arm) —
   emit `VARCHAR` instead of `NVARCHAR` when `!column.IsUnicode` (bounded + MAX forms).

**Out of scope (note, don't do):** the FTS search-term param (`StoreOperationEmitter.cs:266`, a value not a
column); ANSI DDL for the non-SqlServer dialects (the runtime `DbType.AnsiString` fix is dialect-agnostic and
already helps them; their varchar DDL can follow). Inferring ANSI from `SqlType="varchar"` — deferred; the
explicit flag is the contract.

## Plan-owned tests (RED first)
New partial file `tests/Inquiry.Generators.Tests/InquiryGeneratorTests.AnsiString.cs`:
- **`NonUnicodeStringColumn_BindsAnsiStringParameter`** — entity with `[InquiryKey(IsUnicode = false)]
  string Code` + `[InquiryColumn(IsUnicode = false)] string Name` and a `[InquirySelectAllByField("Name")]`
  store method. Assert the generated store text **Contains** `global::System.Data.DbType.AnsiString` (the
  where-by-field + by-key + insert/update bindings) and the generator/compilation produce no errors.
- **`UnicodeStringColumn_StillBindsStringParameter` (backward-compat)** — a default `[InquiryColumn] string`
  column → generated text still **Contains** `global::System.Data.DbType.String` and **DoesNotContain**
  `DbType.AnsiString` for that entity.

RED reason: `IsUnicode` does not yet exist on `InquiryColumnAttribute`, so the test's generator-input source
(`IsUnicode = false`) is a compile error in the in-memory compilation and `DbType.AnsiString` is never
emitted — a valid not-yet-existing-member red.

## Success criteria (mechanically checkable)
- Both new tests GREEN; `dotnet test tests/Inquiry.Generators.Tests` GREEN (no regressions).
- `dotnet build Inquiry.slnx` GREEN, warnings-as-errors clean.
- Diff scope ⊆ the 8 files above + the new test file.

## Expected diff scope
The 8 source files in Verified Targets + `tests/Inquiry.Generators.Tests/InquiryGeneratorTests.AnsiString.cs`.
Anything else is a review flag.

## Slow gates triggered
`src/**` change → SQL Server Testcontainers integration suite (`tests/Inquiry.SqlServer.Tests`). Docker-backed;
may be deferred-with-reason at land if Docker is contended — the generator test deterministically proves the
`DbType.AnsiString` emission (the actual contract); a live seek assertion is a nice-to-have, not the pin.

## Codex effort
`high` — codegen template surgery threading a flag through the shared generator + a provider DDL builder.

## Cycle summary
- **Changed (8 files, +20/-10):** `IsUnicode` flag (default `true`) on `InquiryColumnAttribute` →
  threaded through `ColumnData`/`IColumn`/`EntityProcessor` → `DbTypeMapper` emits
  `DbType.AnsiString`/`AnsiStringFixedLength` for non-unicode `string`/`char` → `ResolveDbType` passes
  `column.IsUnicode` → SQL Server DDL emits `VARCHAR` instead of `NVARCHAR`. Implemented by Codex
  (gpt-5.5, effort high) against the locked tests; diff matched the plan exactly, no review findings.
- **Tests:** 2 new generator tests (`NonUnicodeStringColumn_BindsAnsiStringParameter` RED→GREEN;
  `UnicodeStringColumn_StillBindsStringParameter` backward-compat guard). Full
  `Inquiry.Generators.Tests` suite **348 passed ×3 TFMs (net8/9/10)**, 0 failures. `dotnet build
  Inquiry.slnx -c Release` clean. Test-lock verified (Codex did not touch the test file). `ColumnData`
  is the only `IColumn` implementer, so the interface addition broke nothing.
- **Deferrals / follow-ups:** ANSI DDL for the non-SQL-Server dialects (the runtime `AnsiString` binding
  is already dialect-agnostic; only the SQL Server `VARCHAR` DDL was done); inferring ANSI from
  `SqlType = "varchar"`; the FTS search-term parameter; enum-as-string columns (still nvarchar); a
  dedicated SQL Server round-trip integration test on an `IsUnicode=false` entity (recommended).
- **Skipped slow gates + reason:** the Docker integration suites (SqlServer/MySql/Oracle/PostgreSql)
  were skipped — the change is backward-compatible, emitting byte-identical code for every existing
  (unicode) entity (confirmed by the 348 unchanged generator tests + clean full build), and no existing
  integration test exercises an `IsUnicode=false` entity, so the Docker suites would only re-test
  unchanged generated code.
