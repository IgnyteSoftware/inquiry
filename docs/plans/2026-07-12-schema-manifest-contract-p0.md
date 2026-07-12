# Schema manifest contract P0 implementation plan

## Goal and ownership boundary

Emit a deterministic, versioned, provider-rendered expected-schema manifest from the exact normalized graph that produces `InquiryGeneratedSchema.Ddl`. This completes #175's handoff to #72.

#175 owns expected schema facts and transport. #72 owns query-command manifests, database introspection, live/offline comparison policy, CLI/MSBuild/test helpers, PREPARE/EXPLAIN/describe adapters, refresh workflow, and user-facing drift diagnostics. #79 remains reverse scaffolding, #73 remains user-facing explain plans, and #184 remains reusable provider conformance infrastructure.

This plan explicitly revises the earlier slice-C boundary in `2026-07-12-schema-primitives-p0.md`, which excluded JSON/manifest transport while primitives were being implemented. The normalized model remains private as that plan required, but final #175 now owns serialization and transport so #72 has a stable expected-schema input. Update #175 and #72 acceptance text/comments when this contract lands.

## Generated transport

Preserve all existing generated constants and add to the internal `Inquiry.Generated.InquiryGeneratedSchema` class:

- `SchemaManifestFormatVersion = 1`;
- `SchemaManifestJson`;
- `SchemaManifestSha256` (lowercase hexadecimal SHA-256 of the exact UTF-8 bytes of `SchemaManifestJson`);
- `SchemaManifestChunkCount`.

Also emit the exact JSON in deterministic assembly metadata chunks so tooling can read it from PE metadata without loading user code or reflecting an internal generated type:

- `Inquiry.SchemaManifest.FormatVersion`;
- `Inquiry.SchemaManifest.Sha256`;
- `Inquiry.SchemaManifest.ChunkCount`;
- `Inquiry.SchemaManifest.Chunk.0000`, `0001`, ...

Use BCL `AssemblyMetadataAttribute`; do not introduce a public DTO assembly or runtime initializer. The maximum chunk payload is exactly 12 KiB (12,288 UTF-8 bytes). Chunk keys use four zero-padded decimal digits (`0000` through `9999`); exceeding 10,000 chunks produces a build diagnostic rather than truncated metadata. Split only between complete Unicode scalar encodings. Reassembly must byte-match `SchemaManifestJson`. Analyzer/package-consumer tests must prove the metadata survives packing.

## Canonical JSON v1

Write JSON with one generator-owned UTF-8 writer; do not use reflection serialization or runtime JSON dependencies. Property order and token spelling are part of the v1 contract. Unknown additive fields may be ignored; removing/renaming/changing a field or token requires format v2.

Top level:

- `formatVersion`: integer 1;
- `providerId`: the same stable lowercase ASCII provider id used by computed overrides, matching `[a-z][a-z0-9.-]{0,63}` (`sqlite`, `sqlserver`, `postgresql`, `mysql`, `mariadb`, `oracle`, or a third-party id); `DialectName` remains display-only;
- `tables`: canonical array;
- `providerArtifacts`: canonical array.

The manifest contains semantic schema only. Machine paths, timestamps, current culture, generator build number, CLR property names, and source order that does not affect physical schema are forbidden. The exact JSON is therefore the semantic fingerprint input; no circular hash field appears inside it.

### Tables

Sort by canonical `(schema, table)` using the provider's physical identifier comparison/normalization contract, with ordinal canonical identity as a deterministic tie-breaker. Include only the selected `GenerateDdl=true` representative used by DDL emission. Views, opted-out mappings, invalid mappings, and suppressed duplicate declarations never appear.

Each table contains:

- nullable raw physical `schema` and physical `name`;
- `columns` in physical declaration order;
- `primaryKey` as ordered physical column names or null;
- `indexes`, `checks`, and `foreignKeys` in canonical identity order.

### Columns

- physical `name`;
- nullable final emitted `storeType` through a dedicated builder seam;
- `typeInference`: `explicit` when DDL emits `storeType`, otherwise `database` for computed forms whose type is inferred by the engine;
- stable lowercase portable `typeClass` token;
- `nullable`;
- nullable `primaryKeyOrdinal`;
- stable `generation` token: `none`, `identity`, `rowversion`, `computed`, or `default`;
- final provider-rendered physical `defaultExpression` or null;
- final provider-rendered physical `computedExpression` or null;
- stable `concurrency` token: `none`, `application`, or `database`.

Do not serialize CLR display type/property names. Provider-rendered facets must distinguish length, unicode/ANSI, precision/scale, explicit SQL type, and native rowversion. Generation roles are validated as mutually exclusive before manifest construction. The only precedence used defensively is `rowversion` > `computed` > `identity` > `default` > `none`; encountering more than one applicable role is a build diagnostic and the column/entity is suppressed rather than serialized ambiguously. `identity` means the same generated-key shape that emits identity/auto-increment DDL. `default` means a normal writable column with an emitted default expression. The final-type seam returns literal `ROWVERSION` for SQL Server native tokens even though ordinary `ColumnType` is bypassed; it returns null/`database` for SQL Server, SQLite, and Oracle computed forms whose DDL omits a declared type, and the actual emitted type/`explicit` for PostgreSQL/MySQL-family computed forms.

The declared raw expression is diagnostic provenance, not physical schema identity, and is not included in the semantic v1 JSON. Rendering happens once during provider normalization; DDL and manifest consume that same final value. In particular, a SQL Server declaration using `||` records the rendered `+` expression.

### Indexes

- emitted physical `name`;
- `unique`;
- ordered physical `keyColumns`;
- ordered physical `includeColumns`.

### Checks

- emitted physical `name`;
- final provider-rendered physical `expression`.

### Foreign keys

- emitted physical `name` or null when the provider intentionally emits no constraint name inline;
- ordered `localColumns`;
- referenced raw `schema`, `table`, and ordered `columns`;
- stable lowercase `onDelete` and `onUpdate` tokens.

Inline versus deferred emission is DDL mechanics and must not change manifest semantics or hash. Suppressed/invalid FKs never appear.

### Provider artifacts

- raw `schema` and `name`;
- stable lowercase `kind`;
- stable provider `signature` derived from existing artifact semantic identity (for SQL Server TVPs, element signature), never create/validation SQL.

## Internal architecture

1. Add symbol-free manifest records separate from `EntityData`/`ColumnData`/`IndexData`; those generator internals do not become public compatibility types.
2. Construct the manifest only after `GenerateDdl` filtering, deterministic physical owner selection, length resolution, expression validation/provider rendering, primitive normalization, FK target/cycle validation, collision suppression, and emitted-name resolution.
3. DDL and manifest consume the same final normalized schema object graph. Avoid rebuilding facts independently from source attributes. A referenced table absent from the assembly is an intentional external target and its FK remains in the manifest; suppress only an FK whose table is known in-assembly but whose referenced column is absent from the selected canonical mapping. Existence of external targets is #72 catalog validation.
4. Add builder seams for stable provider id, final emitted store type/type-inference mode, physical identifier canonicalization where required, and provider-artifact semantic signature. A new provider should implement seams without editing schema orchestration.
5. Canonical JSON escaping must cover control characters, quotes, backslashes, and Unicode deterministically on netstandard2.0. Valid scalar values may be emitted as canonical UTF-8. Any unpaired UTF-16 surrogate is emitted as a lowercase `\uXXXX` escape before UTF-8 hashing, so replacement-character behavior can never vary by framework. Chunking occurs only at resulting UTF-8 scalar/escape boundaries.
6. Hash with SHA-256 over exact UTF-8 canonical JSON bytes. No culture-sensitive formatting.
7. Artifact-only assemblies still emit an empty-table manifest, hash, constants, and metadata chunks when provider artifacts exist, matching current `SchemaEmitter` behavior.

## Tests

### Contract/generator tests on net8.0/net9.0/net10.0

- checked-in v1 JSON schema/spec and golden sample;
- golden manifest for all six dialects covering types/facets, defaults, selected computed expressions, rowversion, indexes/includes, checks, FK names/actions, cyclic deferred constraints, and provider artifacts;
- semantic coverage invariants: every final normalized emitted table/column/index/check/FK/artifact appears exactly once and every suppressed fact appears zero times; raw DDL mechanics such as guards, schema creation, and inline/deferred placement are intentionally not mirrored;
- byte-identical JSON/hash/chunks under entity/attribute discovery reorder that does not change physical column/key order, equivalent duplicate mappings, culture/timezone changes, and all three TFMs. Physical column/key order is semantic; changing it intentionally changes JSON and the hash;
- `GenerateDdl=false`, invalid duplicate owner, known in-assembly missing target column, retained unresolved external FK target, name collision, unsupported action/expression, and cyclic suppression cases;
- hash changes for every semantic drift dimension and does not change for inline/deferred placement or CLR-only mapping differences;
- SQL Server `||` declaration produces one rendered `+` expression shared by DDL and manifest; future default/check provider transforms obey the same single-render rule;
- chunk reassembly and PE assembly-metadata readback, including 12,288-byte boundaries, 1/2/3/4-byte UTF-8 scalars, unpaired surrogates, large multi-chunk manifests, and the 10,000-chunk diagnostic;
- artifact-only assembly with zero tables;
- unknown third-party dialect stable normalization.

### Live contract proof

For each provider and TFM, create the schema from generated DDL and use existing test introspection helpers (or narrow test-only queries) to prove manifest facts for representative columns/types/nullability/keys/indexes/checks/FKs/actions. This is contract proof only, not the reusable comparator owned by #72.

### Packaging/compatibility

- package-consumer project proves generated constants and assembly metadata are present when consuming each provider package;
- API compatibility remains unchanged except generated internal constants;
- NativeAOT smoke remains green because transport has no runtime initializer/reflection dependency;
- full solution build/test, DocFX, pack, adversarial review, Copilot review.

## Documentation

Extend the schema-DDL page with:

- manifest purpose and v1 compatibility rules;
- exact semantic/non-semantic boundary;
- assembly metadata keys and reassembly;
- fingerprint usage;
- explicit statement that Inquiry does not yet compare/apply schemas in this slice and #72 owns validation tooling.

Update #175 when merged and add the manifest handoff details to #72. Do not add a parallel roadmap item; the existing build-time SQL validation item already owns #72.

## Non-goals

- live database access or schema diff/apply;
- query/store-method command manifests;
- offline metadata refresh;
- source-path provenance in the semantic hash;
- migration generation;
- freezing generator-internal records as public runtime APIs.
