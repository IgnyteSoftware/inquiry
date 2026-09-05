using Microsoft.CodeAnalysis;

namespace Inquiry.Generators.Diagnostics;

internal static class InquiryDiagnosticDescriptors
{
    // ---------------------------------------------------------------------------------------------
    // DIAGNOSTIC-ID REGISTRY
    //
    // IDs in use:      INQ001, INQ002, INQ004–INQ012, INQ014, INQ016, INQ017, INQ018–INQ023,
    //                  INQ024–INQ026, INQ028–INQ032, INQ035–INQ041, INQ042, INQ043, INQ044,
    //                  INQ045–INQ075, INQ077–INQ086, INQ087–INQ096. INQ076 is owned by SQL Server.
    // Retired (do NOT reuse, keeps existing IDs stable): INQ003, INQ013, INQ015, INQ027 (projection
    //   on soft-delete, removed in P3 #14 — now supported).
    //
    // RESERVED RANGES for in-flight feature workstreams so parallel branches do not collide on the
    // next free ID. Claim from your reserved block; if you need more, extend past INQ040 and update
    // this table in the same commit.
    //   INQ018–INQ019  Richer WHERE predicates      (e.g. bad IN collection, op/type mismatch)  [IN USE]
    //   INQ020–INQ021  ORDER BY + pagination         (paging requires ORDER BY, unknown order field) [IN USE]
    //   INQ022–INQ023  Batch & bulk operations       (INQ022 token entity, INQ023 set-based mutation needs [InquiryWhere]) [IN USE]
    //   INQ024–INQ027  Projections + aggregations    (INQ024 no columns, INQ025 not mapped, INQ026 entity mismatch; INQ027 retired in P3 #14) [IN USE]
    //   INQ028–INQ029  Optimistic concurrency        (INQ028 >1 token, INQ029 token==key)  [IN USE]
    //                       (DB-managed-on-unsupported-dialect and upsert+token reuse INQ006 at emit time, per convention)
    //   INQ030–INQ032  Migrations / schema DDL       (INQ030 generated key not integer, INQ031 string key needs Length, INQ032 indexed string needs Length) [IN USE]
    //   INQ033–INQ034  Soft deletes
    //   INQ035         Full-text search              (unsupported by dialect)
    //   INQ036–INQ038  JSON/array/value-converter column types
    //   INQ039         Operation unsupported by the active dialect (error; explicit lowering enables a stub) [IN USE]
    //   INQ045–INQ046  Ad-hoc DTO materialization    (INQ045 no mappable properties, INQ046 not constructible) [IN USE]
    //   INQ047         Sequential GUID key           (SequentialGuid on non-Guid / generated / db-default key) [IN USE]
    //   INQ048         Raw-SQL injection lint        (non-constant command text passed to InquiryCommand) [IN USE]
    //   INQ049–INQ050  Auditing timestamps           (INQ049 invalid type/placement, INQ050 duplicate) [IN USE]
    //   INQ051         Stored-procedure scalar output (OutputParameter/ReturnsValue misconfiguration) [IN USE]
    //   INQ052         View read-only violation       (mutation op on an [InquiryView] entity) [IN USE]
    //   INQ053         Key-requiring op, keyless entity (key-based select/eager on a keyless view) [IN USE]
    //   INQ054         Derived query name             (field-less [InquirySelectAllByField] name has no 'By…') [IN USE]
    //   INQ055–INQ056  Auditing user columns          (INQ055 invalid type/placement, INQ056 duplicate) [IN USE]
    //   INQ057         Server-computed column          (Computed combined with key/default/audit/etc.) [IN USE]
    //   INQ059         Global query filter             ([InquiryGlobalFilter] on non-bool / key / generated / token / soft-delete) [IN USE]
    //   INQ060         JSON-path predicate             ([InquiryWhere(JsonPath=…)] on non-string / converter column, or malformed path) [IN USE]
    //   INQ061–INQ064  DDL safety lints (off by default) (INQ061 unindexed FK, INQ062 decimal w/o precision, INQ064 unindexed filter column; opt in via .editorconfig) [IN USE]
    //   INQ063         Many-to-many relation          ([InquiryManyToMany] not on a collection navigation) [IN USE]
    //   INQ065         Column metadata range          ([InquiryColumn] Length/Precision/Scale out of range) [IN USE]
    //   INQ066–INQ067  DDL safety lints (off by default) (INQ066 nullable column with default, INQ067 unbounded string column; opt in via .editorconfig) [IN USE]
    //   INQ068         Invalid database-generated concurrency-token shape [IN USE]
    //   INQ069         Provider cannot emit a cyclic foreign-key constraint [IN USE]
    //   INQ070         Duplicate/colliding physical schema mapping [IN USE]
    //   INQ071–INQ077  Provider schema and artifact validation [IN USE]
    //   INQ078–INQ082  Value-converter model and construction validation [IN USE]
    //   INQ083         Paged-result + Distinct conflict [IN USE]
    //   INQ086         Stored-procedure TVP binding     (collection param on sproc: missing TvpTypeName, unsupported provider, or type mapping failure) [IN USE]
    //   INQ087–INQ089  Many-to-many configuration     (INQ087 junction/related type unmapped, INQ088 named junction FK not a mapped column, INQ089 child FKs do not pair with the related key) [IN USE]
    //   INQ091         Ignore-filter bypass            ([InquiryIgnoreFilter] names an unknown/unnamed filter, or sits on an operation that composes no filters) [IN USE]
    //   INQ092         Global-filter name              ([InquiryGlobalFilter] Name blank, or duplicated across the entity's filters) [IN USE]
    //   INQ093         Parameterized filter            ([InquiryGlobalFilter] ContextKey blank/conflicting/unbindable, or on SQL the binder cannot cover) [IN USE]
    //   INQ094         Index property resolution       ([InquiryIndex] key or Include naming something that is not a mapped column) [IN USE]
    //   INQ095         Write-enforced filter           (an operation that cannot honour [InquiryGlobalFilter(EnforceOnWrites = true)] — upsert) [IN USE]
    //   INQ096         Mutation target                 (targetless delete, conflicting all/predicate, or unsupported returning mode) [IN USE]
    // ---------------------------------------------------------------------------------------------


    public static readonly DiagnosticDescriptor EntityKeyCount = new(
        "INQ001",
        "Entity must have at least one InquiryKey property",
        "Entity '{0}' must have at least one InquiryKey property.",
        "Inquiry",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor DuplicateColumn = new(
        "INQ002",
        "Entity contains duplicate mapped column names",
        "Entity '{0}' maps multiple properties to column '{1}'.",
        "Inquiry",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor StoreMustBePartial = new(
        "INQ004",
        "Store class must be partial",
        "Store class '{0}' must be partial.",
        "Inquiry",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnsupportedReturnType = new(
        "INQ005",
        "Query method return type is not supported",
        "Query method '{0}' has unsupported return type '{1}'.",
        "Inquiry",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor InvalidParameters = new(
        "INQ006",
        "Query method parameter list is invalid",
        "Query method '{0}' has an invalid parameter list.",
        "Inquiry",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnknownField = new(
        "INQ007",
        "SelectByField references an unmapped property or column",
        "Query method '{0}' references unmapped field '{1}'.",
        "Inquiry",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor StoreEntityNotMapped = new(
        "INQ008",
        "Store entity type is not mapped with InquiryTable",
        "Store class '{0}' uses entity '{1}', which is not mapped with InquiryTable.",
        "Inquiry",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor PropertyMustHavePublicSetter = new(
        "INQ009",
        "Mapped entity property must have an accessible setter",
        "Entity property '{0}.{1}' must have a public or internal setter to be mapped.",
        "Inquiry",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor MethodMustBePartial = new(
        "INQ010",
        "Query method must be a partial declaration",
        "Query method '{0}' must be declared 'partial' (the source generator supplies the body).",
        "Inquiry",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor CompositeKeyContainsGenerated = new(
        "INQ011",
        "Composite primary key cannot contain database-generated columns",
        "Entity '{0}' has a composite primary key that includes a database-generated column ('{1}'). Composite keys must be entirely client-supplied.",
        "Inquiry",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor EagerLoadingOnCompositeKeyParent = new(
        "INQ012",
        "Eager loading is not supported on a composite-key parent entity",
        "Query method '{0}' uses eager loading on entity '{1}', which has a composite primary key. The entity being eager-loaded (the parent) must have a single-column key; composite keys on the related entities are unaffected by this rule.",
        "Inquiry",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor DialectAmbiguous = new(
        "INQ014",
        "Multiple Inquiry SQL dialects are referenced",
        "Multiple Inquiry SQL dialects are referenced ({0}). Reference exactly one provider package or apply [assembly: InquiryDialect(\"<dialect>\")] to this assembly to disambiguate.",
        "Inquiry",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor DialectUnknown = new(
        "INQ043",
        "Unknown Inquiry SQL dialect",
        "Inquiry SQL dialect '{0}' is not provided by any referenced Inquiry provider. Use one of: {1}.",
        "Inquiry",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor StoreCannotBeNested = new(
        "INQ016",
        "Store class cannot be nested inside another type",
        "Store class '{0}' is nested inside '{1}'. The Inquiry source generator emits its partial at the namespace level, so stores must be top-level types.",
        "Inquiry",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor StoreCannotBeAbstract = new(
        "INQ017",
        "Store class cannot be abstract",
        "Store class '{0}' is declared abstract. The generator emits a concrete partial including the constructor, so the user-authored class must not be abstract or DI cannot instantiate it.",
        "Inquiry",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor PredicateInRequiresCollection = new(
        "INQ018",
        "InquiryWhere In operator requires a collection parameter of the column type",
        "Query method '{0}' uses Compare.In on field '{1}', which requires a single IEnumerable<T> parameter whose element type matches the column type.",
        "Inquiry",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor PredicateParameterMismatch = new(
        "INQ019",
        "InquiryWhere criteria do not match the method parameters",
        "Query method '{0}' has [InquiryWhere] criteria whose operators and parameters do not line up (check arity, parameter order, and that Like is applied to a string field).",
        "Inquiry",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor PagingRequiresOrderBy = new(
        "INQ020",
        "Paged query requires an ORDER BY and matching offset/limit (or pageSize) parameters",
        "Query method '{0}' uses pagination but is missing an ORDER BY clause or the expected paging parameters (offset paging needs OrderBy plus two int parameters; keyset paging needs a nullable cursor plus an int pageSize, and returns InquiryPage<TEntity, TCursor>).",
        "Inquiry",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnknownOrderField = new(
        "INQ021",
        "ORDER BY / keyset references an unmapped property or column",
        "Query method '{0}' orders by unmapped field '{1}'.",
        "Inquiry",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor BatchMutationUnsupportedWithConcurrencyToken = new(
        "INQ022",
        "Batch mutation is not supported for optimistic-concurrency entities",
        "Store method '{0}' uses a batch update/delete operation on entity '{1}', which has an InquiryConcurrencyToken. Use single-row update/delete methods so the token can be matched and advanced.",
        "Inquiry",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    // INQ023: an [InquiryUpdate]/[InquiryDelete] method with no [InquiryWhere] criteria would
    // mutate every row in the table. Whole-collection mutations use entity
    // collection parameters, so the unfiltered scalar form is rejected up front.
    public static readonly DiagnosticDescriptor PredicateMutationRequiresWhere = new(
        "INQ023",
        "Set-based mutation requires at least one InquiryWhere criterion",
        "Store method '{0}' uses a set-based update/delete with no [InquiryWhere] criteria. Add at least one valid [InquiryWhere] criterion.",
        "Inquiry",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor MutationTargetInvalid = new(
        "INQ096",
        "Mutation target is invalid",
        "Store method '{0}' has an invalid mutation target. {1}",
        "Inquiry",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor MultipleConcurrencyTokens = new(
        "INQ028",
        "Entity declares more than one InquiryConcurrencyToken column",
        "Entity '{0}' marks more than one property with [InquiryConcurrencyToken] (e.g. '{1}'). At most one concurrency token is allowed.",
        "Inquiry",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ConcurrencyTokenIsKey = new(
        "INQ029",
        "InquiryConcurrencyToken cannot also be the primary key",
        "Entity '{0}' marks key property '{1}' with [InquiryConcurrencyToken]. A concurrency token must be a non-key column.",
        "Inquiry",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor SoftDeleteUnsupportedType = new(
        "INQ033",
        "InquirySoftDelete column type is not supported",
        "Entity '{0}' marks property '{1}' with [InquirySoftDelete], but its type is not a supported soft-delete representation. Use a bool (flag) or a nullable DateTime/DateTimeOffset (timestamp).",
        "Inquiry",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor MultipleSoftDeleteColumns = new(
        "INQ034",
        "Entity declares more than one InquirySoftDelete column",
        "Entity '{0}' marks more than one property with [InquirySoftDelete] (e.g. '{1}'). At most one soft-delete column is allowed.",
        "Inquiry",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor FullTextSearchNotSupported = new(
        "INQ035",
        "Full-text search is not supported by the target dialect",
        "Query method '{0}' uses [InquiryFullTextSearch], which the current dialect does not support. Full-text search is available on PostgreSQL, SQL Server, and MySQL.",
        "Inquiry",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ProjectionNoColumns = new(
        "INQ024",
        "Projection declares no mapped columns",
        "Projection '{0}' declares no [InquiryColumn] properties. A projection must map at least one column.",
        "Inquiry",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ProjectionNotMapped = new(
        "INQ025",
        "Query method result type is not the entity or a known projection",
        "Query method '{0}' returns element type '{1}', which is neither the store's entity nor an [InquiryProjection] of it.",
        "Inquiry",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ProjectionEntityMismatch = new(
        "INQ026",
        "Projection targets a different entity than the store",
        "Query method '{0}' returns projection '{1}', which projects entity '{2}' — not the store's entity '{3}'.",
        "Inquiry",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    // INQ027 (ProjectionOnSoftDeleteEntity) was retired in P3 #14: projections on soft-delete entities
    // are now supported — the projection SELECT AND-composes the entity's soft-delete filter. The ID is
    // not reused (registry convention) so existing .editorconfig / suppression references stay stable.

    public static readonly DiagnosticDescriptor GeneratedKeyNotInteger = new(
        "INQ030",
        "Database-generated key must be an integer type",
        "Entity '{0}' marks key column '{1}' as database-generated (IsGenerated), but its type is not an integer. Generated keys map to IDENTITY/AUTOINCREMENT/SERIAL, which require an integer column; use a database default expression for non-integer generated values.",
        "Inquiry",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor StringKeyRequiresLength = new(
        "INQ031",
        "String key column requires a bounded Length for this dialect",
        "Entity '{0}' has string key column '{1}' whose Length is unset or beyond the '{2}' dialect's fixed-width limit, so it maps to an unbounded text column the dialect cannot make a primary key — generated DDL would fail. Set a bounded [InquiryColumn(Length = …)] within the dialect's limit.",
        "Inquiry",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor IndexedStringRequiresLength = new(
        "INQ032",
        "Indexed string column requires a bounded Length for this dialect",
        "Entity '{0}' has indexed string column '{1}' whose Length is unset or beyond the '{2}' dialect's fixed-width limit, so it maps to a LOB/MAX text type the dialect cannot index — the generated index is skipped. Set a bounded [InquiryColumn(Length = …)] within the dialect's limit to have the index created (or a foreign key inherits its referenced key's Length).",
        "Inquiry",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    // INQ061 (Info — a DDL "lint"): a foreign-key column with no index. Most engines do not auto-index
    // foreign keys, so joins and ON DELETE/UPDATE cascades over the column scan the table; an explicit
    // index is the standard remedy. Advisory severity (Info) so it never breaks a warnings-as-errors
    // build — opt into enforcement by raising INQ061 in .editorconfig. MySQL auto-indexes FK columns and
    // is exempt.
    public static readonly DiagnosticDescriptor UnindexedForeignKey = new(
        "INQ061",
        "Foreign-key column has no index",
        "Entity '{0}' foreign-key column '{1}' has no index. {2} does not auto-index foreign keys, so joins and cascades over it scan the table. Add IsIndexed = true to the column's [InquiryColumn]/[InquiryForeignKey] to index it.",
        "Inquiry",
        DiagnosticSeverity.Info,
        isEnabledByDefault: false);

    // INQ063: the [InquiryManyToMany] declaration itself is unusable — the property is not a collection,
    // or the attribute did not supply a junction type and at least one child foreign-key name. Discovery
    // cannot tell those two apart (it records both the same way), so the message names both rather than
    // asserting one and risking being wrong. Retains the ID it had when it bundled every many-to-many
    // failure, so existing suppressions keep working; the reasons it CAN distinguish moved to
    // INQ087-INQ089, which name the offending type, property, or arity.
    public static readonly DiagnosticDescriptor ManyToManyInvalid = new(
        "INQ063",
        "InquiryManyToMany declaration is unusable",
        "Entity '{0}' property '{1}' is marked [InquiryManyToMany], but the declaration is unusable. Apply it to a collection of the related entity (List<T>, IReadOnlyList<T>, …) — a many-to-many association is many-valued on both sides — and give [InquiryManyToMany] a junction type plus at least one child foreign-key property name.",
        "Inquiry",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    // INQ090: an auto-managed [InquiryManyToMany] (the parameterless form) cannot be synthesized. Every
    // reason is a case where synthesizing anyway would produce a table that is wrong rather than merely
    // unhelpful — a collision with a mapped entity's table, two sides disagreeing on the shape, a
    // self-referential pair whose columns collide, or a composite key the one-column-per-side naming
    // cannot express. Reason-parameterised rather than split further: they share one fix (map the
    // junction explicitly), and each states its own remedy in the message.
    public static readonly DiagnosticDescriptor AutoJunctionInvalid = new(
        "INQ090",
        "InquiryManyToMany cannot synthesize an auto-managed junction",
        "Entity '{0}' relation '{1}' uses an auto-managed [InquiryManyToMany], but the junction cannot be synthesized: {2}.",
        "Inquiry",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    // INQ092: a global-filter Name that breaks the bypass contract at the declaration site. A blank
    // name looks named but can never be matched; a duplicate makes one [InquiryIgnoreFilter] silently
    // drop MULTIPLE predicates — the ambiguous multi-term removal the named mechanism exists to avoid.
    public static readonly DiagnosticDescriptor GlobalFilterNameInvalid = new(
        "INQ092",
        "InquiryGlobalFilter Name is invalid",
        "Entity '{0}' property '{1}' has an invalid [InquiryGlobalFilter] Name: {2}.",
        "Inquiry",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    // INQ093: a runtime-parameterized [InquiryGlobalFilter] (ContextKey mode) that cannot bind. The
    // modes conflict (explicit KeepWhen), the key is blank, the column's type cannot be a bound
    // scalar (nullable), the column's role is owned by other machinery, or the filter reaches SQL the
    // binder cannot cover in this release (eager loaders). Reason-parameterised like INQ090/091.
    public static readonly DiagnosticDescriptor GlobalFilterContextKeyInvalid = new(
        "INQ093",
        "InquiryGlobalFilter ContextKey configuration is invalid",
        "Entity '{0}' property '{1}' has an invalid runtime-parameterized [InquiryGlobalFilter]: {2}.",
        "Inquiry",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    // INQ095: an operation that cannot honour a write-enforced [InquiryGlobalFilter]. Reason-
    // parameterised like INQ090/091/093 so later shapes can join without a new ID. The v1 reason is
    // upsert, rejected on EVERY dialect rather than "enforced where the dialect happens to allow it":
    // the insert branch is unfilterable, MySQL's ON DUPLICATE KEY UPDATE has no conditional form, and
    // SQL Server's UPDATE-first emulation fires its INSERT branch exactly when the filter blocked the
    // UPDATE (@@ROWCOUNT = 0) — a phantom cross-boundary insert or a duplicate-key error.
    public static readonly DiagnosticDescriptor WriteEnforcedFilterInvalid = new(
        "INQ095",
        "Operation cannot honour a write-enforced InquiryGlobalFilter",
        "Store method '{0}' cannot be generated: entity '{1}' declares [InquiryGlobalFilter(EnforceOnWrites = true)] on '{2}', and {3}.",
        "Inquiry",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    // INQ091: an [InquiryIgnoreFilter] that cannot bypass anything. Reason-parameterised like INQ090 —
    // an unknown or unnamed filter name, or an operation whose SQL never composes the entity's filters.
    // An error rather than a warning: a typo'd name silently returning FILTERED results is the
    // cross-tenant-read shape this attribute's compile-time contract exists to prevent, and an
    // ignored-but-ineffective attribute misleads the reader about what the method returns.
    public static readonly DiagnosticDescriptor IgnoreFilterInvalid = new(
        "INQ091",
        "InquiryIgnoreFilter cannot bypass the named filter",
        "Store method '{0}' is marked [InquiryIgnoreFilter(\"{1}\")], but {2}.",
        "Inquiry",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    // INQ087: the junction or related type named by [InquiryManyToMany] is not a mapped entity. Split out
    // of INQ063 so the message can name which type is unmapped rather than listing every possible cause.
    public static readonly DiagnosticDescriptor ManyToManyTypeNotMapped = new(
        "INQ087",
        "InquiryManyToMany junction or related type is not a mapped entity",
        "Entity '{0}' relation '{1}' references type '{2}', which is not a mapped Inquiry entity. Both the junction type and the related type must be classes marked [InquiryTable] (an entity that failed its own validation is also unmapped — fix the diagnostics on '{2}' first).",
        "Inquiry",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    // INQ088: a foreign-key property named by [InquiryManyToMany] does not exist as a mapped column on the
    // junction. Split out of INQ063 to name the offending string — the common cause is a typo or a
    // property that exists but carries no [InquiryColumn]/[InquiryKey].
    public static readonly DiagnosticDescriptor ManyToManyForeignKeyNotMapped = new(
        "INQ088",
        "InquiryManyToMany names a junction property that is not a mapped column",
        "Entity '{0}' relation '{1}' names '{2}' as a foreign-key property of junction '{3}', but '{3}' has no mapped column for it. Name a property that carries [InquiryColumn] or [InquiryKey]; use nameof to keep the name checked by the compiler.",
        "Inquiry",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    // INQ089: the child foreign keys do not pair with the related entity's key columns — wrong count,
    // duplicated names, or a type that does not match the key column it sits opposite. The pairing is
    // positional and drives both the SQL correlation and the in-memory grouping, so a mis-paired list is
    // a silently wrong join rather than a compile error; this is what makes it loud.
    public static readonly DiagnosticDescriptor ManyToManyChildKeyPairingInvalid = new(
        "INQ089",
        "InquiryManyToMany child foreign keys do not pair with the related entity's key",
        "Entity '{0}' relation '{1}' names {2} child foreign-key propert{3} for related entity '{4}', which has {5} key column{6}. Name one junction property per key column, in the related entity's key-declaration order, with distinct names and each having the same type as the key column it is paired with. {7}",
        "Inquiry",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    // INQ062 (Info — a DDL "lint", off by default): a decimal column with no explicit Precision/Scale and
    // no SqlType override takes the dialect's default (e.g. DECIMAL(18,2)), which can silently round —
    // a real hazard for money. EF Core's DecimalTypeDefaultWarning is the same advisory.
    public static readonly DiagnosticDescriptor DecimalWithoutPrecision = new(
        "INQ062",
        "Decimal column relies on the default precision/scale",
        "Entity '{0}' decimal column '{1}' has no explicit Precision/Scale, so it takes {2}'s default (e.g. DECIMAL(18,2)), which can silently round. Set [InquiryColumn(Precision = …, Scale = …)] (or SqlType) to make the storage type explicit.",
        "Inquiry",
        DiagnosticSeverity.Info,
        isEnabledByDefault: false);

    // INQ064 (Info — a DDL "lint", off by default): a non-key column a store method filters on (a
    // [InquirySelectAllByField] field or an [InquiryWhere] criterion) has no index, so those queries
    // scan the table. Opt in to find columns that would benefit from [InquiryColumn(IsIndexed = true)].
    public static readonly DiagnosticDescriptor UnindexedFilterColumn = new(
        "INQ064",
        "Filtered column has no index",
        "Entity '{0}' column '{1}' is used as a query filter but has no index, so those queries scan the table. Add [InquiryColumn(IsIndexed = true)] (or IsUnique) if it is filtered often.",
        "Inquiry",
        DiagnosticSeverity.Info,
        isEnabledByDefault: false);

    // INQ065: an [InquiryColumn(Length/Precision/Scale = …)] value is out of range. Length/Precision/Scale
    // are read as raw ints with no validation, so a negative Length, a Precision above the portable SQL
    // maximum of 38 (also the ceiling of DbParameter.Precision's byte range for #56's Size emission), or a
    // Scale exceeding its Precision produces invalid DDL (DECIMAL(99, …)) or breaks the generated binder.
    // Reported at the property so the fix is local. Dialect-agnostic: 38 is the SQL-standard decimal max
    // (a dialect with a larger max — MySQL 65, PostgreSQL 1000 — should use SqlType for those rare cases).
    public static readonly DiagnosticDescriptor ColumnMetadataOutOfRange = new(
        "INQ065",
        "Column Length/Precision/Scale is out of range",
        "Entity '{0}' column '{1}' has an out-of-range [InquiryColumn] metadata value: {2}.",
        "Inquiry",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    // INQ066 (Info — a DDL "lint", off by default): a nullable column that also carries a non-null
    // DEFAULT expression. New rows always receive the default, so NULL is unreachable via INSERT —
    // keeping the column nullable adds ambiguity (does NULL mean "never set" or "explicitly cleared"?)
    // without serving a purpose. Either drop the default or make the column NOT NULL.
    public static readonly DiagnosticDescriptor NullableColumnWithDefault = new(
        "INQ066",
        "Nullable column has a default value",
        "Entity '{0}' column '{1}' is nullable but carries a DEFAULT expression. New rows will never be NULL — consider making it NOT NULL, or remove the default if NULL is intentional.",
        "Inquiry",
        DiagnosticSeverity.Info,
        isEnabledByDefault: false);

    // INQ067 (Info — a DDL "lint", off by default): a string column with no explicit Length and no
    // SqlType override takes the dialect's unbounded text type (TEXT, NVARCHAR(MAX), CLOB, etc.),
    // which may inhibit indexing, bloat row storage, or mask a missing constraint. Suppressed for
    // key columns (INQ031 already covers them) and for indexed/unique columns (INQ032 covers them).
    public static readonly DiagnosticDescriptor UnboundedStringColumn = new(
        "INQ067",
        "String column has no explicit length",
        "Entity '{0}' column '{1}' is a string with no Length or SqlType, so it takes the dialect's unbounded type (e.g. TEXT / NVARCHAR(MAX)). Set [InquiryColumn(Length = …)] (or SqlType) if a bounded type is more appropriate.",
        "Inquiry",
        DiagnosticSeverity.Info,
        isEnabledByDefault: false);

    public static readonly DiagnosticDescriptor ConverterInvalid = new(
        "INQ037",
        "Converter type is invalid",
        "Entity '{0}' sets Converter = typeof({1}) on property '{2}', but '{1}' does not provide exactly one IInquiryValueConverter<TModel, TProvider> contract for that property's non-null model type.",
        "Inquiry",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ConverterProviderTypeUnsupported = new(
        "INQ038",
        "Converter provider type is not supported",
        "Entity '{0}' converter '{1}' on property '{2}' uses provider type '{3}', which is not a supported non-null Inquiry scalar provider type. Model-property nullability controls database NULL.",
        "Inquiry",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ConverterModelTypeMismatch = new(
        "INQ078",
        "Converter model type does not match the property type",
        "Converter '{0}' on property '{1}' must implement IInquiryValueConverter<{2}, TProvider> for the property's non-null model type.",
        "Inquiry",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ConverterTypeAbstract = new(
        "INQ079",
        "Converter type cannot be abstract",
        "Converter type '{0}' is abstract and cannot be instantiated by generated Inquiry code.",
        "Inquiry",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ConverterTypeOpenGeneric = new(
        "INQ080",
        "Converter type must be closed",
        "Converter type '{0}' is an open generic type. Supply a closed converter type.",
        "Inquiry",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ConverterTypeInaccessible = new(
        "INQ081",
        "Converter type is inaccessible",
        "Converter type '{0}' is not accessible to generated Inquiry code.",
        "Inquiry",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ConverterConstructorMissing = new(
        "INQ082",
        "Converter type needs a public parameterless constructor",
        "Converter type '{0}' must have a public parameterless constructor for InquiryConverterCache<TConverter>.",
        "Inquiry",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor PagedResultDistinctNotSupported = new(
        "INQ083",
        "InquiryPagedResult cannot be combined with Distinct",
        "Method '{0}' returns InquiryPagedResult<T> which pairs a SELECT with a COUNT(*), but Distinct = true would make the count diverge from the deduplicated result set. Remove Distinct or use a non-paged return type.",
        "Inquiry",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor TypedForeignKeyTargetMissingTable = new(
        "INQ084",
        "Typed foreign key target lacks [InquiryTable]",
        "The type '{0}' referenced in [InquiryForeignKey(typeof({0}))] is not mapped with [InquiryTable]. Add [InquiryTable] to the target entity or use the string overload.",
        "Inquiry",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor TypedForeignKeyTargetMissingKey = new(
        "INQ085",
        "Typed foreign key target has no single [InquiryKey]",
        "The type '{0}' referenced in [InquiryForeignKey(typeof({0}))] has no single [InquiryKey] property. Add [InquiryKey] to exactly one property on the target, specify the column explicitly, or use the string overload.",
        "Inquiry",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor EnumAsStringNonEnum = new(
        "INQ036",
        "InquiryEnumAsString applied to a non-enum property",
        "Entity '{0}' marks property '{1}' with [InquiryEnumAsString], but its type is not an enum (or nullable enum). Remove the attribute or change the property to an enum type.",
        "Inquiry",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    // INQ039: a store method maps to an operation the active dialect cannot emit (e.g. Oracle has no
    // INSERT/UPDATE/UPSERT ... RETURNING). It is an error by default. A consumer that deliberately
    // configures INQ039 below error severity project-wide opts into generated NotSupportedException runtime stubs.
    public static readonly DiagnosticDescriptor DialectOperationNotSupported = new(
        "INQ039",
        "Operation is not supported by the target dialect",
        "Store method '{0}' maps to an operation the '{1}' dialect cannot emit ({2}). Use a dialect-supported pattern (for RETURNING, fetch the generated key separately) or compile against a dialect that supports it. Configure INQ039 below error severity project-wide only to opt all unsupported project methods into generated NotSupportedException runtime stubs.",
        "Inquiry",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    // Reported at relation-declaration time (regardless of whether any method eager-loads it), so a
    // mistyped relation is caught even when never traversed.
    public static readonly DiagnosticDescriptor UnknownRelationForeignKey = new(
        "INQ040",
        "InquiryRelation references an unmapped foreign-key property",
        "Entity '{0}': relation '{1}' references foreign-key property '{2}', which is not a mapped column on '{3}'. Check the InquiryRelation attribute's foreign-key argument for typos.",
        "Inquiry",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor RelationCompositeChildKey = new(
        "INQ041",
        "InquiryRelation child entity has a composite primary key, which is not supported",
        "Entity '{0}': relation '{1}' targets child entity '{2}', which has a composite primary key ({3} key columns). Eager-loading via InquiryRelation only supports single-key children in v1.",
        "Inquiry",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    // INQ058: the relation's foreign-key property exists, but on the wrong entity. A collection
    // (to-many) relation's FK lives on the child; a reference (to-one) relation's FK lives on the
    // parent. Finding it on the opposite side is almost always a reversed relation declaration.
    public static readonly DiagnosticDescriptor RelationForeignKeyWrongSide = new(
        "INQ058",
        "InquiryRelation foreign key is on the wrong entity",
        "Entity '{0}': relation '{1}' expects its foreign-key property '{2}' on '{3}' (a {4} relation's FK lives there), but it is a column on '{5}' instead. The relation looks reversed — a collection relation's FK belongs to the child, a reference relation's FK to the parent.",
        "Inquiry",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    // INQ042: an OrderBy term contains an unrecognised direction token (anything other than ASC/DESC,
    // case-insensitive) or trailing tokens (e.g. NULLS FIRST). The parser previously fell back to ASC
    // silently for any non-DESC token, silently changing query semantics on typos. Reject up front.
    public static readonly DiagnosticDescriptor InvalidOrderByDirection = new(
        "INQ042",
        "OrderBy term has an invalid direction token",
        "Store method '{0}': OrderBy term '{1}' is invalid. Each term must be 'field' or 'field ASC' / 'field DESC' (case-insensitive); '{2}' is not a recognised direction token.",
        "Inquiry",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    // INQ045: an [InquiryAdHoc] DTO with nothing to map. Ordinal mapping covers every public/internal
    // instance property with an accessible setter, in declaration order; a type with none of those
    // would emit a materializer that returns empty objects for every row.
    public static readonly DiagnosticDescriptor AdHocNoProperties = new(
        "INQ045",
        "Ad-hoc DTO declares no mappable properties",
        "Ad-hoc DTO '{0}' has no instance property with a public or internal setter. [InquiryAdHoc] maps every such property to a SELECT-list ordinal in declaration order; declare at least one.",
        "Inquiry",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    // INQ046: the generated materializer instantiates the DTO with `new T { ... }`, which needs a
    // concrete type with an accessible parameterless constructor. Positional records are the common
    // trip-up — their only constructor is the primary one.
    public static readonly DiagnosticDescriptor AdHocNotConstructible = new(
        "INQ046",
        "Ad-hoc DTO must be a concrete type with an accessible parameterless constructor",
        "Ad-hoc DTO '{0}' cannot be instantiated by the generated materializer: it is abstract or has no public/internal parameterless constructor. Use init-only properties instead of positional record or constructor parameters.",
        "Inquiry",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    // INQ047: [InquiryKey(SequentialGuid = true)] only makes sense on a client-supplied Guid key —
    // the generator assigns InquiryGuid.NewVersion7() into the property, which requires a Guid
    // type, and a database-generated or database-defaulted key is never client-assigned.
    public static readonly DiagnosticDescriptor SequentialGuidKeyInvalid = new(
        "INQ047",
        "SequentialGuid requires a client-supplied Guid key",
        "Entity '{0}' marks key property '{1}' with SequentialGuid = true, but the key is not a plain client-supplied Guid. SequentialGuid requires a Guid (or Guid?) key without IsGenerated or UseDatabaseDefault.",
        "Inquiry",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    // INQ049: an auditing timestamp must be a writable DateTime/DateTimeOffset column the generator
    // can stamp client-side, and it cannot double as the key, a generated/db-default column, the
    // soft-delete indicator, or the concurrency token (each of those is owned by other machinery).
    public static readonly DiagnosticDescriptor AuditTimestampInvalid = new(
        "INQ049",
        "Auditing timestamp column is invalid",
        "Entity '{0}' marks property '{1}' as an auditing timestamp, but it is not a plain DateTime/DateTimeOffset column. Auditing timestamps must be DateTime or DateTimeOffset (nullable allowed) and must not be a key, database-generated, database-defaulted, the soft-delete indicator, or a concurrency token.",
        "Inquiry",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor DuplicateAuditTimestamp = new(
        "INQ050",
        "Entity declares more than one auditing timestamp of the same kind",
        "Entity '{0}' marks more than one property with the same auditing-timestamp attribute (e.g. '{1}'). At most one [InquiryCreatedAt] and one [InquiryModifiedAt] are allowed.",
        "Inquiry",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    // INQ052: a store over an [InquiryView] entity is read-only — it may only declare SELECT /
    // aggregate / count operations. Mutations (insert/update/upsert/delete/bulk/set-based) and
    // stored procedures (arbitrary SQL that can write) are rejected at the method.
    public static readonly DiagnosticDescriptor ViewIsReadOnly = new(
        "INQ052",
        "View-mapped entity is read-only",
        "Store method '{0}' is not a read-only operation but targets view-mapped entity '{1}'. An [InquiryView] entity is read-only — only SELECT, aggregate, and count operations are allowed (no mutations or stored procedures).",
        "Inquiry",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    // INQ057: a [InquiryColumn(Computed = …)] server-computed column is calculated by the database,
    // so it cannot also be a key, database-generated, database-defaulted, an auditing column, the
    // soft-delete indicator, or a concurrency token — those all own the column's value themselves.
    public static readonly DiagnosticDescriptor ComputedColumnInvalid = new(
        "INQ057",
        "Server-computed column is misconfigured",
        "Entity '{0}' marks column '{1}' as Computed, but it also acts as a key, database-generated/defaulted column, auditing column, soft-delete indicator, or concurrency token. A computed column's value is owned by the database expression alone.",
        "Inquiry",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ComputedStringRequiresBoundedLength = new(
        "INQ077",
        "Oracle computed string column requires a bounded length",
        "Computed string column '{0}' must declare a positive Length no greater than {1} for Oracle so its virtual expression can be cast to a supported scalar string type.",
        "Inquiry",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    // INQ055: an auditing user column ([InquiryCreatedBy]/[InquiryModifiedBy]) must be a writable
    // string column the generator can stamp from the ambient user, and it cannot double as the key,
    // a generated/db-default column, the soft-delete indicator, or the concurrency token.
    public static readonly DiagnosticDescriptor AuditUserInvalid = new(
        "INQ055",
        "Auditing user column is invalid",
        "Entity '{0}' marks property '{1}' as an auditing user column, but it is not a plain string column. Auditing user columns must be string (nullable allowed) and must not be a key, database-generated, database-defaulted, the soft-delete indicator, or a concurrency token.",
        "Inquiry",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor DuplicateAuditUser = new(
        "INQ056",
        "Entity declares more than one auditing user column of the same kind",
        "Entity '{0}' marks more than one property with the same auditing-user attribute (e.g. '{1}'). At most one [InquiryCreatedBy] and one [InquiryModifiedBy] are allowed.",
        "Inquiry",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    // INQ054: a field-less [InquirySelectAllByField] derives its filter columns from the method name,
    // but the name has no 'By<Field>' segment to parse. Name the method '<verb>By<Field>[And<Field>…]'
    // (e.g. SelectByCountryAsync), or list the fields explicitly in the attribute.
    public static readonly DiagnosticDescriptor DerivedQueryNameInvalid = new(
        "INQ054",
        "Cannot derive query fields from the method name",
        "Store method '{0}' uses a field-less [InquirySelectAllByField] but its name has no 'By<Field>' segment to derive filter columns from. Name it like 'SelectByCountryAndCityAsync', or pass the field names to the attribute.",
        "Inquiry",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    // INQ053: a key-based select or eager-load needs a primary key the entity doesn't declare. Only
    // a keyless [InquiryView] can reach this (tables always have a key) — give such a view a key
    // column for point lookups, or filter with [InquirySelectAllByField] / a predicate instead.
    public static readonly DiagnosticDescriptor OperationRequiresKey = new(
        "INQ053",
        "Operation requires a key the entity does not declare",
        "Store method '{0}' uses a key-based select or eager load against keyless entity '{1}', which has no [InquiryKey]. Add a key column, or filter with [InquirySelectAllByField] / [InquirySelectAllByPredicate].",
        "Inquiry",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    // INQ051: an [InquiryStoredProcedure] with OutputParameter/ReturnsValue is misconfigured —
    // both set at once, or a RETURN value declared as something other than Task<int>. A return
    // shape that isn't Task<T> at all is the general unsupported-return-type error (INQ005). The
    // scalar-output form surfaces a single read-back value as the task result.
    public static readonly DiagnosticDescriptor StoredProcedureScalarOutputInvalid = new(
        "INQ051",
        "Stored-procedure scalar output is misconfigured",
        "Stored-procedure method '{0}' has an invalid OutputParameter/ReturnsValue configuration: {1}",
        "Inquiry",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    // INQ048: InquiryCommand's raw string constructor is the documented advanced escape hatch; a
    // non-constant command text is where injection bugs live. Warning (not error) because dynamic
    // SQL composed from trusted fragments is legitimate — the analyzer makes the reviewer look.
    public static readonly DiagnosticDescriptor NonConstantRawSql = new(
        "INQ048",
        "Non-constant SQL passed to InquiryCommand",
        "The command text passed to InquiryCommand is not a compile-time constant. If it embeds runtime values, use the FormattableString overloads on IInquiry/IInquiryTransaction (or InquirySql.Sql($\"…\")) so each value becomes a bound parameter. Pass dynamic text here only when it cannot contain user input; suppress this warning at the call site once reviewed.",
        "Inquiry",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    // INQ044: an [InquiryUpdate] SET field that resolved to a column the ORM must not assign.
    // Keys and database-generated columns are immutable; the soft-delete indicator is owned by the
    // delete/restore operations; a concurrency token is matched/advanced only by single-row updates.
    public static readonly DiagnosticDescriptor SetFieldNotUpdatable = new(
        "INQ044",
        "InquiryUpdate SET field is not an updatable column",
        "Store method '{0}' assigns field '{1}', which cannot be SET by a set-based update. SET fields must map to a mutable column — not a key, a database-generated column, the soft-delete indicator, or a concurrency token.",
        "Inquiry",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    // INQ059: an [InquiryGlobalFilter] column must be a non-nullable bool the generator can compose into
    // every SELECT's active-row predicate, and it cannot double as the key, a generated/db-default
    // column, the soft-delete indicator, or a concurrency token — those own the column's value.
    public static readonly DiagnosticDescriptor GlobalFilterInvalid = new(
        "INQ059",
        "InquiryGlobalFilter column is invalid",
        "Entity '{0}' marks property '{1}' with [InquiryGlobalFilter], but it is not usable as a global filter. The column must be a non-nullable bool and must not be a key, database-generated, database-defaulted, the soft-delete indicator, or a concurrency token.",
        "Inquiry",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    // INQ060: an [InquiryWhere(JsonPath = …)] criterion filters inside a JSON column, so the named field
    // must be a plain string column holding JSON text (no value converter — the comparison value binds as
    // text, not through the column's converter) and the path must be a dotted object path whose segments
    // are unquoted identifiers — the cross-dialect subset that needs no quoting in any engine.
    public static readonly DiagnosticDescriptor JsonPathPredicateInvalid = new(
        "INQ060",
        "InquiryWhere JSON-path criterion is invalid",
        "Store method '{0}' filters field '{1}' with a JsonPath, but it cannot. The field must be a plain string column holding JSON text (no value converter), and the path must be a dotted object path like \"$.address.city\" — each segment an unquoted identifier (letter or '_', then letters/digits/'_'); no array indices, hyphens, or quoted keys.",
        "Inquiry",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor DatabaseGeneratedConcurrencyTokenInvalid = new(
        "INQ068",
        "Database-generated concurrency token is invalid",
        "Entity '{0}' marks property '{1}' as a database-generated concurrency token, but it is invalid. {2}",
        "Inquiry",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor CyclicForeignKeyNotSupported = new(
        "INQ069",
        "Provider cannot emit cyclic foreign keys",
        "Table '{0}' foreign-key column '{1}' participates in a schema cycle, but provider '{2}' cannot add the constraint after table creation. Break the cycle, disable generated foreign keys for the table, or use a provider that supports deferred constraint creation.",
        "Inquiry",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor DuplicateSchemaMapping = new(
        "INQ070",
        "Duplicate physical schema mapping",
        "Schema mapping '{0}' is ambiguous: {1}. Map each physical table and foreign-key constraint exactly once.",
        "Inquiry",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor SchemaPrimitiveInvalid = new(
        "INQ071",
        "Schema primitive is invalid for the provider",
        "Schema declaration on '{0}' is invalid for provider '{1}': {2}",
        "Inquiry",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ComputedExpressionInvalid = new(
        "INQ072",
        "Computed expression is invalid for the provider",
        "Computed expression on '{0}' is invalid for provider '{1}': {2}",
        "Inquiry",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor SchemaManifestTooLarge = new(
        "INQ073",
        "Schema manifest exceeds metadata transport limit",
        "The generated schema manifest requires {0} metadata chunks, exceeding the maximum of 10000. Split the mapped schema across assemblies.",
        "Inquiry",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor GeneratedKeySchemaFacetInvalid = new(
        "INQ074", "Generated key schema facets conflict",
        "Generated key '{0}.{1}' cannot declare {2}; identity generation owns that physical facet. Remove the conflicting setting.",
        "Inquiry", DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor SchemaManifestMetadataCollision = new(
        "INQ075", "Schema manifest assembly metadata key is already declared",
        "Assembly metadata key '{0}' is reserved for Inquiry schema-manifest transport. Remove the user declaration.",
        "Inquiry", DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor StoredProcedureTvpInvalid = new(
        "INQ086", "Stored-procedure collection parameter TVP binding is invalid",
        "Stored-procedure method '{0}' collection parameter '{1}': {2}",
        "Inquiry", DiagnosticSeverity.Error, isEnabledByDefault: true);

    // INQ094: an [InquiryIndex] key or Include names something that is not a mapped column. The
    // unresolved name used to collapse to an empty string, so the index reached the generated DDL over
    // a blank identifier and the typo surfaced as a provider syntax error at migration time instead.
    public static readonly DiagnosticDescriptor IndexPropertyNotMapped = new(
        "INQ094", "InquiryIndex references an unmapped property",
        "Entity '{0}' declares an [InquiryIndex] over '{1}', which is not a mapped column.",
        "Inquiry", DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor PredicateStructureInvalid = new(
        "INQ097", "InquiryWhere groups are unbalanced",
        "Store method '{0}' has an invalid InquiryWhere group structure. OpenGroups and CloseGroups must be non-negative, balanced, and properly nested.",
        "Inquiry", DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor OptionalPredicateInvalid = new(
        "INQ098", "Optional InquiryWhere criterion is invalid",
        "Store method '{0}' marks field '{1}' optional, but optional criteria require one nullable scalar parameter and do not support In or NotIn.",
        "Inquiry", DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor SetExpressionInvalid = new(
        "INQ099", "InquirySet expression is invalid",
        "Store method '{0}' has an invalid InquirySet assignment for field '{1}': {2}",
        "Inquiry", DiagnosticSeverity.Error, isEnabledByDefault: true);

}
