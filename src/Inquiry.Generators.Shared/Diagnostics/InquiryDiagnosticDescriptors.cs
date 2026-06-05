using Microsoft.CodeAnalysis;

namespace Inquiry.Generators.Diagnostics;

internal static class InquiryDiagnosticDescriptors
{
    // ---------------------------------------------------------------------------------------------
    // DIAGNOSTIC-ID REGISTRY
    //
    // IDs in use:      INQ001, INQ002, INQ004–INQ012, INQ014, INQ016, INQ017, INQ018–INQ022,
    //                  INQ024–INQ026, INQ028–INQ032, INQ035–INQ041, INQ042.
    // Retired (do NOT reuse, keeps existing IDs stable): INQ003, INQ013, INQ015, INQ027 (projection
    //   on soft-delete, removed in P3 #14 — now supported).
    //
    // RESERVED RANGES for in-flight feature workstreams so parallel branches do not collide on the
    // next free ID. Claim from your reserved block; if you need more, extend past INQ040 and update
    // this table in the same commit.
    //   INQ018–INQ019  Richer WHERE predicates      (e.g. bad IN collection, op/type mismatch)  [IN USE]
    //   INQ020–INQ021  ORDER BY + pagination         (paging requires ORDER BY, unknown order field) [IN USE]
    //   INQ022–INQ023  Batch & bulk operations
    //   INQ024–INQ027  Projections + aggregations    (INQ024 no columns, INQ025 not mapped, INQ026 entity mismatch; INQ027 retired in P3 #14) [IN USE]
    //   INQ028–INQ029  Optimistic concurrency        (INQ028 >1 token, INQ029 token==key)  [IN USE]
    //                       (DB-managed-on-unsupported-dialect and upsert+token reuse INQ006 at emit time, per convention)
    //   INQ030–INQ032  Migrations / schema DDL       (INQ030 generated key not integer, INQ031 string key needs Length, INQ032 indexed string needs Length) [IN USE]
    //   INQ033–INQ034  Soft deletes
    //   INQ035         Full-text search              (unsupported by dialect)
    //   INQ036–INQ038  JSON/array/value-converter column types
    //   INQ039         Graceful degradation: operation unsupported by the active dialect (stub + warning) [IN USE]
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

    public static readonly DiagnosticDescriptor UnknownRelationForeignKey = new(
        "INQ040",
        "InquiryRelation references an unmapped foreign-key property",
        "Eager-loading method '{0}': relation '{1}.{2}' references foreign-key property '{3}', which is not a mapped column on child entity '{4}'. Check the InquiryRelation attribute's foreign-key argument for typos.",
        "Inquiry",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor RelationCompositeChildKey = new(
        "INQ041",
        "InquiryRelation child entity has a composite primary key, which is not supported",
        "Eager-loading method '{0}': relation '{1}.{2}' targets child entity '{3}', which has a composite primary key ({4} key columns). Eager-loading via InquiryRelation only supports single-key children in v1.",
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
}
