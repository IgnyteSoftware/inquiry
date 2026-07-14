using Inquiry.Generators.Abstractions;
using Inquiry.Generators.Diagnostics;
using Inquiry.Generators.Infrastructure;
using Inquiry.Generators.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.CSharp.Syntax;
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
        => ExtractCore(entitySymbol, isView: false, cancellationToken);

    /// <summary>Extracts a <c>[InquiryView]</c> read-only, keyless-permitted entity.</summary>
    public static EntityData ExtractView(INamedTypeSymbol entitySymbol, CancellationToken cancellationToken)
        => ExtractCore(entitySymbol, isView: true, cancellationToken);

    private static EntityData ExtractCore(INamedTypeSymbol entitySymbol, bool isView, CancellationToken cancellationToken)
    {
        var diagnostics = ImmutableArray.CreateBuilder<DiagnosticData>();
        var location = entitySymbol.Locations.FirstOrDefault();

        // A view reads its name/schema from [InquiryView]; a table from [InquiryTable].
        var nameAttribute = GeneratorHelpers.GetEntityAttribute(entitySymbol, isView ? "InquiryViewAttribute" : "InquiryTableAttribute");
        var tableName = (nameAttribute is not null ? GeneratorHelpers.GetConstructorString(nameAttribute) : null) ?? entitySymbol.Name;
        var schema = nameAttribute is not null ? GeneratorHelpers.GetNamedString(nameAttribute, "Schema") : null;
        // A view is read-only with no FK DDL; for a table, GenerateForeignKeys defaults true.
        var generateForeignKeys = !isView && (nameAttribute is null ||
            GeneratorHelpers.GetNamedBool(nameAttribute, "GenerateForeignKeys", defaultValue: true));
        var generateDdl = !isView && (nameAttribute is null ||
            GeneratorHelpers.GetNamedBool(nameAttribute, "GenerateDdl", defaultValue: true));

        var columns = DiscoverColumns(entitySymbol, diagnostics);
        var relations = DiscoverRelations(entitySymbol, cancellationToken);
        var indexes = DiscoverIndexes(entitySymbol, columns);
        var checks = DiscoverChecks(entitySymbol);

        var keyColumns = columns.Where(static c => c.IsKey).ToImmutableArray();
        var isMapped = true;

        // A view is keyless-permitted: no key is required. (A key may still be declared to enable
        // key-based selects over a view that exposes a unique id.) Tables require a key.
        if (!isView && keyColumns.Length == 0)
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

        // at most one [InquiryCreatedBy] and one [InquiryModifiedBy] (INQ056).
        var createdByColumns = columns.Where(static c => c.IsCreatedBy).ToImmutableArray();
        if (createdByColumns.Length > 1)
        {
            diagnostics.Add(DiagnosticData.Create(InquiryDiagnosticDescriptors.DuplicateAuditUser, location, entitySymbol.Name, createdByColumns[1].PropertyName));
        }

        var modifiedByColumns = columns.Where(static c => c.IsModifiedBy).ToImmutableArray();
        if (modifiedByColumns.Length > 1)
        {
            diagnostics.Add(DiagnosticData.Create(InquiryDiagnosticDescriptors.DuplicateAuditUser, location, entitySymbol.Name, modifiedByColumns[1].PropertyName));
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
            HintName: GeneratorHelpers.GetHintName(entitySymbol, "InquiryEntity"),
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
            Location = LocationData.From(entitySymbol.Locations.FirstOrDefault()),
            Indexes = new EquatableArray<IndexData>(indexes.ToImmutableArray()),
            Checks = new EquatableArray<CheckConstraintData>(checks.ToImmutableArray()),
            SoftDeleteColumn = softDeleteColumns.Length > 0 ? softDeleteColumns[0] : null,
            ConcurrencyToken = concurrencyTokens.Length > 0 ? concurrencyTokens[0] : null,
            GenerateForeignKeys = generateForeignKeys,
            GenerateDdl = generateDdl,
            IsView = isView,
        };
    }

    public static EntityRegistration EmitMaterializer(SourceProductionContext context, EntityData entity, SqlBuilder sqlBuilder)
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
        MaterializerEmitter.EmitMaterializeBody(source, entity.Columns, entityType, sqlBuilder, indent: "        ");
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
        MaterializerEmitter.EmitMaterializeBody(source, entity.Columns, entityType, sqlBuilder, indent: "        ");
        source.AppendLine("    }");
        source.AppendLine("}");

        GeneratorHelpers.AppendNamespaceEnd(source, entity.Namespace);

        context.AddSource(entity.HintName, SourceText.From(source.ToString(), Encoding.UTF8));
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
            // [InquiryCreatedAt]/[InquiryModifiedAt]/[InquiryCreatedBy]/[InquiryModifiedBy] derive
            // InquiryColumnAttribute, so they are discovered as columns (like [InquiryKey]).
            var createdAtAttribute = GeneratorHelpers.GetEntityAttribute(property, "InquiryCreatedAtAttribute");
            var modifiedAtAttribute = GeneratorHelpers.GetEntityAttribute(property, "InquiryModifiedAtAttribute");
            var createdByAttribute = GeneratorHelpers.GetEntityAttribute(property, "InquiryCreatedByAttribute");
            var modifiedByAttribute = GeneratorHelpers.GetEntityAttribute(property, "InquiryModifiedByAttribute");
            var columnAttribute = keyAttribute ?? concurrencyTokenAttribute ?? createdAtAttribute ?? modifiedAtAttribute
                ?? createdByAttribute ?? modifiedByAttribute
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

            // [InquiryGlobalFilter]: a non-nullable bool column whose value every SELECT filters on. It
            // cannot double as the key, a generated/db-default column, the soft-delete indicator, or a
            // concurrency token — those own the column's value (INQ059, flag cleared so emission stays valid).
            var isGlobalFilter = false;
            var globalFilterKeepWhenTrue = true;
            var globalFilterAttribute = GeneratorHelpers.GetEntityAttribute(property, "InquiryGlobalFilterAttribute");
            if (globalFilterAttribute is not null)
            {
                var isBool = !typeData.IsNullable && typeData.SpecialType == SpecialType.System_Boolean;
                if (!isBool || keyAttribute is not null || isGenerated || useDatabaseDefault ||
                    isConcurrencyToken || softDelete != SoftDeleteKind.None)
                {
                    diagnostics.Add(DiagnosticData.Create(
                        InquiryDiagnosticDescriptors.GlobalFilterInvalid,
                        property.Locations.FirstOrDefault(),
                        entitySymbol.Name,
                        property.Name));
                }
                else
                {
                    isGlobalFilter = true;
                    globalFilterKeepWhenTrue = GeneratorHelpers.GetNamedBool(globalFilterAttribute, "KeepWhen", defaultValue: true);
                }
            }

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

            // Auditing user columns: a writable string column no other machinery owns (INQ055
            // otherwise; flags cleared so emission stays valid).
            var isCreatedBy = createdByAttribute is not null;
            var isModifiedBy = modifiedByAttribute is not null;
            if (isCreatedBy || isModifiedBy)
            {
                if (typeData.SpecialType != SpecialType.System_String || (isCreatedBy && isModifiedBy) ||
                    keyAttribute is not null || isGenerated || useDatabaseDefault || isConcurrencyToken ||
                    GeneratorHelpers.GetEntityAttribute(property, "InquirySoftDeleteAttribute") is not null)
                {
                    diagnostics.Add(DiagnosticData.Create(
                        InquiryDiagnosticDescriptors.AuditUserInvalid,
                        property.Locations.FirstOrDefault(),
                        entitySymbol.Name,
                        property.Name));
                    isCreatedBy = false;
                    isModifiedBy = false;
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
            var lengthSpecified = HasNamedArgument(metadataAttribute, "Length");
            var precisionSpecified = HasNamedArgument(metadataAttribute, "Precision");
            var scaleSpecified = HasNamedArgument(metadataAttribute, "Scale");
            var isUnicodeSpecified = HasNamedArgument(metadataAttribute, "IsUnicode");
            var length = (metadataAttribute is not null ? GeneratorHelpers.GetNamedInt(metadataAttribute, "Length") : null) ?? 0;
            var precision = (metadataAttribute is not null ? GeneratorHelpers.GetNamedInt(metadataAttribute, "Precision") : null) ?? 0;
            var scale = (metadataAttribute is not null ? GeneratorHelpers.GetNamedInt(metadataAttribute, "Scale") : null) ?? 0;
            var defaultExpression = metadataAttribute is not null ? GeneratorHelpers.GetNamedString(metadataAttribute, "DefaultExpression") : null;

            // INQ065: Length/Precision/Scale are read above as raw ints with no range check. A negative
            // value, a Precision past the portable SQL maximum of 38 (also the byte ceiling for #56's Size
            // emission), or a Scale exceeding its Precision produces invalid DDL or a broken binder. Flag the
            // first offending value at the property. Unset metadata (all 0) is left alone.
            var rangeError =
                length < 0 ? "Length (" + length + ") cannot be negative"
                : precision < 0 ? "Precision (" + precision + ") cannot be negative"
                : scale < 0 ? "Scale (" + scale + ") cannot be negative"
                : precision > 38 ? "Precision (" + precision + ") exceeds the maximum of 38 (use SqlType for a wider decimal)"
                : scale > precision && typeData.SpecialType != SpecialType.System_DateTime &&
                    typeData.NonNullableDisplayName != "global::System.DateTimeOffset" && !typeData.IsTimeOnly
                    ? "Scale (" + scale + ") cannot exceed Precision (" + precision + ")"
                : null;
            if (rangeError is not null)
            {
                diagnostics.Add(DiagnosticData.Create(
                    InquiryDiagnosticDescriptors.ColumnMetadataOutOfRange,
                    property.Locations.FirstOrDefault(),
                    entitySymbol.Name,
                    columnName,
                    rangeError));
            }

            // A server-computed column is calculated by the database; it cannot also be a key,
            // database-generated/defaulted, an auditing column, soft-delete, or a concurrency token
            // (INQ057, expression cleared so emission stays valid).
            var computedExpression = metadataAttribute is not null ? GeneratorHelpers.GetNamedString(metadataAttribute, "Computed") : null;
            var computedExpressionLocation = GetNamedArgumentLocation(metadataAttribute, "Computed");
            var computedOverrides = property.GetAttributes()
                .Where(static attribute => attribute.AttributeClass?.ToDisplayString() == "Inquiry.Entities.InquiryComputedExpressionAttribute")
                .Select(static attribute => new ComputedExpressionOverrideData(
                    attribute.ConstructorArguments.Length > 0 ? attribute.ConstructorArguments[0].Value as string ?? string.Empty : string.Empty,
                    attribute.ConstructorArguments.Length > 1 ? attribute.ConstructorArguments[1].Value as string ?? string.Empty : string.Empty,
                    GetConstructorArgumentLocation(attribute, "providerId", 0),
                    GetConstructorArgumentLocation(attribute, "expression", 1)))
                .ToImmutableArray();
            var hasComputedMetadata = !string.IsNullOrEmpty(computedExpression);
            if (!string.IsNullOrEmpty(computedExpression) &&
                (keyAttribute is not null || isGenerated || useDatabaseDefault || isConcurrencyToken ||
                 isCreatedAt || isModifiedAt || isCreatedBy || isModifiedBy || softDelete != SoftDeleteKind.None ||
                 !string.IsNullOrEmpty(defaultExpression)))
            {
                diagnostics.Add(DiagnosticData.Create(
                    InquiryDiagnosticDescriptors.ComputedColumnInvalid,
                    property.Locations.FirstOrDefault(),
                    entitySymbol.Name,
                    columnName));
                computedExpression = null;
            }

            var (foreignKeySchema, foreignKeyTable, foreignKeyColumn) = ReadForeignKeyReference(foreignKeyAttribute);

            // a value converter (explicit Converter=typeof(X), or [InquiryJson] → built-in JSON
            // converter) maps a non-primitive property to/from a provider primitive.
            var converter = ResolveConverter(property, columnAttribute, typeData, entitySymbol, diagnostics);

            if (isDatabaseGeneratedToken &&
                (!typeData.IsByteArray || typeData.IsNullable || keyAttribute is not null || isGenerated || useDatabaseDefault ||
                 !string.IsNullOrEmpty(sqlType) || length != 0 || precision != 0 || scale != 0 ||
                 !string.IsNullOrEmpty(defaultExpression) || hasComputedMetadata || converter is not null))
            {
                diagnostics.Add(DiagnosticData.Create(
                    InquiryDiagnosticDescriptors.DatabaseGeneratedConcurrencyTokenInvalid,
                    property.Locations.FirstOrDefault(),
                    entitySymbol.Name,
                    property.Name,
                    "Use a non-nullable byte[] and remove conflicting column metadata."));
                isDatabaseGeneratedToken = false;
                isConcurrencyToken = false;
            }

            columns.Add(new ColumnData
            {
                Location = LocationData.From(property.Locations.FirstOrDefault()),
                PropertyName = property.Name,
                ColumnName = columnName,
                Type = typeData,
                IsKey = isKey,
                IsGenerated = isGenerated,
                IsSequentialGuid = isSequentialGuid,
                UseDatabaseDefault = useDatabaseDefault,
                SoftDelete = softDelete,
                IsGlobalFilter = isGlobalFilter,
                GlobalFilterKeepWhenTrue = globalFilterKeepWhenTrue,
                IsConcurrencyToken = isConcurrencyToken,
                IsDatabaseGeneratedToken = isDatabaseGeneratedToken,
                IsCreatedAt = isCreatedAt,
                IsModifiedAt = isModifiedAt,
                IsCreatedBy = isCreatedBy,
                IsModifiedBy = isModifiedBy,
                EnumAsString = enumAsString,
                // a converter column's DDL type reflects the PROVIDER primitive it stores, not the model type.
                TypeClass = converter is not null
                    ? converter.ProviderType is not null ? MapTypeClass(converter.ProviderType) : MapSpecialType(converter.ProviderSpecialType)
                    : enumAsString ? DbTypeClass.String : MapTypeClass(typeData),
                IsNullable = !isKey && typeData.IsNullable,
                SqlType = sqlType,
                SqlTypeLocation = GetNamedArgumentLocation(metadataAttribute, "SqlType"),
                ProviderClrTypeName = converter is not null
                    ? converter.ProviderType?.NonNullableDisplayName ?? converter.ProviderTypeDisplay
                    : enumAsString ? "global::System.String"
                    : typeData.IsEnum ? SpecialTypeDisplayName(typeData.EnumUnderlyingSpecialType)
                    : typeData.NonNullableDisplayName,
                ProviderValueIsNullable = converter?.ProviderType?.IsNullable == true,
                Length = length,
                IsLengthSpecified = lengthSpecified,
                LengthLocation = GetNamedArgumentLocation(metadataAttribute, "Length"),
                Precision = precision,
                IsPrecisionSpecified = precisionSpecified,
                PrecisionLocation = GetNamedArgumentLocation(metadataAttribute, "Precision"),
                Scale = scale,
                IsScaleSpecified = scaleSpecified,
                ScaleLocation = GetNamedArgumentLocation(metadataAttribute, "Scale"),
                DefaultExpression = defaultExpression,
                DefaultExpressionLocation = GetNamedArgumentLocation(metadataAttribute, "DefaultExpression"),
                UseDatabaseDefaultLocation = GetNamedArgumentLocation(metadataAttribute, "UseDatabaseDefault"),
                ComputedExpression = computedExpression,
                ComputedExpressionLocation = computedExpressionLocation,
                ComputedExpressionOverrides = new EquatableArray<ComputedExpressionOverrideData>(computedOverrides),
                ForeignKeyTable = foreignKeyTable,
                ForeignKeySchema = foreignKeySchema,
                ForeignKeyColumn = foreignKeyColumn,
                ForeignKeyConstraintName = foreignKeyAttribute is null ? null : GeneratorHelpers.GetNamedString(foreignKeyAttribute, "ConstraintName"),
                ForeignKeyOnDelete = foreignKeyAttribute is null ? 0 : GetNamedEnumValue(foreignKeyAttribute, "OnDelete"),
                ForeignKeyOnUpdate = foreignKeyAttribute is null ? 0 : GetNamedEnumValue(foreignKeyAttribute, "OnUpdate"),
                IsIndexed = metadataAttribute is not null && GeneratorHelpers.GetNamedBool(metadataAttribute, "IsIndexed"),
                IsUnicode = metadataAttribute is null || GeneratorHelpers.GetNamedBool(metadataAttribute, "IsUnicode", true),
                IsUnicodeSpecified = isUnicodeSpecified,
                IsUnicodeLocation = GetNamedArgumentLocation(metadataAttribute, "IsUnicode"),
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

    private static LocationData? GetNamedArgumentLocation(AttributeData? attribute, string name)
    {
        if (attribute?.ApplicationSyntaxReference?.GetSyntax() is not AttributeSyntax syntax) return null;
        var argument = syntax.ArgumentList?.Arguments.FirstOrDefault(value => value.NameEquals?.Name.Identifier.ValueText == name);
        return LocationData.From(argument?.Expression.GetLocation());
    }

    private static LocationData? GetConstructorArgumentLocation(AttributeData attribute, string parameterName, int ordinal)
    {
        if (attribute.ApplicationSyntaxReference?.GetSyntax() is not AttributeSyntax syntax || syntax.ArgumentList is null) return null;
        var named = syntax.ArgumentList.Arguments.FirstOrDefault(argument =>
            argument.NameColon?.Name.Identifier.ValueText == parameterName);
        if (named is not null) return LocationData.From(named.Expression.GetLocation());
        if (ordinal < 0 || ordinal >= syntax.ArgumentList.Arguments.Count) return null;
        var positional = syntax.ArgumentList.Arguments[ordinal];
        return positional.NameColon is null && positional.NameEquals is null
            ? LocationData.From(positional.Expression.GetLocation())
            : null;
    }

    private static int GetNamedEnumValue(AttributeData attribute, string name)
    {
        foreach (var argument in attribute.NamedArguments)
        {
            if (argument.Key == name && argument.Value.Value is int value) return value;
        }
        return 0;
    }

    private static ImmutableArray<IndexData>.Builder DiscoverIndexes(INamedTypeSymbol entity, IReadOnlyList<ColumnData> columns)
    {
        var result = ImmutableArray.CreateBuilder<IndexData>();
        var byProperty = columns.ToDictionary(static c => c.PropertyName, StringComparer.Ordinal);
        var ordinal = 0;
        foreach (var attribute in entity.GetAttributes().Where(static a => a.AttributeClass?.ToDisplayString() == "Inquiry.Entities.InquiryIndexAttribute"))
        {
            var logicalKeys = ReadStringArray(attribute.ConstructorArguments.Length > 0 ? attribute.ConstructorArguments[0] : default).ToImmutableArray();
            var keys = logicalKeys
                .Select(p => byProperty.TryGetValue(p, out var c) ? c.ColumnName : "").ToImmutableArray();
            var includeArg = attribute.NamedArguments.FirstOrDefault(static p => p.Key == "Include").Value;
            var logicalIncludes = ReadStringArray(includeArg).ToImmutableArray();
            var includes = logicalIncludes.Select(p => byProperty.TryGetValue(p, out var c) ? c.ColumnName : "").ToImmutableArray();
            result.Add(new IndexData(null, string.Empty, new EquatableArray<string>(keys), new EquatableArray<string>(includes),
                GeneratorHelpers.GetNamedBool(attribute, "IsUnique"), GeneratorHelpers.GetNamedString(attribute, "Name"),
                LocationData.From(attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation()))
            {
                LogicalKeyProperties = new EquatableArray<string>(logicalKeys),
                LogicalIncludeProperties = new EquatableArray<string>(logicalIncludes),
                Origin = IndexOrigin.TableAttribute,
                Ordinal = ordinal++,
            });
        }
        return result;
    }

    private static ImmutableArray<CheckConstraintData>.Builder DiscoverChecks(INamedTypeSymbol entity)
    {
        var result = ImmutableArray.CreateBuilder<CheckConstraintData>();
        var ordinal = 0;
        foreach (var attribute in entity.GetAttributes().Where(static a => a.AttributeClass?.ToDisplayString() == "Inquiry.Entities.InquiryCheckAttribute"))
        {
            var expression = attribute.ConstructorArguments.Length > 0 ? attribute.ConstructorArguments[0].Value as string ?? string.Empty : string.Empty;
            result.Add(new CheckConstraintData(null, string.Empty, expression, GeneratorHelpers.GetNamedString(attribute, "Name"),
                LocationData.From(attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation())) { Ordinal = ordinal++ });
        }
        return result;
    }

    private static IEnumerable<string> ReadStringArray(TypedConstant constant)
        => constant.Kind == TypedConstantKind.Array
            ? constant.Values.Select(static value => value.Value as string ?? string.Empty)
            : Enumerable.Empty<string>();

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

    private static bool HasNamedArgument(AttributeData? attribute, string name)
        => attribute?.NamedArguments.Any(pair => pair.Key == name) == true;

    private static string SpecialTypeDisplayName(SpecialType type) => type switch
    {
        SpecialType.System_SByte => "global::System.SByte",
        SpecialType.System_Byte => "global::System.Byte",
        SpecialType.System_Int16 => "global::System.Int16",
        SpecialType.System_UInt16 => "global::System.UInt16",
        SpecialType.System_Int32 => "global::System.Int32",
        SpecialType.System_UInt32 => "global::System.UInt32",
        SpecialType.System_Int64 => "global::System.Int64",
        SpecialType.System_UInt64 => "global::System.UInt64",
        _ => "global::System.Int32",
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

            var providerTypeData = TypeData.Create(providerType, providerType.NullableAnnotation);
            if (!IsSupportedConverterProviderType(providerTypeData))
            {
                diagnostics.Add(DiagnosticData.Create(
                    InquiryDiagnosticDescriptors.ConverterProviderTypeUnsupported,
                    property.Locations.FirstOrDefault(),
                    entitySymbol.Name,
                    converterType.Name,
                    property.Name,
                    providerType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
                return null;
            }

            return new ConverterData(
                converterType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                providerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                providerType.SpecialType)
            {
                ProviderType = providerTypeData,
            };
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

    private static bool IsSupportedConverterProviderType(TypeData type)
        => !type.IsNullable &&
           (type.IsByteArray || type.IsGuid || type.IsDateOnly || type.IsTimeOnly ||
            type.NonNullableDisplayName == "global::System.DateTimeOffset" ||
            type.SpecialType is SpecialType.System_Boolean or SpecialType.System_Byte or SpecialType.System_SByte
                or SpecialType.System_Int16 or SpecialType.System_UInt16 or SpecialType.System_Int32
                or SpecialType.System_UInt32 or SpecialType.System_Int64 or SpecialType.System_UInt64
                or SpecialType.System_Single or SpecialType.System_Double or SpecialType.System_Decimal
                or SpecialType.System_Char or SpecialType.System_String or SpecialType.System_DateTime);

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

            // [InquiryManyToMany(typeof(Junction), parentFkProp, childFkProp)] on a collection property:
            // resolved through a junction entity rather than a foreign key on the child.
            var manyToManyAttribute = GeneratorHelpers.GetEntityAttribute(property, "InquiryManyToManyAttribute");
            if (manyToManyAttribute is not null)
            {
                if (manyToManyAttribute.ConstructorArguments.Length < 3 ||
                    manyToManyAttribute.ConstructorArguments[0].Value is not INamedTypeSymbol junctionSymbol ||
                    manyToManyAttribute.ConstructorArguments[1].Value is not string parentFk ||
                    manyToManyAttribute.ConstructorArguments[2].Value is not string childFk ||
                    !TryGetChildEntityType(property.Type, out var manyChildSymbol, out var manyIsCollection) ||
                    !manyIsCollection)
                {
                    // A non-collection M:N (or malformed args) is reported by ValidateRelations (INQ063);
                    // still record it (with whatever child type we found) so the diagnostic has a target.
                    // This fallback record is diagnostic-only and never load-bearing: IsCollection is forced
                    // false so every emit-time check (ValidateRelations / TryValidateForEmit) treats it as
                    // invalid and drops it — no SQL or loader is ever generated from it.
                    TryGetChildEntityType(property.Type, out var fallbackChild, out _);
                    relations.Add(new RelationData(
                        property.Name,
                        string.Empty,
                        fallbackChild?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? string.Empty,
                        IsCollection: false,
                        LocationData.From(property.Locations.FirstOrDefault()))
                    {
                        IsManyToMany = true,
                        JunctionEntityFullyQualifiedName = (manyToManyAttribute.ConstructorArguments.Length > 0
                            ? manyToManyAttribute.ConstructorArguments[0].Value as INamedTypeSymbol
                            : null)?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        JunctionParentForeignKeyProperty = manyToManyAttribute.ConstructorArguments.Length > 1 ? manyToManyAttribute.ConstructorArguments[1].Value as string : null,
                        JunctionChildForeignKeyProperty = manyToManyAttribute.ConstructorArguments.Length > 2 ? manyToManyAttribute.ConstructorArguments[2].Value as string : null,
                    });
                    continue;
                }

                relations.Add(new RelationData(
                    property.Name,
                    string.Empty,
                    manyChildSymbol!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    IsCollection: true,
                    LocationData.From(property.Locations.FirstOrDefault()))
                {
                    IsManyToMany = true,
                    JunctionEntityFullyQualifiedName = junctionSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    JunctionParentForeignKeyProperty = parentFk,
                    JunctionChildForeignKeyProperty = childFk,
                });
                continue;
            }

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
                isCollection,
                LocationData.From(property.Locations.FirstOrDefault())));
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
