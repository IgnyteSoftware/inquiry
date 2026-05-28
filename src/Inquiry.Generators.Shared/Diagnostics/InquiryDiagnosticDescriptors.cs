using Microsoft.CodeAnalysis;

namespace Inquiry.Generators.Diagnostics;

internal static class InquiryDiagnosticDescriptors
{
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
}
