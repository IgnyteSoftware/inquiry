using Inquiry.Generators.Abstractions;
using Inquiry.Generators.Diagnostics;
using Inquiry.Generators.Infrastructure;
using Inquiry.Generators.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;

namespace Inquiry.Generators;

/// <summary>
/// Discovers <c>[InquiryTable]</c> entities and emits a materializer per entity. Discovery runs in
/// the syntax-provider transform and produces a value-equatable <see cref="EntityData"/> (carrying
/// diagnostics as data); emission consumes that data with no symbols, so the output stage caches.
/// </summary>
internal static class EntityProcessor
{
    /// <summary>Extracts the cacheable model for one <c>[InquiryTable]</c> entity symbol.</summary>
    public static EntityData Extract(INamedTypeSymbol entitySymbol, CancellationToken cancellationToken)
    {
        var diagnostics = ImmutableArray.CreateBuilder<DiagnosticData>();
        var location = entitySymbol.Locations.FirstOrDefault();

        var tableAttribute = GeneratorHelpers.GetEntityAttribute(entitySymbol, "InquiryTableAttribute");
        var tableName = (tableAttribute is not null ? GeneratorHelpers.GetConstructorString(tableAttribute) : null) ?? entitySymbol.Name;
        var schema = tableAttribute is not null ? GeneratorHelpers.GetNamedString(tableAttribute, "Schema") : null;
        // GenerateForeignKeys defaults to true; only an explicit `= false` named arg disables FK DDL.
        var generateForeignKeys = tableAttribute is null ||
            GeneratorHelpers.GetNamedBool(tableAttribute, "GenerateForeignKeys", defaultValue: true);

        var columns = DiscoverColumns(entitySymbol, diagnostics);
        var relations = DiscoverRelations(entitySymbol, cancellationToken);

        var keyColumns = columns.Where(static c => c.IsKey).ToImmutableArray();
        var isMapped = true;

        if (keyColumns.Length == 0)
        {
            diagnostics.Add(DiagnosticData.Create(InquiryDiagnosticDescriptors.EntityKeyCount, location, entitySymbol.Name));
            isMapped = false;
        }
        else if (keyColumns.Length > 1)
        {
            var generatedKey = keyColumns.FirstOrDefault(static k => k.IsGenerated);
            if (generatedKey is not null)
            {
                diagnostics.Add(DiagnosticData.Create(InquiryDiagnosticDescriptors.CompositeKeyContainsGenerated, location, entitySymbol.Name, generatedKey.PropertyName));
                isMapped = false;
            }
        }

        foreach (var duplicate in columns.GroupBy(static c => c.ColumnName, StringComparer.OrdinalIgnoreCase).Where(static g => g.Count() > 1))
        {
            diagnostics.Add(DiagnosticData.Create(InquiryDiagnosticDescriptors.DuplicateColumn, location, entitySymbol.Name, duplicate.Key));
        }

        // at most one [InquirySoftDelete] column. Columns whose type was unsupported carry
        // SoftDeleteKind.None (already reported), so they are excluded from this count.
        var softDeleteColumns = columns.Where(static c => c.SoftDelete != SoftDeleteKind.None).ToImmutableArray();
        if (softDeleteColumns.Length > 1)
        {
            diagnostics.Add(DiagnosticData.Create(InquiryDiagnosticDescriptors.MultipleSoftDeleteColumns, location, entitySymbol.Name, softDeleteColumns[1].PropertyName));
        }

        // at most one [InquiryCreatedAt] and one [InquiryModifiedAt] (INQ050). Columns whose
        // attribute was invalid carry cleared flags (already reported), so they don't count here.
        var createdAtColumns = columns.Where(static c => c.IsCreatedAt).ToImmutableArray();
        if (createdAtColumns.Length > 1)
        {
            diagnostics.Add(DiagnosticData.Create(InquiryDiagnosticDescriptors.DuplicateAuditTimestamp, location, entitySymbol.Name, createdAtColumns[1].PropertyName));
        }

        var modifiedAtColumns = columns.Where(static c => c.IsModifiedAt).ToImmutableArray();
        if (modifiedAtColumns.Length > 1)
        {
            diagnostics.Add(DiagnosticData.Create(InquiryDiagnosticDescriptors.DuplicateAuditTimestamp, location, entitySymbol.Name, modifiedAtColumns[1].PropertyName));
        }

        // at most one [InquiryConcurrencyToken] (INQ028), and it must not be the key (INQ029).
        var concurrencyTokens = columns.Where(static c => c.IsConcurrencyToken).ToImmutableArray();
        if (concurrencyTokens.Length > 1)
        {
            diagnostics.Add(DiagnosticData.Create(InquiryDiagnosticDescriptors.MultipleConcurrencyTokens, location, entitySymbol.Name, concurrencyTokens[1].PropertyName));
        }

        foreach (var keyToken in concurrencyTokens.Where(static c => c.IsKey))
        {
            diagnostics.Add(DiagnosticData.Create(InquiryDiagnosticDescriptors.ConcurrencyTokenIsKey, location, entitySymbol.Name, keyToken.PropertyName));
        }

        var classMaterializerName = entitySymbol.Name + "InquiryEntityMaterializer";
        var structMaterializerName = entitySymbol.Name + "InquiryEntityStructMaterializer";

        return new EntityData(
            FullyQualifiedName: entitySymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            Name: entitySymbol.Name,
            Namespace: entitySymbol.ContainingNamespace.IsGlobalNamespace ? null : entitySymbol.ContainingNamespace.ToDisplayString(),
            TableName: tableName,
            Schema: schema,
            Columns: new EquatableArray<ColumnData>(columns.ToImmutableArray()),
            Keys: new EquatableArray<ColumnData>(keyColumns),
            Relations: new EquatableArray<RelationData>(relations.ToImmutableArray()),
            ClassMaterializerName: classMaterializerName,
            StructMaterializerName: structMaterializerName,
            ClassMaterializerFullName: GeneratorHelpers.GetGeneratedTypeName(entitySymbol, classMaterializerName),
            StructMaterializerFullName: GeneratorHelpers.GetGeneratedTypeName(entitySymbol, structMaterializerName),
            IsMapped: isMapped,
            Diagnostics: new EquatableArray<DiagnosticData>(diagnostics.ToImmutable()))
        {
            SoftDeleteColumn = softDeleteColumns.Length > 0 ? softDeleteColumns[0] : null,
            ConcurrencyToken = concurrencyTokens.Length > 0 ? concurrencyTokens[0] : null,
            GenerateForeignKeys = generateForeignKeys,
        };
    }

    public static EntityRegistration EmitMaterializer(SourceProductionContext context, EntityData entity)
    {
        var entityType = entity.FullyQualifiedName;

        var source = new StringBuilder();
        source.AppendLine("// <auto-generated />");
        source.AppendLine("#nullable enable");
        GeneratorHelpers.AppendNamespaceStart(source, entity.Namespace);

        // Class materializer — registered as singleton in DI and used by ad-hoc IInquiry queries
        // that resolve the materializer at runtime.
        source.AppendLine($"internal sealed class {entity.ClassMaterializerName} : global::Inquiry.Materialization.IInquiryEntityMaterializer<{entityType}>");
        source.AppendLine("{");
        source.AppendLine($"    public {entityType} Materialize(global::System.Data.Common.DbDataReader reader)");
        source.AppendLine("    {");
        MaterializerEmitter.EmitMaterializeBody(source, entity.Columns, entityType, indent: "        ");
        source.AppendLine("    }");
        source.AppendLine("}");
        source.AppendLine();

        // Struct materializer — used by generated stores via the struct-constrained pipeline
        // overloads. The struct has no fields; generated stores pass `default(...)`. The JIT
        // produces a separate specialization per TMaterializer so the read-loop call to
        // Materialize is inlined instead of dispatched through the interface.
        source.AppendLine($"internal readonly struct {entity.StructMaterializerName} : global::Inquiry.Materialization.IInquiryEntityMaterializer<{entityType}>");
        source.AppendLine("{");
        source.AppendLine($"    public {entityType} Materialize(global::System.Data.Common.DbDataReader reader)");
        source.AppendLine("    {");
        MaterializerEmitter.EmitMaterializeBody(source, entity.Columns, entityType, indent: "        ");
        source.AppendLine("    }");
        source.AppendLine("}");

        GeneratorHelpers.AppendNamespaceEnd(source, entity.Namespace);

        context.AddSource($"{entity.Name}.InquiryEntity.g.cs", SourceText.From(source.ToString(), Encoding.UTF8));
        return new EntityRegistration(entityType, entity.ClassMaterializerFullName);
    }

    private static List<ColumnData> DiscoverColumns(INamedTypeSymbol entitySymbol, ImmutableArray<DiagnosticData>.Builder diagnostics)
    {
        var columns = new List<ColumnData>();

        foreach (var property in entitySymbol.GetMembers().OfType<IPropertySymbol>())
        {
            var keyAttribute = GeneratorHelpers.GetEntityAttribute(property, "InquiryKeyAttribute");
            // [InquiryConcurrencyToken] derives InquiryColumnAttribute, so it is discovered as a
            // column (like [InquiryKey]). Probed after the key but before the plain column probe.
            var concurrencyTokenAttribute = GeneratorHelpers.GetEntityAttribute(property, "InquiryConcurrencyTokenAttribute");
            // [InquiryCreatedAt]/[InquiryModifiedAt] derive InquiryColumnAttribute, so they are
            // discovered as columns (like [InquiryKey]/[InquiryConcurrencyToken]).
            var createdAtAttribute = GeneratorHelpers.GetEntityAttribute(property, "InquiryCreatedAtAttribute");
            var modifiedAtAttribute = GeneratorHelpers.GetEntityAttribute(property, "InquiryModifiedAtAttribute");
            var columnAttribute = keyAttribute ?? concurrencyTokenAttribute ?? createdAtAttribute ?? modifiedAtAttribute
                ?? GeneratorHelpers.GetEntityAttribute(property, "InquiryColumnAttribute");
            var foreignKeyAttribute = GeneratorHelpers.GetEntityAttribute(property, "InquiryForeignKeyAttribute");
            if (columnAttribute is null && foreignKeyAttribute is null)
            {
                continue;
            }

            var columnName = ResolveColumnName(columnAttribute, foreignKeyAttribute, property.Name);
            var typeData = TypeData.Create(property.Type, property.NullableAnnotation);
            var isGenerated = keyAttribute is not null && GeneratorHelpers.GetNamedBool(keyAttribute, "IsGenerated");
            var isSequentialGuid = keyAttribute is not null && GeneratorHelpers.GetNamedBool(keyAttribute, "SequentialGuid");
            var useDatabaseDefault =
                columnAttribute is not null && GeneratorHelpers.GetNamedBool(columnAttribute, "UseDatabaseDefault") ||
                foreignKeyAttribute is not null && GeneratorHelpers.GetNamedBool(foreignKeyAttribute, "UseDatabaseDefault");

            // SequentialGuid assigns InquiryGuid.NewVersion7() into the property on insert/upsert,
            // so the key must be a plain client-supplied Guid (INQ047 otherwise; flag cleared so
            // emission never produces an invalid assignment).
            if (isSequentialGuid && (!typeData.IsGuid || isGenerated || useDatabaseDefault))
            {
                diagnostics.Add(DiagnosticData.Create(
                    InquiryDiagnosticDescriptors.SequentialGuidKeyInvalid,
                    property.Locations.FirstOrDefault(),
                    entitySymbol.Name,
                    property.Name));
                isSequentialGuid = false;
            }

            var softDelete = SoftDeleteKind.None;
            if (GeneratorHelpers.GetEntityAttribute(property, "InquirySoftDeleteAttribute") is not null)
            {
                softDelete = InferSoftDeleteKind(typeData);
                if (softDelete == SoftDeleteKind.None)
                {
                    diagnostics.Add(DiagnosticData.Create(
                        InquiryDiagnosticDescriptors.SoftDeleteUnsupportedType,
                        property.Locations.FirstOrDefault(),
                        entitySymbol.Name,
                        property.Name));
                }
            }

            var isConcurrencyToken = concurrencyTokenAttribute is not null;
            var isDatabaseGeneratedToken = isConcurrencyToken &&
                GeneratorHelpers.GetNamedBool(concurrencyTokenAttribute!, "DatabaseGenerated");

            // Auditing timestamps: a writable DateTime/DateTimeOffset column that no other
            // machinery owns (INQ049 otherwise; flags cleared so emission stays valid).
            var isCreatedAt = createdAtAttribute is not null;
            var isModifiedAt = modifiedAtAttribute is not null;
            if (isCreatedAt || isModifiedAt)
            {
                var isTimestampType = typeData.SpecialType == SpecialType.System_DateTime ||
                    typeData.NonNullableDisplayName == "global::System.DateTimeOffset";
                if (!isTimestampType || (isCreatedAt && isModifiedAt) || keyAttribute is not null ||
                    isGenerated || useDatabaseDefault || isConcurrencyToken ||
                    GeneratorHelpers.GetEntityAttribute(property, "InquirySoftDeleteAttribute") is not null)
                {
                    diagnostics.Add(DiagnosticData.Create(
                        InquiryDiagnosticDescriptors.AuditTimestampInvalid,
                        property.Locations.FirstOrDefault(),
                        entitySymbol.Name,
                        property.Name));
                    isCreatedAt = false;
                    isModifiedAt = false;
                }
            }

            // [InquiryEnumAsString] stores an enum column as its member name. Only valid on an
            // enum (or nullable enum) property; otherwise report INQ036 and leave the flag clear.
            var enumAsString = false;
            if (GeneratorHelpers.GetEntityAttribute(property, "InquiryEnumAsStringAttribute") is not null)
            {
                if (typeData.IsEnum)
                {
                    enumAsString = true;
                }
                else
                {
                    diagnostics.Add(DiagnosticData.Create(
                        InquiryDiagnosticDescriptors.EnumAsStringNonEnum,
                        property.Locations.FirstOrDefault(),
                        entitySymbol.Name,
                        property.Name));
                }
            }

            // DDL metadata. Named args (Length/SqlType/Precision/Scale/DefaultExpression/index flags)
            // live on InquiryColumnAttribute, which InquiryForeignKeyAttribute also inherits. When a
            // property is mapped solely by [InquiryForeignKey] (no separate [InquiryColumn]/[InquiryKey]),
            // read those named args off the FK attribute so e.g. Length is honored on the FK column.
            // ResolveColumnName/ResolveConverter still use columnAttribute alone — the FK attribute's
            // constructor args are (table, column), not a column name.
            var metadataAttribute = columnAttribute ?? foreignKeyAttribute;
            var isKey = keyAttribute is not null;
            var sqlType = metadataAttribute is not null ? GeneratorHelpers.GetNamedString(metadataAttribute, "SqlType") : null;
            var length = (metadataAttribute is not null ? GeneratorHelpers.GetNamedInt(metadataAttribute, "Length") : null) ?? 0;
            var precision = (metadataAttribute is not null ? GeneratorHelpers.GetNamedInt(metadataAttribute, "Precision") : null) ?? 0;
            var scale = (metadataAttribute is not null ? GeneratorHelpers.GetNamedInt(metadataAttribute, "Scale") : null) ?? 0;
            var defaultExpression = metadataAttribute is not null ? GeneratorHelpers.GetNamedString(metadataAttribute, "DefaultExpression") : null;
            var (foreignKeySchema, foreignKeyTable, foreignKeyColumn) = ReadForeignKeyReference(foreignKeyAttribute);

            // a value converter (explicit Converter=typeof(X), or [InquiryJson] → built-in JSON
            // converter) maps a non-primitive property to/from a provider primitive.
            var converter = ResolveConverter(property, columnAttribute, typeData, entitySymbol, diagnostics);

            columns.Add(new ColumnData
            {
                PropertyName = property.Name,
                ColumnName = columnName,
                Type = typeData,
                IsKey = isKey,
                IsGenerated = isGenerated,
                IsSequentialGuid = isSequentialGuid,
                UseDatabaseDefault = useDatabaseDefault,
                SoftDelete = softDelete,
                IsConcurrencyToken = isConcurrencyToken,
                IsDatabaseGeneratedToken = isDatabaseGeneratedToken,
                IsCreatedAt = isCreatedAt,
                IsModifiedAt = isModifiedAt,
                EnumAsString = enumAsString,
                // a converter column's DDL type reflects the PROVIDER primitive it stores, not the model type.
                TypeClass = converter is not null ? MapSpecialType(converter.ProviderSpecialType) : MapTypeClass(typeData),
                IsNullable = !isKey && typeData.IsNullable,
                SqlType = sqlType,
                Length = length,
                Precision = precision,
                Scale = scale,
                DefaultExpression = defaultExpression,
                ForeignKeyTable = foreignKeyTable,
                ForeignKeySchema = foreignKeySchema,
                ForeignKeyColumn = foreignKeyColumn,
                IsIndexed = metadataAttribute is not null && GeneratorHelpers.GetNamedBool(metadataAttribute, "IsIndexed"),
                IsUnique = metadataAttribute is not null && GeneratorHelpers.GetNamedBool(metadataAttribute, "IsUnique"),
                IndexName = metadataAttribute is not null ? GeneratorHelpers.GetNamedString(metadataAttribute, "IndexName") : null,
                Converter = converter,
            });

            if (property.SetMethod is null || property.SetMethod.DeclaredAccessibility == Accessibility.Private)
            {
                diagnostics.Add(DiagnosticData.Create(
                    InquiryDiagnosticDescriptors.PropertyMustHavePublicSetter,
                    property.Locations.FirstOrDefault(),
                    entitySymbol.Name,
                    property.Name));
            }
        }

        return columns;
    }

    /// <summary>
    /// Collapses a CLR type into the dialect-neutral <see cref="DbTypeClass"/> the DDL builder maps
    /// to a physical type. Enums collapse to their underlying integer class so DDL never special-cases them.
    /// </summary>
    private static DbTypeClass MapTypeClass(TypeData type)
    {
        if (type.IsByteArray) return DbTypeClass.ByteArray;
        if (type.IsGuid) return DbTypeClass.Guid;
        if (type.IsDateOnly) return DbTypeClass.DateOnly;
        if (type.IsTimeOnly) return DbTypeClass.TimeOnly;
        if (type.NonNullableDisplayName == "global::System.DateTimeOffset") return DbTypeClass.DateTimeOffset;

        return MapSpecialType(type.IsEnum ? type.EnumUnderlyingSpecialType : type.SpecialType);
    }

    /// <summary>Maps a <see cref="SpecialType"/> to its <see cref="DbTypeClass"/> (text fallback for string/char/other).</summary>
    private static DbTypeClass MapSpecialType(SpecialType special) => special switch
    {
        SpecialType.System_Boolean => DbTypeClass.Boolean,
        SpecialType.System_Byte or SpecialType.System_SByte => DbTypeClass.Byte,
        SpecialType.System_Int16 or SpecialType.System_UInt16 => DbTypeClass.Int16,
        SpecialType.System_Int32 or SpecialType.System_UInt32 => DbTypeClass.Int32,
        SpecialType.System_Int64 or SpecialType.System_UInt64 => DbTypeClass.Int64,
        SpecialType.System_Single => DbTypeClass.Single,
        SpecialType.System_Double => DbTypeClass.Double,
        SpecialType.System_Decimal => DbTypeClass.Decimal,
        SpecialType.System_DateTime => DbTypeClass.DateTime,
        // String, Char, and anything else fall back to a text column.
        _ => DbTypeClass.String,
    };

    /// <summary>
    /// Resolves the value converter for a column — an explicit <c>Converter = typeof(X)</c> (its
    /// <c>IInquiryValueConverter&lt;,&gt;</c> provider type drives the read/write primitive), or
    /// <c>[InquiryJson]</c> (the built-in <c>InquiryJsonConverter&lt;T&gt;</c> over <c>string</c>).
    /// Returns null when neither applies; reports INQ037 when an explicit converter type does not
    /// implement the converter interface.
    /// </summary>
    private static ConverterData? ResolveConverter(
        IPropertySymbol property,
        AttributeData? columnAttribute,
        TypeData typeData,
        INamedTypeSymbol entitySymbol,
        ImmutableArray<DiagnosticData>.Builder diagnostics)
    {
        var converterType = columnAttribute is not null ? GeneratorHelpers.GetNamedType(columnAttribute, "Converter") : null;
        if (converterType is not null)
        {
            var providerType = FindConverterProviderType(converterType);
            if (providerType is null)
            {
                diagnostics.Add(DiagnosticData.Create(
                    InquiryDiagnosticDescriptors.ConverterInvalid,
                    property.Locations.FirstOrDefault(),
                    entitySymbol.Name,
                    converterType.Name,
                    property.Name));
                return null;
            }

            return new ConverterData(
                converterType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                providerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                providerType.SpecialType);
        }

        if (GeneratorHelpers.GetEntityAttribute(property, "InquiryJsonAttribute") is not null)
        {
            return new ConverterData(
                "global::Inquiry.Converters.InquiryJsonConverter<" + typeData.NonNullableDisplayName + ">",
                "string",
                SpecialType.System_String);
        }

        return null;
    }

    /// <summary>Returns the <c>TProvider</c> of the converter's <c>IInquiryValueConverter&lt;TModel, TProvider&gt;</c> interface, or null.</summary>
    private static ITypeSymbol? FindConverterProviderType(INamedTypeSymbol converterType)
    {
        foreach (var iface in converterType.AllInterfaces)
        {
            if (iface.TypeArguments.Length == 2 &&
                iface.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::Inquiry.Entities.IInquiryValueConverter<TModel, TProvider>")
            {
                return iface.TypeArguments[1];
            }
        }

        return null;
    }

    /// <summary>
    /// Extracts the referenced (schema, table, column) from an <c>[InquiryForeignKey]</c>. The 2-arg form is
    /// <c>(referencedTable, referencedColumn)</c>; the 3-arg form is <c>(localColumn, referencedTable,
    /// referencedColumn)</c>. Returns (null, null, null) when the property has no foreign-key attribute.
    /// </summary>
    private static (string?, string?, string?) ReadForeignKeyReference(AttributeData? foreignKeyAttribute)
    {
        if (foreignKeyAttribute is null)
        {
            return (null, null, null);
        }

        var args = foreignKeyAttribute.ConstructorArguments;
        var schema = GeneratorHelpers.GetNamedString(foreignKeyAttribute, "ReferencedSchema");
        if (args.Length == 3)
        {
            return (schema, args[1].Value as string, args[2].Value as string);
        }

        if (args.Length == 2)
        {
            return (schema, args[0].Value as string, args[1].Value as string);
        }

        return (null, null, null);
    }

    private static List<RelationData> DiscoverRelations(INamedTypeSymbol entitySymbol, CancellationToken cancellationToken)
    {
        var relations = new List<RelationData>();

        foreach (var property in entitySymbol.GetMembers().OfType<IPropertySymbol>())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var relationAttribute = GeneratorHelpers.GetEntityAttribute(property, "InquiryRelationAttribute");
            if (relationAttribute is null)
            {
                continue;
            }

            var foreignKeyProperty = GeneratorHelpers.GetConstructorString(relationAttribute);
            if (string.IsNullOrEmpty(foreignKeyProperty))
            {
                continue;
            }

            // Determine child entity type. Supports List<T>, IReadOnlyList<T>, IEnumerable<T>, or T? directly.
            if (!TryGetChildEntityType(property.Type, out var childEntitySymbol, out var isCollection))
            {
                continue;
            }

            relations.Add(new RelationData(
                property.Name,
                foreignKeyProperty!,
                childEntitySymbol!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                isCollection));
        }

        return relations;
    }

    private static bool TryGetChildEntityType(ITypeSymbol type, out INamedTypeSymbol? childEntitySymbol, out bool isCollection)
    {
        isCollection = false;
        childEntitySymbol = null;

        // Strip nullable wrapper first
        var nonNullable = type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } named
            ? named.TypeArguments[0]
            : type;

        if (nonNullable is INamedTypeSymbol namedType)
        {
            if (namedType.IsGenericType && namedType.TypeArguments.Length == 1)
            {
                var fqn = namedType.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                if (fqn is "global::System.Collections.Generic.List<T>"
                         or "global::System.Collections.Generic.IReadOnlyList<T>"
                         or "global::System.Collections.Generic.IList<T>"
                         or "global::System.Collections.Generic.IEnumerable<T>"
                         or "global::System.Collections.Generic.ICollection<T>")
                {
                    childEntitySymbol = namedType.TypeArguments[0] as INamedTypeSymbol;
                    isCollection = true;
                    return childEntitySymbol is not null;
                }
            }

            // Single reference
            childEntitySymbol = namedType;
            isCollection = false;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Infers the soft-delete representation from the marked property's type: a non-nullable
    /// <c>bool</c> is a flag, a nullable <c>DateTime</c>/<c>DateTimeOffset</c> is a timestamp, and
    /// anything else is unsupported (<see cref="SoftDeleteKind.None"/>, reported by the caller).
    /// </summary>
    private static SoftDeleteKind InferSoftDeleteKind(TypeData type)
    {
        if (!type.IsNullable && type.SpecialType == SpecialType.System_Boolean)
        {
            return SoftDeleteKind.BooleanFlag;
        }

        if (type.IsNullable &&
            (type.NonNullableDisplayName == "global::System.DateTime" ||
             type.NonNullableDisplayName == "global::System.DateTimeOffset"))
        {
            return SoftDeleteKind.Timestamp;
        }

        return SoftDeleteKind.None;
    }

    private static string ResolveColumnName(AttributeData? columnAttribute, AttributeData? foreignKeyAttribute, string propertyName)
    {
        if (columnAttribute is not null)
        {
            return GeneratorHelpers.GetConstructorString(columnAttribute) ?? propertyName;
        }

        // Foreign-key attribute. The 3-arg form (localColumn, referencedTable, referencedColumn)
        // supplies an explicit column name at index 0; the 2-arg form defaults to the property name.
        if (foreignKeyAttribute is { ConstructorArguments.Length: 3 } &&
            foreignKeyAttribute.ConstructorArguments[0].Value is string explicitColumn &&
            !string.IsNullOrWhiteSpace(explicitColumn))
        {
            return explicitColumn;
        }

        return propertyName;
    }

}
