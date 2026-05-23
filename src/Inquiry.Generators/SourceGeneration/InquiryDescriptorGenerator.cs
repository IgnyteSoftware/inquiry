using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Inquiry.Generators;

[Generator]
public sealed class InquiryDescriptorGenerator : ISourceGenerator
{
    private static readonly DiagnosticDescriptor EntityMustBePartial = new(
        id: "INQ001",
        title: "Inquiry entity must be partial",
        messageFormat: "Entity '{0}' must be partial for Inquiry descriptor generation",
        category: "Inquiry.Mapping",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor DuplicateColumn = new(
        id: "INQ002",
        title: "Duplicate Inquiry column",
        messageFormat: "Entity '{0}' maps multiple properties to column '{1}'",
        category: "Inquiry.Mapping",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor ConflictingIgnoreAndColumn = new(
        id: "INQ003",
        title: "Conflicting Inquiry mapping attributes",
        messageFormat: "Property '{0}.{1}' cannot use both InquiryIgnore and InquiryColumn",
        category: "Inquiry.Mapping",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor SetterRequired = new(
        id: "INQ004",
        title: "Inquiry materialization requires a setter",
        messageFormat: "Property '{0}.{1}' should have a public setter for generated materialization",
        category: "Inquiry.Mapping",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public void Initialize(GeneratorInitializationContext context)
    {
        context.RegisterForSyntaxNotifications(static () => new Receiver());
    }

    public void Execute(GeneratorExecutionContext context)
    {
        if (context.SyntaxReceiver is not Receiver receiver)
        {
            return;
        }

        var tableAttribute = context.Compilation.GetTypeByMetadataName("Inquiry.InquiryTableAttribute");
        if (tableAttribute is null)
        {
            return;
        }

        var entities = new List<EntityModel>();
        foreach (var candidate in receiver.Candidates)
        {
            var model = context.Compilation.GetSemanticModel(candidate.SyntaxTree);
            if (model.GetDeclaredSymbol(candidate) is not INamedTypeSymbol symbol)
            {
                continue;
            }

            if (!HasAttribute(symbol, "Inquiry.InquiryTableAttribute"))
            {
                continue;
            }

            if (!candidate.Modifiers.Any(SyntaxKind.PartialKeyword))
            {
                context.ReportDiagnostic(Diagnostic.Create(EntityMustBePartial, candidate.Identifier.GetLocation(), symbol.Name));
                continue;
            }

            var entity = ReadEntity(symbol, candidate, context);
            if (entity is not null)
            {
                entities.Add(entity);
                context.AddSource($"{entity.HintName}.InquiryDescriptor.g.cs", SourceText.From(GenerateDescriptor(entity), Encoding.UTF8));
            }
        }

        if (entities.Count > 0)
        {
            context.AddSource("InquiryGeneratedMappings.g.cs", SourceText.From(GenerateRegistration(entities), Encoding.UTF8));
        }
    }

    private static EntityModel? ReadEntity(INamedTypeSymbol symbol, TypeDeclarationSyntax syntax, GeneratorExecutionContext context)
    {
        var tableAttribute = symbol.GetAttributes().First(attribute => IsAttribute(attribute, "Inquiry.InquiryTableAttribute"));
        var tableName = tableAttribute.ConstructorArguments.Length == 1
            ? tableAttribute.ConstructorArguments[0].Value as string ?? symbol.Name
            : symbol.Name;
        var schema = tableAttribute.NamedArguments.FirstOrDefault(pair => pair.Key == "Schema").Value.Value as string;

        var columns = new Dictionary<string, PropertyModel>(StringComparer.OrdinalIgnoreCase);
        var properties = new List<PropertyModel>();

        foreach (var property in symbol.GetMembers().OfType<IPropertySymbol>())
        {
            if (property.IsStatic || property.DeclaredAccessibility != Accessibility.Public)
            {
                continue;
            }

            var ignored = HasAttribute(property, "Inquiry.InquiryIgnoreAttribute");
            var columnAttribute = property.GetAttributes().FirstOrDefault(attribute => IsAttribute(attribute, "Inquiry.InquiryColumnAttribute"));
            if (ignored && columnAttribute is not null)
            {
                context.ReportDiagnostic(Diagnostic.Create(ConflictingIgnoreAndColumn, syntax.Identifier.GetLocation(), symbol.Name, property.Name));
                continue;
            }

            if (ignored)
            {
                continue;
            }

            if (property.GetMethod is null)
            {
                continue;
            }

            var columnName = columnAttribute?.ConstructorArguments.Length == 1
                ? columnAttribute.ConstructorArguments[0].Value as string ?? property.Name
                : property.Name;

            if (columns.ContainsKey(columnName))
            {
                context.ReportDiagnostic(Diagnostic.Create(DuplicateColumn, syntax.Identifier.GetLocation(), symbol.Name, columnName));
                continue;
            }

            var keyAttribute = property.GetAttributes().FirstOrDefault(attribute => IsAttribute(attribute, "Inquiry.InquiryKeyAttribute"));
            var databaseGenerated = keyAttribute?.NamedArguments.FirstOrDefault(pair => pair.Key == "DatabaseGenerated").Value.Value as bool? ?? false;
            var isKey = keyAttribute is not null;
            var isReadOnly = HasAttribute(property, "Inquiry.InquiryReadOnlyAttribute") || HasAttribute(property, "Inquiry.InquiryComputedAttribute");
            var isConcurrencyToken = HasAttribute(property, "Inquiry.InquiryConcurrencyTokenAttribute");
            var isInsertable = !isReadOnly && !HasAttribute(property, "Inquiry.InquiryInsertIgnoreAttribute") && !databaseGenerated;
            var isUpdateable = !isReadOnly && !isConcurrencyToken && !isKey && !HasAttribute(property, "Inquiry.InquiryUpdateIgnoreAttribute");
            var hasPublicSetter = property.SetMethod is { DeclaredAccessibility: Accessibility.Public };

            if (!hasPublicSetter)
            {
                context.ReportDiagnostic(Diagnostic.Create(SetterRequired, syntax.Identifier.GetLocation(), symbol.Name, property.Name));
            }

            var propertyModel = new PropertyModel(
                property.Name,
                columnName,
                property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                isKey,
                databaseGenerated,
                isInsertable,
                isUpdateable,
                isConcurrencyToken,
                hasPublicSetter,
                GetReaderExpression(property.Type, properties.Count));

            columns[columnName] = propertyModel;
            properties.Add(propertyModel);
        }

        if (properties.Count == 0)
        {
            return null;
        }

        return new EntityModel(
            symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            symbol.Name,
            GetNamespace(symbol),
            Sanitize(symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)),
            tableName,
            schema,
            properties);
    }

    private static string GenerateDescriptor(EntityModel entity)
    {
        var descriptorName = $"{entity.TypeName}InquiryDescriptor";
        var builder = new StringBuilder();
        builder.AppendLine("// <auto-generated />");
        builder.AppendLine("#nullable enable");
        if (!string.IsNullOrWhiteSpace(entity.Namespace))
        {
            builder.Append("namespace ").Append(entity.Namespace).AppendLine(";");
            builder.AppendLine();
        }

        builder.Append("internal sealed class ").Append(descriptorName)
            .Append(" : global::Inquiry.IInquiryEntityDescriptor<").Append(entity.FullyQualifiedType).Append(">, ")
            .Append("global::Inquiry.IInquiryMaterializer<").Append(entity.FullyQualifiedType).AppendLine(">");
        builder.AppendLine("{");
        builder.Append("    public static readonly ").Append(descriptorName).Append(" Instance = new ").Append(descriptorName).AppendLine("();");
        builder.AppendLine();
        builder.Append("    private static readonly global::System.Collections.Generic.IReadOnlyList<global::Inquiry.IInquiryPropertyDescriptor<").Append(entity.FullyQualifiedType).Append(">> PropertiesValue =");
        builder.AppendLine();
        builder.AppendLine("        new global::Inquiry.IInquiryPropertyDescriptor<" + entity.FullyQualifiedType + ">[]");
        builder.AppendLine("        {");
        foreach (var property in entity.Properties)
        {
            builder.Append("            new global::Inquiry.InquiryPropertyDescriptor<").Append(entity.FullyQualifiedType).Append(">(")
                .Append(ToLiteral(property.PropertyName)).Append(", ")
                .Append(ToLiteral(property.ColumnName)).Append(", ")
                .Append("typeof(").Append(property.TypeName).Append("), ")
                .Append(property.IsKey ? "true" : "false").Append(", ")
                .Append(property.IsDatabaseGenerated ? "true" : "false").Append(", ")
                .Append(property.IsInsertable ? "true" : "false").Append(", ")
                .Append(property.IsUpdateable ? "true" : "false").Append(", ")
                .Append("entity => entity.").Append(property.PropertyName).Append(", ");

            if (property.HasPublicSetter)
            {
                builder.Append("(entity, value) => entity.").Append(property.PropertyName)
                    .Append(" = (").Append(property.TypeName).Append(")global::Inquiry.InquiryTypeConversion.FromDatabaseValue(value, typeof(")
                    .Append(property.TypeName).Append("))!)");
            }
            else
            {
                builder.Append("(_, _) => throw new global::Inquiry.InquiryMappingException(")
                    .Append(ToLiteral($"Property '{entity.FullyQualifiedType}.{property.PropertyName}' does not have a public setter."))
                    .Append("))");
            }

            builder.AppendLine(",");
        }

        builder.AppendLine("        };");
        builder.AppendLine();
        builder.Append("    private static readonly global::System.Collections.Generic.IReadOnlyList<global::Inquiry.IInquiryPropertyDescriptor<").Append(entity.FullyQualifiedType).Append(">> KeysValue =");
        builder.AppendLine();
        builder.AppendLine("        new global::Inquiry.IInquiryPropertyDescriptor<" + entity.FullyQualifiedType + ">[]");
        builder.AppendLine("        {");
        foreach (var key in entity.Properties.Where(property => property.IsKey))
        {
            builder.Append("            PropertiesValue[").Append(entity.Properties.IndexOf(key)).AppendLine("],");
        }

        builder.AppendLine("        };");
        builder.AppendLine();
        builder.Append("    public string TableName => ").Append(ToLiteral(entity.TableName)).AppendLine(";");
        builder.Append("    public string? Schema => ").Append(entity.Schema is null ? "null" : ToLiteral(entity.Schema)).AppendLine(";");
        builder.Append("    public global::System.Collections.Generic.IReadOnlyList<global::Inquiry.IInquiryPropertyDescriptor<").Append(entity.FullyQualifiedType).Append(">> Properties => PropertiesValue;").AppendLine();
        builder.Append("    public global::System.Collections.Generic.IReadOnlyList<global::Inquiry.IInquiryPropertyDescriptor<").Append(entity.FullyQualifiedType).Append(">> Keys => KeysValue;").AppendLine();

        var concurrencyIndex = entity.Properties.FindIndex(property => property.IsConcurrencyToken);
        builder.Append("    public global::Inquiry.IInquiryPropertyDescriptor<").Append(entity.FullyQualifiedType).Append(">? ConcurrencyToken => ")
            .Append(concurrencyIndex >= 0 ? $"PropertiesValue[{concurrencyIndex}]" : "null").AppendLine(";");
        builder.AppendLine();
        builder.Append("    public ").Append(entity.FullyQualifiedType).AppendLine(" Materialize(global::System.Data.Common.DbDataReader reader)");
        builder.AppendLine("    {");
        builder.Append("        var entity = new ").Append(entity.FullyQualifiedType).AppendLine("();");
        for (var index = 0; index < entity.Properties.Count; index++)
        {
            var property = entity.Properties[index];
            if (!property.HasPublicSetter)
            {
                continue;
            }

            builder.Append("        entity.").Append(property.PropertyName).Append(" = ").Append(property.ReaderExpression).AppendLine(";");
        }

        builder.AppendLine("        return entity;");
        builder.AppendLine("    }");
        builder.AppendLine("}");
        return builder.ToString();
    }

    private static string GenerateRegistration(IReadOnlyList<EntityModel> entities)
    {
        var builder = new StringBuilder();
        builder.AppendLine("// <auto-generated />");
        builder.AppendLine("#nullable enable");
        builder.AppendLine("namespace Inquiry.Generated;");
        builder.AppendLine();
        builder.AppendLine("public static class InquiryGeneratedMappings");
        builder.AppendLine("{");
        builder.AppendLine("    public static global::Inquiry.InquiryMetadataRegistry RegisterGeneratedInquiryMappings(this global::Inquiry.InquiryMetadataRegistry registry)");
        builder.AppendLine("    {");
        foreach (var entity in entities)
        {
            var descriptorName = string.IsNullOrWhiteSpace(entity.Namespace)
                ? $"global::{entity.TypeName}InquiryDescriptor"
                : $"global::{entity.Namespace}.{entity.TypeName}InquiryDescriptor";
            builder.Append("        registry.Register(").Append(descriptorName).AppendLine(".Instance);");
        }

        builder.AppendLine("        return registry;");
        builder.AppendLine("    }");
        builder.AppendLine("}");
        return builder.ToString();
    }

    private static string GetReaderExpression(ITypeSymbol type, int ordinal)
    {
        var typeName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        if (type.NullableAnnotation == NullableAnnotation.Annotated || type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
        {
            var inner = type is INamedTypeSymbol named && named.TypeArguments.Length == 1
                ? named.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                : typeName.TrimEnd('?');
            return $"reader.IsDBNull({ordinal}) ? default : reader.GetFieldValue<{inner}>({ordinal})";
        }

        if (type.SpecialType == SpecialType.System_String)
        {
            return $"reader.IsDBNull({ordinal}) ? string.Empty : reader.GetString({ordinal})";
        }

        if (type.TypeKind == TypeKind.Enum)
        {
            return $"reader.IsDBNull({ordinal}) ? default : ({typeName})reader.GetValue({ordinal})";
        }

        return $"reader.IsDBNull({ordinal}) ? default! : reader.GetFieldValue<{typeName}>({ordinal})";
    }

    private static string GetNamespace(INamedTypeSymbol symbol)
    {
        return symbol.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : symbol.ContainingNamespace.ToDisplayString();
    }

    private static bool HasAttribute(ISymbol symbol, string metadataName)
    {
        return symbol.GetAttributes().Any(attribute => IsAttribute(attribute, metadataName));
    }

    private static bool IsAttribute(AttributeData attribute, string metadataName)
    {
        return attribute.AttributeClass?.ToDisplayString() == metadataName;
    }

    private static string ToLiteral(string value)
    {
        return SymbolDisplay.FormatLiteral(value, quote: true);
    }

    private static string Sanitize(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            builder.Append(char.IsLetterOrDigit(character) ? character : '_');
        }

        return builder.ToString();
    }

    private sealed class Receiver : ISyntaxReceiver
    {
        public List<TypeDeclarationSyntax> Candidates { get; } = new();

        public void OnVisitSyntaxNode(SyntaxNode syntaxNode)
        {
            if (syntaxNode is TypeDeclarationSyntax typeDeclaration && typeDeclaration.AttributeLists.Count > 0)
            {
                Candidates.Add(typeDeclaration);
            }
        }
    }

    private sealed class EntityModel
    {
        public EntityModel(
            string fullyQualifiedType,
            string typeName,
            string @namespace,
            string hintName,
            string tableName,
            string? schema,
            List<PropertyModel> properties)
        {
            FullyQualifiedType = fullyQualifiedType;
            TypeName = typeName;
            Namespace = @namespace;
            HintName = hintName;
            TableName = tableName;
            Schema = schema;
            Properties = properties;
        }

        public string FullyQualifiedType { get; }

        public string TypeName { get; }

        public string Namespace { get; }

        public string HintName { get; }

        public string TableName { get; }

        public string? Schema { get; }

        public List<PropertyModel> Properties { get; }
    }

    private sealed class PropertyModel
    {
        public PropertyModel(
            string propertyName,
            string columnName,
            string typeName,
            bool isKey,
            bool isDatabaseGenerated,
            bool isInsertable,
            bool isUpdateable,
            bool isConcurrencyToken,
            bool hasPublicSetter,
            string readerExpression)
        {
            PropertyName = propertyName;
            ColumnName = columnName;
            TypeName = typeName;
            IsKey = isKey;
            IsDatabaseGenerated = isDatabaseGenerated;
            IsInsertable = isInsertable;
            IsUpdateable = isUpdateable;
            IsConcurrencyToken = isConcurrencyToken;
            HasPublicSetter = hasPublicSetter;
            ReaderExpression = readerExpression;
        }

        public string PropertyName { get; }

        public string ColumnName { get; }

        public string TypeName { get; }

        public bool IsKey { get; }

        public bool IsDatabaseGenerated { get; }

        public bool IsInsertable { get; }

        public bool IsUpdateable { get; }

        public bool IsConcurrencyToken { get; }

        public bool HasPublicSetter { get; }

        public string ReaderExpression { get; }
    }
}
