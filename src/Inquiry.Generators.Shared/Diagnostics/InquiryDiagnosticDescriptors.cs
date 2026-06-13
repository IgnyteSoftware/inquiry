using Microsoft.CodeAnalysis;

namespace Inquiry.Generators.Diagnostics;

internal static class InquiryDiagnosticDescriptors
{
    // ---------------------------------------------------------------------------------------------
    // DIAGNOSTIC-ID REGISTRY
    //
    // IDs in use:      INQ001, INQ002, INQ004–INQ012, INQ014, INQ016, INQ017, INQ018–INQ023,
    //                  INQ024–INQ026, INQ028–INQ032, INQ035–INQ041, INQ042, INQ043, INQ044,
    //                  INQ045–INQ058.
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
    //   INQ039         Graceful degradation: operation unsupported by the active dialect (stub + warning) [IN USE]
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
        "Eager loading is not supported on composite-key entities",
        "Query method '{0}' uses eager loading on entity '{1}', which has a composite primary key. Composite-key parents are not supported for eager loading.",
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

    // INQ023: an [InquiryUpdateWhere]/[InquiryDeleteWhere] method with no [InquiryWhere] criteria would
    // mutate every row in the table — almost certainly a bug. Whole-collection mutations have explicit
    // operations ([InquiryUpdateAll]/[InquiryDeleteAll]), so the unfiltered form is rejected up front.
    public static readonly DiagnosticDescriptor PredicateMutationRequiresWhere = new(
        "INQ023",
        "Set-based mutation requires at least one InquiryWhere criterion",
        "Store method '{0}' uses a set-based update/delete with no [InquiryWhere] criteria. An unfiltered set-based mutation would affect every row; add at least one [InquiryWhere], or use [InquiryUpdateAll]/[InquiryDeleteAll] for whole-collection operations.",
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
        "String key column requires an explicit Length for this dialect",
        "Entity '{0}' has string key column '{1}' with no [InquiryColumn(Length = …)]. The '{2}' dialect cannot create a primary key over an unbounded text column, so generated DDL would fail. Set an explicit Length.",
        "Inquiry",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor IndexedStringRequiresLength = new(
        "INQ032",
        "Indexed string column requires an explicit Length for this dialect",
        "Entity '{0}' has indexed string column '{1}' with no [InquiryColumn(Length = …)]. The '{2}' dialect maps an unbounded string to a LOB/MAX text type it cannot index, so the generated index is skipped. Set an explicit Length to have the index created (or a foreign key inherits its referenced key's Length).",
        "Inquiry",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ConverterInvalid = new(
        "INQ037",
        "Converter type does not implement IInquiryValueConverter<,>",
        "Entity '{0}' sets Converter = typeof({1}) on property '{2}', but '{1}' does not implement IInquiryValueConverter<TModel, TProvider>.",
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
    // INSERT/UPDATE/UPSERT ... RETURNING). Reported as a Warning so the build degrades gracefully —
    // the generator emits a stub that throws NotSupportedException at runtime instead of aborting the
    // entire compilation. Elevate to error via .editorconfig if a dialect must be fully supported.
    public static readonly DiagnosticDescriptor DialectOperationNotSupported = new(
        "INQ039",
        "Operation is not supported by the target dialect",
        "Store method '{0}' maps to an operation the '{1}' dialect cannot emit ({2}). A stub that throws NotSupportedException at runtime was generated; calling it will fail. Use a dialect-supported pattern (for RETURNING, fetch the generated key separately) or compile against a dialect that supports it.",
        "Inquiry",
        DiagnosticSeverity.Warning,
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

    // INQ044: an [InquiryUpdateWhere] SET field that resolved to a column the ORM must not assign.
    // Keys and database-generated columns are immutable; the soft-delete indicator is owned by the
    // delete/restore operations; a concurrency token is matched/advanced only by single-row updates.
    public static readonly DiagnosticDescriptor SetFieldNotUpdatable = new(
        "INQ044",
        "InquiryUpdateWhere SET field is not an updatable column",
        "Store method '{0}' assigns field '{1}', which cannot be SET by a set-based update. SET fields must map to a mutable column — not a key, a database-generated column, the soft-delete indicator, or a concurrency token.",
        "Inquiry",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
