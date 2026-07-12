# Schema primitives plan (#175 slice C)

> **Boundary revision (2026-07-12):** This slice intentionally stopped at normalized private schema data and emitted DDL. The final #175 plan in `2026-07-12-schema-manifest-contract-p0.md` now owns deterministic JSON, fingerprint, and assembly-metadata transport for that normalized graph so #72 can consume an expected-schema contract. The earlier “no JSON/generated manifest constant” statement applied to slice C implementation scope and is superseded for final #175 only; live comparison, catalog readers, query manifests, and drift diagnostics remain #72.

## Goal

Add repeatable composite, unique, and covering indexes; table check constraints; and named foreign-key
referential actions to generated baseline DDL without breaking the existing single-column schema API or
provider extension surface. Normalize legacy and new declarations into one internal constraint model so
inline and deferred DDL cannot diverge and the following #72 schema-manifest slice has stable metadata to
consume.

This slice does not add composite foreign keys, filtered/expression/descending indexes, unique constraints
as a distinct public primitive, a migration engine, schema drift comparison, or a public/runtime schema
manifest. Slice D owns manifest emission and #72 drift validation.

## Frozen public API

### Repeatable indexes

Add a public sealed class-level attribute:

```csharp
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class InquiryIndexAttribute : Attribute
{
    public InquiryIndexAttribute(params string[] properties);

    public string? Name { get; set; }
    public bool IsUnique { get; set; }
    public string[] Include { get; set; } = [];
}
```

`properties` and `Include` contain CLR mapped property names, so declarations can use `nameof`. The
generator resolves them to physical column names before rendering. Key-property order is significant;
include-property declaration order is preserved for deterministic output. `Include` means non-key covering
columns and must never be emulated by appending columns to the index key.

Existing `[InquiryColumn(IsIndexed = true)]`, `IsUnique`, and `IndexName` remain supported with their
current behavior and short `IX_<table>_<column>` / `UX_<table>_<column>` names. Do not rename, hash, or
otherwise change their emitted DDL in this slice.

### Repeatable checks

Add a public sealed class-level attribute:

```csharp
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class InquiryCheckAttribute : Attribute
{
    public InquiryCheckAttribute(string expression);

    public string? Name { get; set; }
}
```

The expression is raw provider SQL over physical column identifiers. Like `Computed`, `SqlType`, and
`DefaultExpression`, it is a compile-time escape hatch: Inquiry does not parse, quote, translate, or accept
runtime input for it. Cross-provider applications are responsible for choosing an expression accepted by
each target dialect.

### Foreign-key actions and names

Extend the existing property-level `InquiryForeignKeyAttribute` with additive named properties only:

```csharp
public string? ConstraintName { get; set; }
public InquiryReferentialAction OnDelete { get; set; }
public InquiryReferentialAction OnUpdate { get; set; }
```

Add the public enum:

```csharp
public enum InquiryReferentialAction
{
    NoAction,
    Restrict,
    Cascade,
    SetNull,
    SetDefault,
}
```

Both actions default to `NoAction`, preserving current source and DDL semantics. Keep both shipped
`InquiryForeignKeyAttribute` constructors unchanged. Use `ConstraintName`, not `Name`, because the
attribute inherits the existing read-only local column `Name` from `InquiryColumnAttribute`.

There is no composite-foreign-key public attribute in this slice. Composite foreign keys remain future
work and must not be approximated by correlating multiple property attributes.

## Compatibility contract

- Do not add members to public `IColumn`.
- Keep the public `SqlBuildContext` constructor binary and source compatible. Carry normalized schema
  primitives through an internal schema-emission construction path or additive internal properties.
- Keep existing column-index flags and explicit names byte-for-byte stable in generated DDL.
- Normalize legacy column indexes and property foreign keys into the same internal records used by new
  declarations; do not retain parallel rendering implementations.
- Existing unnamed, acyclic foreign keys may remain unnamed. Emit a name when the user supplies
  `ConstraintName` or when slice B requires a deterministic name for deferred cyclic `ALTER TABLE` DDL.
- `GenerateForeignKeys = false` suppresses property foreign-key DDL and its cycle edge, including actions
  and requested names. It does not suppress indexes or checks.
- Views emit no indexes, checks, or foreign keys because Inquiry does not own their DDL.
- Preserve existing phase order: provider artifacts, all tables with inline checks/foreign keys, deferred
  cyclic foreign keys, then indexes.

## Internal normalized model

Add value-equatable internal records using strings, primitives, `EquatableArray<T>`, and `LocationData`:

### `IndexData`

- local schema and table;
- ordered property and resolved physical key-column identities;
- ordered property and resolved physical include-column identities;
- unique flag;
- requested name, emitted physical name, and whether the name is legacy;
- length-delimited canonical identity;
- declaration location and origin (`ColumnFlag` or `TableAttribute`).

Legacy flags normalize to one single-column record. `IsUnique = true` retains the current unique-index
behavior; when both legacy booleans are true, normalize to one unique index as today.

### `CheckConstraintData`

- local schema and table;
- raw expression;
- requested and emitted physical name;
- length-delimited canonical identity;
- class-attribute location.

### `ForeignKeyConstraintData`

Extend the slice B record with requested name, emitted name, `OnDelete`, and `OnUpdate`. Retain local and
referenced schema/table/column, canonical identity, property location, and cyclic/deferred classification.
The same record must render inline `CREATE TABLE` and deferred `ALTER TABLE` constraints.

Add `EquatableArray<IndexData>`, `EquatableArray<CheckConstraintData>`, and normalized foreign keys as
init-only body properties on `EntityData`, matching the existing additive model convention. `ColumnData`
may retain its legacy `IColumn` facts for store lints/provider compatibility, but schema emission consumes
the normalized collections.

Build canonical identities from length-delimited fields, not delimiter-joined user input. Class indexes
and checks receive deterministic ASCII, hash-suffixed generated names no longer than 63 UTF-8 bytes, with
readable prefixes truncated only at complete UTF-8 scalar boundaries. Explicit names remain exact after
validation. Legacy column-index names are exempt from the new hash naming rule to preserve compatibility.

## Discovery and normalization

1. Discover repeatable class attributes during entity processing and retain the attribute syntax location.
2. Resolve every index property through the entity's CLR-property map, then store the resolved physical
   columns in declaration order.
3. Normalize each legacy column index into `IndexData`; normalize each class index after property
   resolution; reject invalid or duplicate records before emission.
4. Normalize checks without interpreting the expression. Whitespace-only expressions are invalid; do not
   otherwise rewrite expression text.
5. Populate action/name fields when normalizing every property foreign key. Perform action precondition and
   provider-capability validation before adding it to the cycle graph or emitter.
6. Run slice B SCC analysis over the validated normalized foreign-key set. An invalid FK is excluded from
   inline DDL, deferred DDL, and graph edges.
7. Render table checks and inline foreign keys from the normalized records, deferred cyclic foreign keys
   from those same records, and normalized indexes in the final index phase.

## Provider matrix

| Primitive | SQLite | SQL Server | PostgreSQL | MySQL | MariaDB | Oracle |
|---|---|---|---|---|---|---|
| Composite / unique index | Yes | Yes | Yes | Yes | Yes | Yes |
| Covering `INCLUDE` | Error | Yes | Yes | Error | Error | Error |
| Check constraint | Yes | Yes | Yes | Yes | Yes | Yes |
| `ON DELETE CASCADE` | Yes | Yes | Yes | Yes | Yes | Yes |
| `ON DELETE SET NULL` | Yes | Yes | Yes | Yes | Yes | Yes |
| `ON DELETE SET DEFAULT` | Yes | Yes | Yes | Error | Error | Error |
| `ON DELETE RESTRICT` | Yes | Error | Yes | Yes | Yes | Error |
| `ON UPDATE CASCADE` | Yes | Yes | Yes | Yes | Yes | Error |
| `ON UPDATE SET NULL` | Yes | Yes | Yes | Yes | Yes | Error |
| `ON UPDATE SET DEFAULT` | Yes | Yes | Yes | Error | Error | Error |
| `ON UPDATE RESTRICT` | Yes | Error | Yes | Yes | Yes | Error |

`NoAction` emits no clause and preserves each engine's default behavior. Do not silently translate
`Restrict` to `NoAction`: timing and provider semantics are not universally equivalent. MySQL and MariaDB
must reject `SetDefault` for the supported InnoDB path. Oracle supports only `ON DELETE CASCADE` and
`ON DELETE SET NULL`; every non-default update action and unsupported delete action is an error.

Action preconditions are provider-independent:

- `SetNull` requires the local foreign-key property to be nullable.
- `SetDefault` requires an emitted `DefaultExpression` on the local column. `UseDatabaseDefault` alone is
  insufficient because it changes write omission but does not put a default into generated baseline DDL.
  The generator cannot prove arbitrary raw default compatibility, but it must reject a missing expression.
- Actions require an emitted FK; when `GenerateForeignKeys = false`, do not emit an action-only warning.

Add explicit provider capability seams for include columns and referential actions. Safe base defaults
report diagnostics; every bundled provider declares its supported set. Keep quoting and SQL spelling in
the builders, not in entity discovery.

## Diagnostics

Allocate new IDs after `INQ070`. All invalid declarations are errors enabled by default and located on the
exact property argument, named argument, or class attribute when Roslyn exposes that location. Emit one
primary diagnostic per invalid declaration and exclude it from DDL.

### Index diagnostics

- no key properties;
- null, empty, duplicate, unknown, unmapped, or navigation property in the key list;
- null, empty, duplicate, unknown, unmapped, or navigation property in `Include`;
- an included property also appears in the key;
- provider does not support include columns;
- duplicate canonical index declaration, including a class declaration duplicating a legacy flag;
- duplicate physical name on the same schema/table;
- explicit name empty, invalid for the provider, or over its identifier byte limit.

Do not reject legitimate overlapping indexes merely because they share a prefix. A future advisory lint may
identify redundant overlaps; it is not a correctness error for this slice.

### Check diagnostics

- null, empty, or whitespace expression;
- duplicate canonical expression on the same table;
- duplicate physical name on the same schema/table;
- explicit name empty, invalid for the provider, or over its identifier byte limit.

The generator does not SQL-parse raw checks. Provider syntax/type errors beyond these structural checks are
verified by live DDL tests and otherwise remain database errors.

### Foreign-key diagnostics

- empty/invalid explicit constraint name or a physical-name collision;
- provider does not support the requested delete/update action;
- `SetNull` on a non-nullable local property;
- `SetDefault` without a mapped database default;
- duplicate physical FK declarations if normalization discovers the same local/reference identity twice.

Retain existing FK target/type/length diagnostics and slice B `INQ069`. New errors are property-located on
the existing `[InquiryForeignKey]` declaration. Invalid FKs do not participate in cycle analysis.

Generalize the opt-in missing-FK-index lint to consider whether an index begins with the FK column. For the
current single-column FK API, any normalized index whose first key column is the FK column satisfies the
lint; include-only coverage does not.

## Provider rendering

- Add normalized index rendering that supports multiple quoted key columns, optional `UNIQUE`, and
  provider-specific `INCLUDE` syntax for SQL Server/PostgreSQL. Preserve the existing single-column legacy
  renderer output and name exactly.
- Emit checks as table constraints inside `CREATE TABLE`, with `CONSTRAINT <name>` using the explicit or
  deterministic generated name.
- Render FK name and action clauses in one shared constraint-body path used by both inline and deferred
  statements. The only difference between them is the surrounding `CREATE TABLE` line versus
  `ALTER TABLE ... ADD` wrapper.
- Quote every table, column, index, and constraint identifier through provider hooks. Never quote or rewrite
  raw check/default expressions.
- Keep baseline run-once semantics from slice B. Do not add catalog guards or procedural idempotency.

## Metadata design for #72 slice D

Design and retain sufficient normalized data now, but do not emit a public manifest in this slice. Slice D
must be able to serialize:

- stable canonical identity and declaration kind;
- logical property names and resolved physical schema/table/column names;
- explicit versus generated physical object name;
- ordered index keys and include columns plus uniqueness;
- check expression verbatim;
- FK local and referenced identities, actions, constraint name, and inline/deferred emission mode;
- relevant normalized column facets for FK/check comparison: physical type, nullability, length,
  precision/scale, and default expression;
- provider/dialect identity and capability-driven emission state.

Canonical identity must be independent of source discovery order. Manifest comparison should be able to
match canonical identity separately from physical name so slice D can report object renames versus semantic
changes. No JSON, generated manifest constant, catalog reader, or drift diagnostic is added here.

## Tests

### Public API and generator tests

- compile existing `InquiryColumn` index flags and both existing `InquiryForeignKey` constructors unchanged;
- repeatable two- and three-column indexes, unique indexes, explicit names, and deterministic generated names;
- covering indexes render `INCLUDE` on SQL Server/PostgreSQL and report exact class-located errors on the
  other four providers;
- legacy single-column index SQL and names remain byte-identical;
- class/legacy duplicate index detection, name collisions, unknown/duplicate/include-overlap properties,
  empty key list, and long ASCII/multibyte names;
- multiple named and generated checks, raw expression preservation, duplicates, empty expressions, and
  identifier validation on every provider;
- every supported FK delete/update action per provider and every unsupported matrix cell;
- `SetNull` nullability and `SetDefault` default preconditions;
- explicit FK names inline and in cycles; unnamed acyclic legacy FK remains compatible;
- cyclic named/action FKs appear once after all tables and retain actions; self-reference stays inline;
- `GenerateForeignKeys = false` removes the normalized FK edge and emits no action diagnostic;
- reordered source declarations yield byte-identical class-object names and DDL;
- a test-only builder using safe base capability defaults reports diagnostics instead of emitting unsupported
  syntax;
- generated internal metadata records are value-equatable and contain all fields reserved for slice D.

### Live generated-DDL matrix

Add shared feature-catalog entities for:

- a composite index, a unique composite index, and (for SQL Server/PostgreSQL) a covering index;
- multiple checks, with one valid row and one rejected row per check;
- named FKs exercising every action that can be proven without destructive fixture coupling, including
  delete cascade, delete set-null, and update actions where supported.

Execute extracted generated DDL subsets in the existing isolated harnesses so unrelated feature-catalog DDL
cannot mask the primitive under test. Introspect catalogs to verify ordered keys/includes, uniqueness,
constraint names, checks, and actions, then perform DML that proves enforcement. Unsupported declarations
belong in generator diagnostic tests, not live assemblies that must compile for every provider.

Run the focused live catalog/enforcement suite on SQLite, SQL Server, PostgreSQL, MySQL, MariaDB, and Oracle
for net8.0, net9.0, and net10.0. Preserve existing container versions and statement splitters; Oracle must
continue executing one statement at a time. Do not weaken unavailable live engines to snapshot-only tests.

## Exact implementation areas

- `src/Inquiry/Entities/InquiryIndexAttribute.cs` (new)
- `src/Inquiry/Entities/InquiryCheckAttribute.cs` (new)
- `src/Inquiry/Entities/InquiryReferentialAction.cs` (new)
- `src/Inquiry/Entities/InquiryForeignKeyAttribute.cs`
- new normalized records under `src/Inquiry.Generators.Shared/Models/`
- `src/Inquiry.Generators.Shared/Models/EntityData.cs`
- `src/Inquiry.Generators.Shared/EntityProcessor.cs`
- `src/Inquiry.Generators.Shared/SchemaEmitter.cs`
- `src/Inquiry.Generators.Shared/Abstractions/SqlBuilder.cs` and internal schema context path
- all six provider builders for explicit include/action capabilities and rendering
- `src/Inquiry.Generators.Shared/Diagnostics/InquiryDiagnosticDescriptors.cs`
- focused generator tests, shared feature-catalog fixtures, and all six live provider projects
- schema-DDL, entity-mapping, provider, project-status, and roadmap documentation as applicable

Do not edit `IColumn` or break the public `SqlBuildContext` constructor.

## Verification and release gates

1. Focused API, normalization, diagnostic, naming, action, and rendering generator tests on net8/net9/net10.
2. Full generator suite, including byte-stability assertions for legacy index/FK DDL.
3. Focused generated-DDL catalog and enforcement tests for all six providers on all three target frameworks.
4. Existing schema DDL/index/cycle, FK length, Northwind, provider generated-DDL, and schema-fidelity tests.
5. Full runtime tests, release solution build, package build/pack validation, and DocFX build with no warnings.
6. `git diff --check` and an API-compatibility review proving existing constructors, attributes, `IColumn`, and
   `SqlBuildContext` remain compatible.
7. Final source audit: one normalized rendering path per primitive; no unsupported include/action syntax;
   no invalid record reaches DDL or cycle analysis; legacy short index names unchanged; every new generated
   class index/check name is deterministic and at most 63 UTF-8 bytes; slice D metadata is retained but no
   manifest is emitted.
8. Independent adversarial review of public API ambiguity, provider semantics, identifier edge cases,
   duplicate detection, raw-expression boundaries, and action enforcement.
9. Publish the slice PR into `prerelease`, wait for Copilot review, resolve every actionable comment, rerun
   affected focused and regression tests, then complete the release build/pack/docs gates before merge.
