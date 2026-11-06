# Migrations recipe (DbUp / FluentMigrator)

Inquiry deliberately does **not** ship a migration engine. It emits the full `CREATE TABLE` DDL for your entities as a compile-time constant — `InquiryGeneratedSchema.Ddl` (see [Schema DDL](schema-ddl.md)) — and leaves schema *evolution* to the dedicated tools that already do it well. This page shows how to use those constants with DbUp, from the first deployment through later schema changes, on SQL Server.

> `InquiryGeneratedSchema` is generated `internal` to your entity assembly, so run the migration bootstrap from that assembly (or expose the string yourself).

## Rules

- `Ddl` describes the schema that the current entities expect. It is a reference, not a migration. Take a copy of it once as your first migration script and never regenerate that script.
- Write every later change as a new script. Inquiry does not generate `ALTER` statements.
- DbUp records script names, not script contents. After any database has run a script, never rename or edit it.
- On SQL Server, a `Compare.In` method binds a table-valued parameter whose type name comes from the column's exact type, facets and nullability. When those change, the new application version needs a new type. Add it in the same migration as the column change, and drop the old type in a later release. A list of up to 16 integers or strings binds as scalar parameters and does not use the type, so a missing type or a missing permission fails only a call with more values. Run `ProviderArtifactsValidationSql` during deployment instead of relying on calls to find it.

## The runnable sample

[`samples/Inquiry.SchemaEvolution`](https://github.com/IgnyteSoftware/inquiry/tree/main/samples/Inquiry.SchemaEvolution) takes one SQL Server database through three releases with DbUp (`dbup-sqlserver` 7.2.0):

1. Release 1 creates the `CatalogItems` table, with a 32-character `Code` column.
2. Release 2 widens `Code` to 64 characters.
3. Release 3 drops the table type that only version 1 of the application uses.

Run it with Docker installed:

```bash
dotnet run --project samples/Inquiry.SchemaEvolution/Runner
```

The Runner starts a SQL Server container, runs every step on this page, checks the result of each step, and prints one line per step. The same code runs in the repository's SQL Server test suite and against the packed release candidate packages, and both compare the output with the transcript below.

Version 1 of the entity library:

[!code-csharp[](../../../../samples/Inquiry.SchemaEvolution/V1/Catalog.cs)]

Version 2 is the same file with `[InquiryColumn(Length = 64)]` on `Code`, a `Catalog.V2` namespace and an `AddCatalogV2` method. The sample compiles each version as its own assembly only because one process hosts both. A real application has one entity assembly, at two different commits.

The migration scripts are frozen copies. `0001` is version 1's `Ddl`. `0002` is a hand-written `ALTER` plus version 2's `ProviderArtifactsDdl`, which creates only the new table type:

[!code-sql[](../../../../samples/Inquiry.SchemaEvolution/Scripts/0001-initial-schema.sql)]

[!code-sql[](../../../../samples/Inquiry.SchemaEvolution/Scripts/0002-widen-code.sql)]

[!code-sql[](../../../../samples/Inquiry.SchemaEvolution/Scripts/0003-retire-v1-type.sql)]

The scripts are embedded resources. `Migrations` loads them and builds the DbUp engine. Each release passes the scripts it knows about:

[!code-csharp[](../../../../samples/Inquiry.SchemaEvolution/Migrations.cs)]

The Runner prints:

[!code-text[](../../../../samples/Inquiry.SchemaEvolution/expected-output.txt)]

The complete workflow is in [`SchemaEvolution.cs`](https://github.com/IgnyteSoftware/inquiry/blob/main/samples/Inquiry.SchemaEvolution/SchemaEvolution.cs). The sections below explain each group of lines.

## New database

For an empty database, release 2 passes `[0001, 0002]` and calls `PerformUpgrade()`. DbUp runs both scripts in name order and records each one in `dbo.SchemaVersions`. Both versions' `ProviderArtifactsValidationSql` return no rows, so every table type either version binds exists. A second engine with the same scripts reports nothing to run.

## Existing database

A database created before you adopted DbUp has no journal, so DbUp would run `0001` again. Record `0001` as already applied without running it:

1. Confirm the database matches `0001`. `ProviderArtifactsValidationSql` checks only table types. Compare the tables, columns and indexes yourself, or with a schema comparison tool. In the sample, the version 2 validation reports `missing` because version 2's type does not exist yet. That is expected.
2. Build an engine that contains only `0001` and call `MarkAsExecuted()`. It records every script the engine has not run, without running it. Because the engine knows only `0001`, it cannot record `0002` by mistake.
3. Build the release 2 engine. `GetScriptsToExecute()` now returns only `0002`, and `PerformUpgrade()` runs it.

`MarkAsExecuted(string latestScript)` records scripts up to and including the named one. If no script has that name, it records every pending script and still reports success. If you use it, check `result.Scripts` afterwards. Do not use DbUp's `NullJournal` for this. It records nothing, so every script runs every time.

## Deployment order

1. Back up the database. DbUp takes no lock between deployments, so make sure that only one runs at a time.
2. Run the release's migrations as the deployment principal. Keep the change additive while older application versions still run: widen columns, and create the new version's table types. Do not drop anything yet.
3. Run `ProviderArtifactsValidationSql` for every application version that will run against the database, as a principal that can see catalog metadata. Stop if any query returns rows. Each row gives `ArtifactName`, `ExpectedElementSignature`, `Status` (`missing`, `mismatched` or `metadata-invisible`) and `Details`.
4. Deploy the new application version while the old one keeps running. Until every old instance has stopped, keep data within what the old version accepts. In the sample, codes stay at 32 characters or fewer.
5. In a later release, drop the old table types, once no instance, rollback build or scheduled job can still use them.

Never drop an old table type in the same migration that creates its replacement. This also applies when you move from the pre-release coarse TVP names to the v2 names.

## Coexistence

After release 2, the sample runs version 1 and version 2 of the application against the same database. Each version binds its own table type: `nvarchar(32)` for version 1 and `nvarchar(64)` for version 2. Version 2 reads a row that version 1 wrote, and version 1 reads a row that version 2 wrote. Only after version 1 stops does version 2 store a 48-character code.

Release 3 then drops version 1's type. Version 1's validation now reports `missing`, and a version 1 call with more than 16 values fails with a `SqlException` that names the type. Version 2 is not affected. Inquiry binds table types without any catalog query or DDL, so a missing type always fails visibly at the call and never at startup.

## Permissions

Use separate principals for deployment and for the application.

| Principal | Needs |
|---|---|
| Deployment | `CREATE TABLE`, `CREATE TYPE` and `ALTER` on the target schema ([`CREATE TYPE` permissions](https://learn.microsoft.com/sql/t-sql/statements/create-type-transact-sql#permissions)); permission to create and write DbUp's `dbo.SchemaVersions` table; and `VIEW DEFINITION` on the schema or database, so the validation query can see type metadata. |
| Application | The table permissions its stores use, plus `EXECUTE` and `REFERENCES` on each generated table type it binds, or on the schema that owns them ([table-valued parameter permissions](https://learn.microsoft.com/sql/relational-databases/tables/use-table-valued-parameters-database-engine#permissions)). No `CREATE` or `ALTER` permission and no access to the journal. |

`REFERENCES` alone is not enough. When the application principal has no `EXECUTE` permission on the type, SQL Server rejects the call with error 229, "The EXECUTE permission was denied on the object". The sample creates an application login with `SELECT` and `INSERT` on the table. Its first call fails with error 229. The sample pads every lookup past 16 codes with codes that match no row, so each call uses the table type. After `GRANT EXECUTE, REFERENCES ON TYPE::…`, the same call succeeds. The sample deploys as a server administrator, so it does not prove the minimum deployment permissions.

When a type is not visible to the principal and it has no `VIEW DEFINITION` on the schema or database, the validation query reports `metadata-invisible` instead of claiming the type is missing. Run validation as the deployment principal.

## Failure recovery

The sample uses `WithTransactionPerScript()`. DbUp runs each script and writes its journal row in one transaction. When a script fails, SQL Server rolls back that script and its journal row. Scripts that ran before it in the same release stay applied. The sample proves this with a copy of `0002` that fails after its `ALTER`: the journal still lists only `0001`, `Code` is still `nvarchar(32)`, and the version 2 type does not exist. The real `0002` then runs normally.

- `PerformUpgrade()` and `MarkAsExecuted()` return a `DatabaseUpgradeResult`. Check `Successful`, and stop the rollout if it is false. `Error` holds the exception and `ErrorScript` names the script that failed.
- Correct the cause and run the same release again. DbUp continues from the first script that is not in the journal. You can still edit a script that failed, as long as no database has recorded it.
- Do not delete journal rows, and do not mark a failed script as executed to move on.
- A committed migration is not undone when a later step fails, such as validation or the application deployment. Fix forward with a new script, or restore the backup.
- Rolling back the application binaries works only while the old version's table types exist and the data still fits the old mapping.
- `WithTransaction()` runs a whole release in one transaction instead. `WithTransactionAlwaysRollback()` runs the scripts and then rolls everything back, for a rehearsal. `GetScriptsToExecute()` lists what would run. `GetExecutedButNotDiscoveredScripts()` lists journaled scripts that the engine no longer has, which usually means a script was renamed or removed.

## Provider-specific limitations

The runnable sample covers SQL Server only. DbUp ships a separate package for each database, and each package has its own transaction and script-splitting behaviour.

| Provider | Limitation |
|---|---|
| SQL Server | A table type cannot be altered. A facet or nullability change creates a new type, and the old one is dropped later. DbUp splits scripts on `GO` lines; Inquiry's constants contain none. |
| PostgreSQL, MySQL, MariaDB, Oracle, SQLite | Inquiry generates no provider artifacts, so `ProviderArtifactsDdl` and `ProviderArtifactsValidationSql` do not exist. Check the schema with your own queries. |
| MySQL, MariaDB, Oracle | DDL statements commit implicitly. A transaction cannot roll back a script that fails halfway, so keep one DDL statement per script. |
| Oracle | `Ddl` uses plain `CREATE TABLE`, which fails with `ORA-00955` if the table exists. See [Schema DDL](schema-ddl.md#per-dialect-ddl-flavor). |
| SQLite | `ALTER TABLE` cannot change a column's type. Rebuild the table instead. |
| All | `Ddl` is a run-once script. Deferred cyclic foreign keys and indexes are not guarded, so do not run it against a database that already has the schema. |

## FluentMigrator

The repository does not run this example. Wrap the frozen first script in the first migration, and write later migrations with the normal fluent API:

```csharp
[Migration(1)]
public sealed class InitialSchema : Migration
{
    public override void Up() => Execute.Script("Scripts/0001-initial-schema.sql");
    public override void Down() => throw new NotSupportedException("Baseline is not reversible.");
}
```

## Keeping the baseline honest

- **Schema-drift test.** In CI, create a database from your migration chain and another from the current `InquiryGeneratedSchema.Ddl`, then compare them (table and column lists are enough). If a migration drifts from what the entities declare, the test fails before production does. The `Inquiry.Sqlite.Testing` fixture makes the entity-side database a one-liner.
- **Validate every running version.** Before and after each deployment, run `ProviderArtifactsValidationSql` from every application version that is still deployed.

## What stays out of scope

Diff-based migration generation, `ALTER` emission, versioning, and rollback are explicitly not planned ([roadmap](../../develop/roadmap.md#explicitly-out-of-scope-for-10)) — DbUp/FluentMigrator/Flyway own that lifecycle. The sample proves one widening change. Renaming, narrowing or dropping columns, and changes to keys or indexes, each need their own review and test. `ProviderArtifactsValidationSql` checks table types only, not tables, columns, indexes or data.

## See also

- [Schema DDL](schema-ddl.md) — what the generated script contains and per-dialect shapes.
- [SQL Server provider](../providers/sqlserver.md) — table-valued parameters and their artifacts.
- [Testing](testing.md) — the SQLite fixture that bootstraps test databases from the same constant.
