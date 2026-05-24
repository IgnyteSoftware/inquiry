using Microsoft.CodeAnalysis;

namespace Inquiry.Generators.Diagnostics;

internal static class InquiryDiagnosticDescriptors
{
    public static readonly DiagnosticDescriptor EntityKeyCount = new(
        "INQ001",
        "Entity must have exactly one InquiryKey property",
        "Entity '{0}' must have exactly one InquiryKey property.",
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

    public static readonly DiagnosticDescriptor UnsupportedPropertyType = new(
        "INQ003",
        "Entity property type is not supported",
        "Entity property '{0}.{1}' has unsupported type '{2}'.",
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

    public static readonly DiagnosticDescriptor MethodMustBeAbstract = new(
        "INQ010",
        "Query method must be abstract",
        "Query method '{0}' must be abstract.",
        "Inquiry",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor InvalidForeignKey = new(
        "INQ011",
        "Foreign-key mapping is invalid",
        "Entity property '{0}.{1}' has an invalid foreign-key mapping: {2}.",
        "Inquiry",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
