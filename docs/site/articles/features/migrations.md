# Migrations recipe (DbUp / FluentMigrator)

Inquiry deliberately does **not** ship a migration engine. It emits the full `CREATE TABLE` DDL for your entities as a compile-time constant — `InquiryGeneratedSchema.Ddl` (see [Schema DDL](schema-ddl.md)) — and leaves schema *evolution* to the dedicated tools that already do it well. This page is the recipe for wiring that constant into them.

> `InquiryGeneratedSchema` is generated `internal` to your entity assembly, so run the migration bootstrap from that assembly (or expose the string yourself).

On SQL Server, `Ddl` includes the generated TVP types required by positive collection predicates. For an existing database, add `InquiryGeneratedSchema.ProviderArtifactsDdl` to a migration before deploying code that references those methods. Use `ProviderArtifactsValidationSql` as a read-only deployment check: success returns no rows; otherwise inspect `Status` (`missing`, `mismatched`, or `metadata-invisible`) and `Details`. Do not run setup lazily from application requests: binding is deliberately free of catalog I/O and DDL, and invalid artifacts fail visibly so migration drift is not hidden.

Use separate deployment and application principals. The migration principal needs permission to create schemas/types. The application principal needs `REFERENCES` on the generated types (granting `REFERENCES` on the owning schema is the practical option) plus its ordinary table permissions. A principal running the validation query also needs catalog visibility, normally `VIEW DEFINITION` on the owning schema/database; without it the query reports `metadata-invisible` instead of incorrectly claiming the type is missing.

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

When a new SQL Server collection element type or entity schema appears, include the regenerated `ProviderArtifactsDdl` in the next migration. Names are deterministic, so independently deployed instances and databases agree on the same objects.

TVP v2 names include exact facets and nullability. SQL Server cannot alter a user-defined table type in place, so deploy in this order: create the newly generated types, run validation with a catalog-visible deployment principal, deploy the application binaries that reference them, then remove unreferenced legacy types in a later migration. Do not drop old types in the same migration when rolling instances may still use them. This also applies when moving from the pre-release coarse TVP names to v2.

Diff-based migration generation, `ALTER` emission, versioning, and rollback are explicitly not planned ([roadmap](../../develop/roadmap.md#explicitly-out-of-scope-for-10)) — DbUp/FluentMigrator/Flyway own that lifecycle. Inquiry's contribution is an always-correct, dependency-ordered baseline for free.

## See also

- [Schema DDL](schema-ddl.md) — what the generated script contains and per-dialect shapes.
- [Testing](testing.md) — the SQLite fixture that bootstraps test databases from the same constant.
