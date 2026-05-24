using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Inquiry.Generators.Diagnostics;
using Inquiry.Generators.Infrastructure;
using Inquiry.Generators.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using GeneratorTypeInfo = Inquiry.Generators.Models.TypeInfo;

namespace Inquiry.Generators.Features.EntityMetadata;

internal static class EntityDiscoverer
{
    public static Dictionary<string, EntityModel> Discover(SourceProductionContext context, Compilation compilation, ImmutableArray<ClassDeclarationSyntax> candidates)
    {
        var entities = new Dictionary<string, EntityModel>();

        foreach (var classDeclaration in candidates)
        {
            context.CancellationToken.ThrowIfCancellationRequested();

            var model = compilation.GetSemanticModel(classDeclaration.SyntaxTree);
            if (model.GetDeclaredSymbol(classDeclaration, context.CancellationToken) is not INamedTypeSymbol entitySymbol)
            {
                continue;
            }

            var tableAttribute = GeneratorHelpers.GetEntityAttribute(entitySymbol, "InquiryTableAttribute");
            if (tableAttribute is null)
            {
                continue;
            }

            var tableName = GeneratorHelpers.GetConstructorString(tableAttribute) ?? entitySymbol.Name;
            var schema = GeneratorHelpers.GetNamedString(tableAttribute, "Schema");
            var columns = DiscoverColumns(context, entitySymbol);

            var keyColumns = columns.Where(static c => c.IsKey).ToArray();
            if (keyColumns.Length != 1)
            {
                context.ReportDiagnostic(Diagnostic.Create(InquiryDiagnosticDescriptors.EntityKeyCount, classDeclaration.Identifier.GetLocation(), entitySymbol.Name));
                continue;
            }

            foreach (var duplicate in columns.GroupBy(static c => c.ColumnName, StringComparer.OrdinalIgnoreCase).Where(static g => g.Count() > 1))
            {
                context.ReportDiagnostic(Diagnostic.Create(InquiryDiagnosticDescriptors.DuplicateColumn, classDeclaration.Identifier.GetLocation(), entitySymbol.Name, duplicate.Key));
            }

            var entity = new EntityModel(entitySymbol, tableName, schema, columns, keyColumns[0]);
            entities[entitySymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)] = entity;
        }

        return entities;
    }

    private static List<ColumnModel> DiscoverColumns(SourceProductionContext context, INamedTypeSymbol entitySymbol)
    {
        var columns = new List<ColumnModel>();

        foreach (var property in entitySymbol.GetMembers().OfType<IPropertySymbol>())
        {
            var keyAttribute = GeneratorHelpers.GetEntityAttribute(property, "InquiryKeyAttribute");
            var mappedColumnAttribute = keyAttribute ?? GeneratorHelpers.GetEntityAttribute(property, "InquiryColumnAttribute");
            var foreignKeyAttribute = GeneratorHelpers.GetEntityAttribute(property, "InquiryForeignKeyAttribute");
            if (mappedColumnAttribute is null && foreignKeyAttribute is null)
            {
                continue;
            }

            var foreignKey = foreignKeyAttribute is null
                ? null
                : CreateForeignKeyModel(context, entitySymbol, property, foreignKeyAttribute);
            var columnName = GetMappedColumnName(mappedColumnAttribute, foreignKeyAttribute, property.Name);
            var typeInfo = GeneratorTypeInfo.Create(property.Type, property.NullableAnnotation);
            var isGenerated = keyAttribute is not null && GeneratorHelpers.GetNamedBool(keyAttribute, "IsGenerated");
            var column = new ColumnModel(property, property.Name, columnName, typeInfo, keyAttribute is not null, isGenerated, foreignKey);
            columns.Add(column);

            if (!typeInfo.IsSupported)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    InquiryDiagnosticDescriptors.UnsupportedPropertyType,
                    property.Locations.FirstOrDefault(),
                    entitySymbol.Name,
                    property.Name,
                    property.Type.ToDisplayString()));
            }

            if (property.SetMethod is null || property.SetMethod.DeclaredAccessibility == Accessibility.Private)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    InquiryDiagnosticDescriptors.PropertyMustHavePublicSetter,
                    property.Locations.FirstOrDefault(),
                    entitySymbol.Name,
                    property.Name));
            }
        }

        return columns;
    }

    private static string GetMappedColumnName(AttributeData? mappedColumnAttribute, AttributeData? foreignKeyAttribute, string propertyName)
    {
        if (mappedColumnAttribute is not null)
        {
            return GeneratorHelpers.GetConstructorString(mappedColumnAttribute) ?? propertyName;
        }

        if (foreignKeyAttribute is { ConstructorArguments.Length: 3 } &&
            foreignKeyAttribute.ConstructorArguments[0].Value is string localColumn &&
            !string.IsNullOrWhiteSpace(localColumn))
        {
            return localColumn;
        }

        return propertyName;
    }

    private static ForeignKeyModel? CreateForeignKeyModel(
        SourceProductionContext context,
        INamedTypeSymbol entitySymbol,
        IPropertySymbol property,
        AttributeData foreignKeyAttribute)
    {
        var constructorArguments = foreignKeyAttribute.ConstructorArguments;
        var localColumn = constructorArguments.Length == 3
            ? constructorArguments[0].Value as string
            : null;
        var referencedTable = constructorArguments.Length == 3
            ? constructorArguments[1].Value as string
            : constructorArguments.Length > 0
                ? constructorArguments[0].Value as string
                : null;
        var referencedColumn = constructorArguments.Length == 3
            ? constructorArguments[2].Value as string
            : constructorArguments.Length > 1
                ? constructorArguments[1].Value as string
                : null;

        if (localColumn is not null && string.IsNullOrWhiteSpace(localColumn))
        {
            ReportInvalidForeignKey(context, entitySymbol, property, "local column cannot be empty");
            return null;
        }

        if (string.IsNullOrWhiteSpace(referencedTable))
        {
            ReportInvalidForeignKey(context, entitySymbol, property, "referenced table cannot be empty");
            return null;
        }

        if (string.IsNullOrWhiteSpace(referencedColumn))
        {
            ReportInvalidForeignKey(context, entitySymbol, property, "referenced column cannot be empty");
            return null;
        }

        return new ForeignKeyModel(referencedTable!, referencedColumn!);
    }

    private static void ReportInvalidForeignKey(
        SourceProductionContext context,
        INamedTypeSymbol entitySymbol,
        IPropertySymbol property,
        string reason)
    {
        context.ReportDiagnostic(Diagnostic.Create(
            InquiryDiagnosticDescriptors.InvalidForeignKey,
            property.Locations.FirstOrDefault(),
            entitySymbol.Name,
            property.Name,
            reason));
    }
}
