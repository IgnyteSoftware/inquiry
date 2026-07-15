# SQL Server TVP artifacts and I/O-free binding (#199)

## Goal

Implement the artifact-lifecycle phase of umbrella #69:

- discover every SQL Server TVP signature at compile time;
- emit deterministic, schema-qualified user-defined table types through explicit generated setup tooling;
- pass the exact generated type name into each generated TVP binder;
- remove all catalog access, DDL, synchronization, and connection use from parameter binding;
- make missing artifacts visible through generated validation SQL and fail normally at command execution.

Preserve the existing query contract: constant SQL and exactly one Structured parameter for positive `IN`/DeleteAll collections.

## Explicitly out of scope

- Exact per-column TVP metadata, bounded strings/decimals, complete temporal/binary support, and streaming/bounded record allocation are #197.
- Stored-procedure input/output/TVP parameters are #188.
- TVP/OPENJSON/scalar cardinality benchmarks are #87.
- Do not redesign negative `NOT IN`; it retains scalar expansion for its empty-set semantics.
- Do not add automatic migration/diff/drop behavior. Initial/setup DDL remains additive and migration-runner friendly.

## Current defect

`InquiryTvpParameter.Bind` currently:

1. derives a process-wide unqualified name such as `Inquiry_IntList`;
2. synchronously calls `EnsureType` during command binding;
3. queries/executes DDL through the command connection;
4. caches success in a static `ConcurrentDictionary<string, byte>` keyed only by type name.

SQL Server user-defined table types are database- and schema-local. The cache can therefore suppress provisioning in a second database, concurrent first use can race, a missing artifact appears inside ordinary query binding, and ambient transactions can be affected by unexpected DDL/I/O.

## Architecture

### 1. Provider-neutral compile-time artifact descriptor

Add a small value-equatable descriptor in `src/Inquiry.Generators.Shared/Abstractions`, for example:

```csharp
public sealed record CollectionParameterArtifact(
    string Identity,
    string Schema,
    string Name,
    string RuntimeTypeName,
    string CreateDdl,
    string ValidationName);
```

Names may vary, but the model must carry:

- a canonical deduplication identity;
- schema and unqualified object name;
- the unquoted `schema.name` string assigned to `SqlParameter.TypeName`;
- guarded setup DDL;
- a qualified name suitable for validation diagnostics.

Add virtual `SqlBuilder` hooks whose portable defaults return no artifact:

```csharp
public virtual CollectionParameterArtifact? BuildCollectionParameterArtifact(
    string? owningSchema,
    IColumn column) => null;
```

The same hook is called during collection binding emission and artifact collection, ensuring the generated binder and generated DDL cannot derive names independently.

Do not expose `ColumnData` across analyzer assemblies. Use the existing public `IColumn` abstraction plus owning schema.

### 2. Collect usages from validated store operations

Collection artifacts are query artifacts, not merely entity artifacts. Generate only types that emitted store methods actually reference.

During `StoreProcessor.Emit`, after operation and predicate validation has produced the `valid` method list:

- collect the single key column for every valid `DeleteAll`;
- collect every resolved predicate binding where `IsCollection` is true and `IsNegatedCollection` is false;
- include select predicates and set-based update/delete predicates;
- use the entity's declared schema, normalized by SQL Server to `dbo` when absent;
- call `BuildCollectionParameterArtifact` and append non-null descriptors to a generator-owned accumulator.

Do not collect:

- invalid/stubbed methods;
- scalar predicates;
- `NOT IN` bindings that use expansion;
- unused entity columns;
- stored-procedure parameters.

Thread an artifact accumulator from `InquiryGeneratorBase` into `StoreProcessor.Emit`, or return a richer emission result containing the registration plus descriptors. Prefer a returned result if it avoids hidden mutation; either design must keep collection tied to the already-resolved `valid` operations rather than re-parsing stores in `SchemaEmitter`.

After all stores emit, deduplicate by `Identity` and sort ordinally by schema/name before schema emission. Deterministic ordering is required for incremental output and migration diffs.

### 3. Stable schema-qualified naming

SQL Server generates a canonical v1 element signature from the effective provider/storage category already supported by `InquiryTvpParameter` in this phase. Examples include bit, tinyint, smallint, int, bigint, real, float, decimal(18,2), nvarchar(max), uniqueidentifier, datetime2, and datetimeoffset.

Converters use their non-null `TProvider`; enums use their effective underlying storage category. Exact column length/precision/scale and the remaining unsupported categories stay with #197.

Canonical identity:

```text
sqlserver-tvp-v1|schema=<normalized-schema>|element=<normalized-current-signature>
```

Object name:

```text
Inquiry_Tvp_<full lowercase-or-uppercase SHA-256 hex of canonical element signature>
```

Use the full SHA-256 digest, not `GetHashCode`, a truncated process-random hash, CLR type name alone, or a store/method name. The resulting identifier remains below SQL Server's 128-character limit and changes automatically when #197 later changes the canonical signature.

Qualified forms:

- DDL: `[schema].[Inquiry_Tvp_<hash>]`
- `SqlParameter.TypeName`: `schema.Inquiry_Tvp_<hash>`

Normalize a missing entity schema to `dbo`. Escape `]` in DDL identifiers. Validate/escape runtime schema and object segments without accepting a caller-provided combined string.

Server and database are deployment identity boundaries and are not knowable at source-generation time. Collision safety comes from the full runtime identity `(server, database, schema, artifact name)` plus removal of all process-global provisioning state. The same stable name may correctly exist independently in two databases or servers.

Two usages with the same normalized schema/signature share one artifact. The same signature in two schemas produces two schema-qualified artifacts.

### 4. Generated setup and validation contract

Extend `SchemaEmitter.Emit` to accept the deduplicated provider artifacts. It must emit schema output even when there are no table entities but a view/store requires a collection artifact.

For SQL Server, generate in `Inquiry.Generated.InquiryGeneratedSchema`:

```csharp
public const string ProviderArtifactsDdl = @"...";
public const string ProviderArtifactsValidationSql = @"...";
public const string Ddl = ProviderArtifactsDdl + @"...table/index DDL...";
```

Keep the class's current accessibility policy. It is generated into the consuming assembly, so application setup/migration code in that assembly can use the constants without creating a cross-assembly public type collision.

`ProviderArtifactsDdl` requirements:

- deterministic artifact order;
- schema-qualified `TYPE_ID` checks;
- guarded, escaped `CREATE TYPE ... AS TABLE ([Value] <current SQL type> NOT NULL)`;
- dynamic execution where SQL Server grammar requires `CREATE TYPE` to begin its batch;
- create the owning non-default schema only when the application has explicitly declared it and the schema is absent, or document that schema creation precedes artifact setup. Prefer emitting the same guarded schema setup already used by generated table DDL if such a hook exists; do not silently fall back to dbo.
- no drop or alter of existing types.

Example shape, with correctly escaped literals:

```sql
IF TYPE_ID(N'[tenant].[Inquiry_Tvp_<hash>]') IS NULL
    EXEC(N'CREATE TYPE [tenant].[Inquiry_Tvp_<hash>]
           AS TABLE ([Value] INT NOT NULL)');
```

`ProviderArtifactsValidationSql` returns one row per missing artifact, including its schema-qualified name and expected element signature. It performs no writes. Prefer a result-set query over `THROW` so setup tools can report all missing artifacts in one run. An empty result set means valid.

Compatibility:

- Existing users applying `InquiryGeneratedSchema.Ddl` continue to get a complete initial schema because artifact DDL is prepended.
- Migration users can apply `ProviderArtifactsDdl` as a standalone additive migration before deploying code that references the new type names.
- Existing runtime-created names are not reused or dropped. Pre-1.0 test artifacts may remain harmlessly until the database owner removes them.
- A later #197 signature change creates a new stable type rather than trying to mutate an immutable SQL Server table type.

Non-SQL Server dialects may omit these additional constants or emit empty values, but their existing `Ddl` output must remain byte-for-byte unchanged.

### 5. Generated binder contract

Change the SQL Server collection binder call to include the descriptor's exact runtime name:

```csharp
InquiryTvpParameter.Bind(
    command,
    "@CategoryId",
    values,
    "tenant.Inquiry_Tvp_<hash>");
```

Collection binding emission already has the resolved `ColumnData`; thread the owning entity schema into the common collection binding helper and call the same `BuildCollectionParameterArtifact` hook used by the collector.

Fail generation if SQL Server selects its TVP transport but the builder cannot return a descriptor for a currently supported signature. Do not silently fall back to an unqualified legacy runtime name. Unsupported metadata/categories owned by #197 retain their existing diagnostic/runtime scope; #199 must not pretend to solve them.

Positive `IN` SQL stays exactly:

```sql
[Column] IN (SELECT [Value] FROM @parameter)
```

DeleteAll uses the same constant SQL and one Structured parameter.

### 6. I/O-free runtime binder

In `src/Inquiry.SqlServer/Parameters/InquiryTvpParameter.cs`:

- change `Bind` to require the generated `typeName`;
- remove `EnsuredTypes` and `System.Collections.Concurrent`;
- remove `EnsureType`;
- remove the DDL-only `ResolveSqlType` path;
- do not access `command.Connection`;
- do not open commands, query `TYPE_ID`, execute DDL, wait on a lock, or cache database state;
- retain current element-to-`SqlDataRecord` behavior and current metadata mapping until #197;
- create one `SqlParameter` with `SqlDbType.Structured`, the generated `TypeName`, and the current record value.

Validate null command/name/type-name arguments locally without I/O. Avoid parsing/re-hashing the type name at runtime; it is compile-time generated.

Add a runtime unit test using a disconnected/fake command proving `Bind` succeeds without a connection and creates exactly one Structured parameter. Add source/behavioral guards proving no command is created or executed by `Bind`.

Do not catch and replace execution-time `SqlException` for a missing type. The binder cannot discover that condition without forbidden I/O. The generated qualified `TypeName` makes SQL Server's natural error actionable, while `ProviderArtifactsValidationSql` provides the explicit preflight path.

## Missing-artifact behavior

The contract is deliberately split:

1. **Bind:** succeeds without I/O and constructs the parameter.
2. **Explicit validation:** generated validation SQL reports every missing qualified artifact.
3. **Execution without setup:** SQL Server fails the command with `SqlException` naming the missing table type.
4. **Setup then retry:** applying generated artifact DDL makes the unchanged generated command succeed.

Do not auto-heal at query time, retry DDL, or maintain a process cache.

## Exact files

Shared generator:

- `src/Inquiry.Generators.Shared/Abstractions/SqlBuilder.cs`
- add `src/Inquiry.Generators.Shared/Abstractions/CollectionParameterArtifact.cs`
- `src/Inquiry.Generators.Shared/InquiryGeneratorBase.cs`
- `src/Inquiry.Generators.Shared/StoreProcessor.cs`
- `src/Inquiry.Generators.Shared/StoreOperationEmitter.cs`
- `src/Inquiry.Generators.Shared/SchemaEmitter.cs`

SQL Server analyzer/runtime:

- `src/Inquiry.SqlServer.Analyzer/SqlServerSqlBuilder.cs`
- `src/Inquiry.SqlServer/Parameters/InquiryTvpParameter.cs`

Tests/docs:

- `tests/Inquiry.Generators.Tests/TvpGeneratorTests.cs`
- `tests/Inquiry.Generators.Tests/SchemaDdlGeneratorTests.cs` or a focused `TvpArtifactGeneratorTests.cs`
- `tests/Inquiry.SqlServer.Tests/InquiryTvpParameterTests.cs`
- add `tests/Inquiry.SqlServer.Tests/TvpArtifactIntegrationTests.cs`
- `tests/Inquiry.SqlServer.Tests/Fixtures/SqlServerTestHarness.cs` only for reusable two-database/schema setup seams
- `docs/site/articles/providers/sqlserver.md`
- `docs/site/articles/features/schema-ddl.md` or the current migrations/schema setup page
- `docs/site/articles/features/batch-operations.md`
- `docs/site/articles/features/prepared-statements.md`

## Red generator tests

Add focused snapshots proving:

- no TVP-using method produces no artifact;
- positive predicate IN emits one descriptor and passes its exact type name to `Bind`;
- DeleteAll and predicate IN with the same schema/signature deduplicate to one artifact;
- select and predicate-mutation IN usages are collected;
- NOT IN does not produce an artifact;
- identical signatures in two methods/stores deduplicate;
- identical signatures in `dbo` and a custom schema produce separate qualified artifacts;
- different supported signatures produce different full-hash names;
- converter/enum effective provider signatures select the current correct artifact category;
- ordering is stable under store declaration order changes;
- setup DDL quotes schema/name and guards `TYPE_ID`;
- validation SQL lists qualified missing names/signatures;
- `InquiryGeneratedSchema.Ddl` begins with/includes artifact setup before tables/indexes;
- non-SQL Server generated DDL and collection binding remain unchanged;
- generated SQL still contains one TVP parameter and no scalar expansion.

Add a deterministic naming unit test with fixed canonical signatures and expected hashes. Use a culture-invariant UTF-8 SHA-256 implementation; prove output is stable across repeated generator runs.

## Runtime unit tests

For `InquiryTvpParameter.Bind`:

- disconnected command/no connection still binds;
- exactly one Structured `SqlParameter` is added;
- `TypeName` equals the generated schema-qualified string;
- no DDL/catalog command is created or executed;
- null/empty collections retain current semantics;
- current supported element categories retain current record behavior;
- source no longer contains `EnsureType`, `ConcurrentDictionary`, `TYPE_ID`, `CREATE TYPE`, or connection access.

Do not add #197's exact metadata/streaming assertions here.

## Live SQL Server tests

Run all scenarios on net8.0, net9.0, and net10.0.

### Provisioned concurrent first use

1. Create a fresh database.
2. Apply generated complete DDL or `ProviderArtifactsDdl` plus table DDL.
3. Start multiple callers simultaneously on the first TVP query/DeleteAll use.
4. Assert all succeed and the type exists once.
5. Confirm no runtime CREATE TYPE/catalog activity is observable from the binder path.

This tests concurrent **use after provisioning**, not concurrent setup.

### Two databases in one process

1. Create two fresh databases with the same compiled stores.
2. Provision the generated artifact DDL independently in both.
3. Alternate/parallelize TVP queries through separate service providers.
4. Assert both succeed.

This is the regression for the former process-global unqualified cache.

### Non-default schemas

1. Map a TVP-using entity to a declared custom schema.
2. Apply generated setup.
3. Assert the type exists in that schema, not dbo.
4. Assert `SqlParameter.TypeName` is `schema.name` and the query succeeds.
5. Provision the same signature in dbo and the custom schema and prove they are independent.

### Ambient transaction

1. Provision artifacts before starting the transaction.
2. Start the repository's supported ambient transaction shape (`TransactionScope` if already covered, otherwise Inquiry's transaction API on one connection).
3. Execute a TVP select/mutation and an ordinary mutation.
4. Roll back and verify mutation rollback.
5. Assert no type DDL was attempted and the transaction remained usable.

Avoid accidental MSDTC promotion by following the repository's established SQL Server ambient-transaction fixture.

### Missing artifact

1. Create the table schema without `ProviderArtifactsDdl`.
2. Execute generated validation SQL and assert it reports the exact qualified type/signature.
3. Prove binding itself succeeds without connection I/O.
4. Execute the generated TVP command and assert `SqlException` clearly references the missing type.
5. Apply `ProviderArtifactsDdl`, validate empty results, and run the unchanged command successfully.

### Migration compatibility

1. Apply table DDL and artifact DDL as separate migration steps.
2. Reapply guarded artifact DDL and assert it is idempotent.
3. Leave an unrelated/legacy Inquiry TVP type in place and prove setup neither drops nor mutates it.
4. Verify complete `InquiryGeneratedSchema.Ddl` still provisions a fresh working database.

## Performance invariants

- `Bind` performs zero database/network I/O and zero lock waits.
- No process-global cache or database identity lookup.
- No runtime hashing/type-name derivation.
- Constant query SQL remains unchanged.
- Exactly one Structured parameter remains.
- Current record allocations remain unchanged until #197; do not regress them in this phase.
- Setup/validation work occurs only when explicitly invoked by deployment/test code.

## Documentation updates

State clearly:

- SQL Server TVP types are deployment artifacts, not lazily created query-time objects.
- Apply `InquiryGeneratedSchema.Ddl` for complete initial setup, or `ProviderArtifactsDdl` as an additive migration before deploying TVP-using code.
- Run `ProviderArtifactsValidationSql` during startup/deployment health checks if desired.
- Missing types fail at command execution; Inquiry does not auto-create them.
- Types are schema-qualified and signature-versioned; new signatures create new additive artifacts.
- Runtime binding remains constant-SQL/one-parameter and I/O-free.
- Exact column metadata/streaming, stored-procedure TVPs, and cardinality benchmarks remain tracked by #197, #188, and #87.

Remove any documentation that promises first-use/query-time TVP provisioning.

## Validation commands

```powershell
dotnet test tests/Inquiry.Generators.Tests/Inquiry.Generators.Tests.csproj -c Release --no-restore
dotnet test tests/Inquiry.SqlServer.Tests/Inquiry.SqlServer.Tests.csproj -c Release -f net8.0 --no-restore
dotnet test tests/Inquiry.SqlServer.Tests/Inquiry.SqlServer.Tests.csproj -c Release -f net9.0 --no-restore
dotnet test tests/Inquiry.SqlServer.Tests/Inquiry.SqlServer.Tests.csproj -c Release -f net10.0 --no-restore

dotnet test tests/Inquiry.Tests/Inquiry.Tests.csproj -c Release --no-restore
dotnet build Inquiry.slnx -c Release --no-restore
dotnet test Inquiry.slnx -c Release --no-build
dotnet publish tests/Inquiry.AotSmoke/Inquiry.AotSmoke.csproj -c Release
dotnet pack Inquiry.slnx -c Release --no-build
git diff --check
```

Final source/generated-output audit:

```powershell
rg -n "EnsureType|EnsuredTypes|ConcurrentDictionary|CREATE TYPE|TYPE_ID|command\.Connection" src/Inquiry.SqlServer/Parameters/InquiryTvpParameter.cs
rg -n "InquiryTvpParameter\.Bind" tests -g "*.cs"
```

The first command must return no runtime-binder provisioning/cache hits. `CREATE TYPE`/`TYPE_ID` should appear only in generated schema/setup output and its tests.
