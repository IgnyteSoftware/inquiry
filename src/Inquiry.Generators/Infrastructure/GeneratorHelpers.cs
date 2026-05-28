using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
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

    public static string GetParameterDeclaration(IMethodSymbol method, bool enumeratorCancellation = false)
    {
        // Defaults live on the user's partial declaration; the generator's implementation half
        // must not repeat them or CS1066 fires.
        var parts = new List<string>();
        for (var i = 0; i < method.Parameters.Length; i++)
        {
            var parameter = method.Parameters[i];
            var isCt = i == method.Parameters.Length - 1 && IsCancellationToken(parameter.Type);
            var prefix = enumeratorCancellation && isCt
                ? "[global::System.Runtime.CompilerServices.EnumeratorCancellation] "
                : string.Empty;
            parts.Add($"{prefix}{parameter.Type.ToDisplayString(KnownSymbols.FullyQualifiedNullableFormat)} {parameter.Name}");
        }

        return string.Join(", ", parts);
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

    public static void AppendNamespaceStart(System.Text.StringBuilder source, INamedTypeSymbol symbol)
    {
        if (!symbol.ContainingNamespace.IsGlobalNamespace)
        {
            source.AppendLine($"namespace {symbol.ContainingNamespace.ToDisplayString()}");
            source.AppendLine("{");
        }
    }

    public static void AppendNamespaceEnd(System.Text.StringBuilder source, INamedTypeSymbol symbol)
    {
        if (!symbol.ContainingNamespace.IsGlobalNamespace)
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

    /// <summary>
    /// Returns the fully-qualified type name of the struct materializer emitted alongside the
    /// class materializer in <c>EntityProcessor.EmitMaterializer</c>. The struct lives in the
    /// same namespace as the entity, so this is just <c>entityType.Namespace + ".{Name}InquiryEntityStructMaterializer"</c>.
    /// </summary>
    public static string GetStructMaterializerFullName(INamedTypeSymbol entitySymbol)
        => GetGeneratedTypeName(entitySymbol, entitySymbol.Name + "InquiryEntityStructMaterializer");

    private static string StripGlobalPrefix(string displayName)
    {
        return displayName.StartsWith(KnownSymbols.GlobalPrefix, StringComparison.Ordinal)
            ? displayName.Substring(KnownSymbols.GlobalPrefix.Length)
            : displayName;
    }
}
