using Microsoft.CodeAnalysis;

namespace Inquiry.Generators.Diagnostics;

internal static class InquiryDiagnosticDescriptors
{
    // ---------------------------------------------------------------------------------------------
    // DIAGNOSTIC-ID REGISTRY (Phase 0 / F7)
    //
    // IDs in use:      INQ001, INQ002, INQ004–INQ012, INQ014, INQ016, INQ017.
    // Historically skipped (do NOT reuse, keeps existing IDs stable): INQ003, INQ013, INQ015.
    //
    // RESERVED RANGES for in-flight feature workstreams so parallel branches do not collide on the
    // next free ID. Claim from your reserved block; if you need more, extend past INQ040 and update
    // this table in the same commit.
    //   INQ018–INQ019  W1  Richer WHERE predicates      (e.g. bad IN collection, op/type mismatch)
    //   INQ020–INQ021  W2  ORDER BY + pagination         (paging requires ORDER BY, unknown order field)
    //   INQ022–INQ023  W3  Batch & bulk operations
    //   INQ024–INQ027  W5  Projections + aggregations    (projection not mapped, unknown column, …)
    //   INQ028–INQ029  W6  Optimistic concurrency        (>1 token, token==key, unsupported dialect)
    //   INQ030–INQ032  W7  Migrations / schema DDL
    //   INQ033–INQ034  W8  Soft deletes
    //   INQ035         W9  Full-text search              (unsupported by dialect)
    //   INQ036–INQ038  W10 JSON/array/value-converter column types
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
}
