using Microsoft.CodeAnalysis;
using System;
using System.Linq;

namespace Inquiry.Generators.Infrastructure;

internal static class GeneratorHelpers
{
    public static AttributeData? GetEntityAttribute(ISymbol symbol, string shortName)
    {
        return symbol.GetAttributes().FirstOrDefault(a =>
            a.AttributeClass?.Name == shortName &&
            a.AttributeClass.ContainingNamespace.ToDisplayString() == KnownSymbols.EntityAttributeNamespace);
    }

    public static bool IsStoreAttribute(AttributeData attribute)
    {
        return attribute.AttributeClass?.ContainingNamespace?.ToDisplayString() == KnownSymbols.StoreAttributeNamespace;
    }

    public static string? GetConstructorString(AttributeData attribute)
    {
        return attribute.ConstructorArguments.Length > 0 ? attribute.ConstructorArguments[0].Value as string : null;
    }

    /// <summary>
    /// Reads the first constructor argument as a string array. Handles attributes whose
    /// constructor is declared with <c>params string[]</c>, where the typed constant is
    /// an array of typed-constant strings.
    /// </summary>
    public static string[]? GetConstructorStringArray(AttributeData attribute)
    {
        if (attribute.ConstructorArguments.Length == 0)
        {
            return null;
        }

        var first = attribute.ConstructorArguments[0];
        if (first.Kind != TypedConstantKind.Array)
        {
            return null;
        }

        var result = new string[first.Values.Length];
        for (var i = 0; i < first.Values.Length; i++)
        {
            if (first.Values[i].Value is not string value)
            {
                return null;
            }
            result[i] = value;
        }
        return result;
    }

    public static string? GetNamedString(AttributeData attribute, string name)
    {
        foreach (var argument in attribute.NamedArguments)
        {
            if (argument.Key == name)
            {
                return argument.Value.Value as string;
            }
        }

        return null;
    }

    public static bool GetNamedBool(AttributeData attribute, string name)
    {
        foreach (var argument in attribute.NamedArguments)
        {
            if (argument.Key == name && argument.Value.Value is bool flag)
            {
                return flag;
            }
        }

        return false;
    }

    public static bool TryGetStoreEntityType(INamedTypeSymbol symbol, out ITypeSymbol entityType)
    {
        for (var current = symbol.BaseType; current is not null; current = current.BaseType)
        {
            if (current is { IsGenericType: true } &&
                current.ContainingNamespace.ToDisplayString() == KnownSymbols.StoreNamespace &&
                current.Name == "InquiryStore" &&
                current.TypeArguments.Length == 1)
            {
                entityType = current.TypeArguments[0];
                return true;
            }
        }

        entityType = symbol;
        return false;
    }

    public static bool IsGenericType(ITypeSymbol type, string metadataName, ITypeSymbol typeArgument)
    {
        return type is INamedTypeSymbol named &&
            named.TypeArguments.Length == 1 &&
            StripGlobalPrefix(named.ConstructedFrom.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)) == metadataName &&
            SymbolEqualityComparer.Default.Equals(named.TypeArguments[0], typeArgument);
    }

    public static bool IsGenericType(ITypeSymbol type, string metadataName, SpecialType specialType)
    {
        return type is INamedTypeSymbol named &&
            named.TypeArguments.Length == 1 &&
            StripGlobalPrefix(named.ConstructedFrom.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)) == metadataName &&
            named.TypeArguments[0].SpecialType == specialType;
    }

    public static bool IsCancellationToken(ITypeSymbol type)
    {
        return type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::System.Threading.CancellationToken";
    }

    public static string Escape(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    public static string Literal(string? value)
    {
        return value is null ? "null" : "\"" + Escape(value) + "\"";
    }

    public static string BooleanLiteral(bool value)
    {
        return value ? "true" : "false";
    }

    public static string GetAccessibility(Accessibility accessibility)
    {
        return accessibility switch
        {
            Accessibility.Public => "public",
            Accessibility.Internal => "internal",
            _ => "internal",
        };
    }

    /// <summary>Namespace-open for a pre-extracted namespace string (null = global namespace).</summary>
    public static void AppendNamespaceStart(System.Text.StringBuilder source, string? @namespace)
    {
        if (@namespace is not null)
        {
            source.AppendLine($"namespace {@namespace}");
            source.AppendLine("{");
        }
    }

    /// <summary>Namespace-close for a pre-extracted namespace string (null = global namespace).</summary>
    public static void AppendNamespaceEnd(System.Text.StringBuilder source, string? @namespace)
    {
        if (@namespace is not null)
        {
            source.AppendLine("}");
        }
    }

    public static string GetGeneratedTypeName(INamedTypeSymbol containingType, string generatedTypeName)
    {
        if (containingType.ContainingNamespace.IsGlobalNamespace)
        {
            return "global::" + generatedTypeName;
        }

        return "global::" + containingType.ContainingNamespace.ToDisplayString() + "." + generatedTypeName;
    }

    private static string StripGlobalPrefix(string displayName)
    {
        return displayName.StartsWith(KnownSymbols.GlobalPrefix, StringComparison.Ordinal)
            ? displayName.Substring(KnownSymbols.GlobalPrefix.Length)
            : displayName;
    }
}
