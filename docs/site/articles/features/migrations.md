# Migrations recipe (DbUp / FluentMigrator)

Inquiry deliberately does **not** ship a migration engine. It emits the full `CREATE TABLE` DDL for your entities as a compile-time constant — `InquiryGeneratedSchema.Ddl` (see [Schema DDL](schema-ddl.md)) — and leaves schema *evolution* to the dedicated tools that already do it well. This page is the recipe for wiring that constant into them.

> `InquiryGeneratedSchema` is generated `internal` to your entity assembly, so run the migration bootstrap from that assembly (or expose the string yourself).

## DbUp

Use the generated DDL as migration **0001** and hand-write everything after it. DbUp journals applied scripts, so the baseline runs exactly once:

```csharp
using DbUp;

var upgrader = DeployChanges.To
    .SqlDatabase(connectionString)
    .WithScript("0001-initial-schema", Inquiry.Generated.InquiryGeneratedSchema.Ddl)
    .WithScriptsEmbeddedInAssembly(typeof(Program).Assembly)   // 0002+, your ALTERs
    .LogToConsole()
    .Build();

var result = upgrader.PerformUpgrade();
if (!result.Successful) throw result.Error;
```

For an **existing** database that already matches the entities, journal the baseline without executing it (`upgrader.MarkAsExecuted(...)` via `DbUp`'s `NullJournal`/baselining patterns), then apply only `0002+`.

## FluentMigrator

Wrap the constant in the first migration; later migrations use the normal fluent API:

```csharp
[Migration(1)]
public sealed class InitialSchema : Migration
{
    public override void Up() => Execute.Sql(Inquiry.Generated.InquiryGeneratedSchema.Ddl);
    public override void Down() => throw new NotSupportedException("Baseline is not reversible.");
}
```

## Keeping the baseline honest

Two practices make this setup self-policing:

- **Schema-drift test.** In CI, create a database from your migration chain and another from `InquiryGeneratedSchema.Ddl`, then compare (table/column dumps are enough). If a migration drifts from what the entities declare, the test fails before production does. The `Inquiry.Testing` SQLite fixture makes the entity-side database a one-liner.
- **Never edit migration 0001.** When entities change, write a new `ALTER` migration; the regenerated `Ddl` constant is your reference for what the end-state should be, not a script to re-run.

## What stays out of scope

Diff-based migration generation, `ALTER` emission, versioning, and rollback are explicitly not planned ([roadmap](../../develop/roadmap.md#explicitly-not-planned)) — DbUp/FluentMigrator/Flyway own that lifecycle. Inquiry's contribution is a always-correct, dependency-ordered baseline for free.

## See also

- [Schema DDL](schema-ddl.md) — what the generated script contains and per-dialect shapes.
- [Testing](testing.md) — the SQLite fixture that bootstraps test databases from the same constant.
