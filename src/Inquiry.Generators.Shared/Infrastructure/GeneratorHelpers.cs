using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Inquiry.Generators.Infrastructure;

internal static class GeneratorHelpers
{
    public static AttributeData? GetEntityAttribute(ISymbol symbol, string shortName)
    {
        return symbol.GetAttributes().FirstOrDefault(a =>
            a.AttributeClass?.Name == shortName &&
            a.AttributeClass.ContainingNamespace.ToDisplayString() == KnownSymbols.EntityAttributeNamespace);
    }

    /// <summary>
    /// The properties an entity can map: those of its base classes, base class first, then its own.
    /// A base property is left out when a more derived class hides it, or overrides it with an
    /// Inquiry attribute of its own. The entity's own properties are returned as declared.
    /// </summary>
    public static IEnumerable<IPropertySymbol> GetMappableProperties(INamedTypeSymbol type)
    {
        var baseTypes = new List<INamedTypeSymbol>();
        for (var current = type.BaseType; current is not null && current.SpecialType != SpecialType.System_Object; current = current.BaseType)
        {
            baseTypes.Add(current);
        }

        for (var i = baseTypes.Count - 1; i >= 0; i--)
        {
            var index = i;
            foreach (var property in baseTypes[i].GetMembers().OfType<IPropertySymbol>()
                .Where(property => !IsReplacedInDerivedType(property, type, baseTypes, index)))
            {
                yield return property;
            }
        }

        foreach (var property in type.GetMembers().OfType<IPropertySymbol>())
        {
            yield return property;
        }
    }

    // An override without an Inquiry attribute keeps the mapping of the property it overrides.
    // Any other member with the same name replaces the base property.
    private static bool IsReplacedInDerivedType(IPropertySymbol property, INamedTypeSymbol type, List<INamedTypeSymbol> baseTypes, int index)
    {
        for (var i = index - 1; i >= -1; i--)
        {
            var derived = i < 0 ? type : baseTypes[i];
            if (derived.GetMembers(property.Name).Any(static member =>
                    member is not IPropertySymbol { IsOverride: true } || HasEntityAttribute(member)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasEntityAttribute(ISymbol symbol)
        => symbol.GetAttributes().Any(static attribute =>
            attribute.AttributeClass?.ContainingNamespace?.ToDisplayString() == KnownSymbols.EntityAttributeNamespace);

    /// <summary>Returns <paramref name="name"/> with the <c>@</c> prefix when it is a reserved C# word.</summary>
    public static string EscapeIdentifier(string name)
        => SyntaxFacts.GetKeywordKind(name) == SyntaxKind.None ? name : "@" + name;

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

    /// <summary>Reads a named <c>System.Type</c> argument as its symbol, or null when absent.</summary>
    public static INamedTypeSymbol? GetNamedType(AttributeData attribute, string name)
    {
        foreach (var argument in attribute.NamedArguments)
        {
            if (argument.Key == name && argument.Value.Value is INamedTypeSymbol type)
            {
                return type;
            }
        }

        return null;
    }

    /// <summary>Reads a named enum/int argument as its underlying int, or null when absent.</summary>
    public static int? GetNamedInt(AttributeData attribute, string name)
    {
        foreach (var argument in attribute.NamedArguments)
        {
            if (argument.Key == name && argument.Value.Value is int value)
            {
                return value;
            }
        }

        return null;
    }

    public static bool GetNamedBool(AttributeData attribute, string name)
        => GetNamedBool(attribute, name, defaultValue: false);

    /// <summary>Reads a named bool argument, returning <paramref name="defaultValue"/> when absent.</summary>
    public static bool GetNamedBool(AttributeData attribute, string name, bool defaultValue)
    {
        foreach (var argument in attribute.NamedArguments)
        {
            if (argument.Key == name && argument.Value.Value is bool flag)
            {
                return flag;
            }
        }

        return defaultValue;
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
        // FormatLiteral escapes the quote character only when it also adds the quotes.
        var literal = SymbolDisplay.FormatLiteral(value, quote: true);
        return literal.Substring(1, literal.Length - 2);
    }

    public static string Literal(string? value)
    {
        return value is null ? "null" : SymbolDisplay.FormatLiteral(value, quote: true);
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

    public static string GetHintName(INamedTypeSymbol symbol, string generatedKind)
    {
        var containingTypes = new Stack<string>();
        for (var current = symbol; current is not null; current = current.ContainingType)
        {
            containingTypes.Push(current.MetadataName);
        }

        var typeIdentity = string.Join("+", containingTypes);
        // A namespace segment that is a reserved word displays with the @ prefix, as in Acme.@internal.
        // A hint name cannot contain @, and the generator stops when one does.
        var namespacePrefix = symbol.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : symbol.ContainingNamespace.ToDisplayString().Replace("@", string.Empty) + ".";
        return namespacePrefix + typeIdentity + "." + generatedKind + ".g.cs";
    }

    private static string StripGlobalPrefix(string displayName)
    {
        return displayName.StartsWith(KnownSymbols.GlobalPrefix, StringComparison.Ordinal)
            ? displayName.Substring(KnownSymbols.GlobalPrefix.Length)
            : displayName;
    }
}
