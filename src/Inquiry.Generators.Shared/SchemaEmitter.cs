using Inquiry.Generators.Abstractions;
using Inquiry.Generators.Diagnostics;
using Inquiry.Generators.Infrastructure;
using Inquiry.Generators.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Inquiry.Generators;

/// <summary>
/// Emits a per-assembly <c>InquiryGeneratedSchema.g.cs</c> exposing the full <c>CREATE TABLE</c>
/// DDL for every mapped entity as a single <c>const string</c>. Tables are ordered so a referenced
/// table is created before the table that references it (topological sort over foreign keys; self
/// references and cross-assembly references do not constrain ordering).
/// </summary>
/// <remarks>
/// DDL generation only. ALTER/diff, versioning, a history table, and an apply runner are out
/// of scope — feed <see cref="Ddl"/> into a migration runner (e.g. DbUp) as the initial script, or use
/// it for first-run/dev table creation.
/// </remarks>
internal static class SchemaEmitter
{
    public const string GeneratedClassName = "InquiryGeneratedSchema";
    private const string GeneratedNamespace = "Inquiry.Generated";

    public static void Emit(
        SourceProductionContext context,
        IReadOnlyList<EntityData> entities,
        SqlBuilder builder,
        IReadOnlyList<CollectionParameterArtifact> providerArtifacts,
        bool emitManifestMetadata)
    {
        entities = entities.Where(static entity => entity.GenerateDdl).ToArray();
        entities = SuppressInvalidGeneratedKeySchemas(context, entities);
        if (entities.Count == 0 && providerArtifacts.Count == 0)
        {
            return;
        }

        var discoveredEntities = entities;
        entities = SelectPhysicalTableRepresentatives(context, discoveredEntities);

        // A string foreign-key column with no declared Length inherits its referenced column's declared
        // Length, so on a bounded dialect it emits a valid bounded VARCHAR instead of an unindexable/
        // unkeyable LOB. Indexed by (schema, table, column) across every entity (the referenced table may be any).
        var declaredLengths = BuildColumnLengthIndex(entities);

        ReportDatabaseGeneratedTokenDiagnostics(context, entities, builder);

        ReportKeyDiagnostics(context, entities, builder, declaredLengths);

        var normalizedIndexes = new Dictionary<string, IReadOnlyList<IndexData>>(System.StringComparer.Ordinal);
        var normalizedChecks = new Dictionary<string, IReadOnlyList<CheckConstraintData>>(System.StringComparer.Ordinal);
        foreach (var entity in entities)
        {
            normalizedIndexes[entity.FullyQualifiedName] = NormalizeIndexes(context, entity, builder, declaredLengths);
            normalizedChecks[entity.FullyQualifiedName] = NormalizeChecks(context, entity, builder)
                .Select(check => check with { Expression = builder.RenderCheckExpression(check.Expression) }).ToArray();
        }
        var graph = AnalyzeForeignKeys(context, entities, builder);
        ValidateCrossEntityObjectNames(context, entities, builder, normalizedIndexes, normalizedChecks);
        foreach (var invalid in graph.InvalidMappings)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                InquiryDiagnosticDescriptors.DuplicateSchemaMapping,
                invalid.Location?.ToLocation(),
                invalid.Identity,
                invalid.Reason));
        }
        var ordered = OrderByForeignKeyDependencies(entities, graph.CyclicIdentities);
        var suppressedByTable = new Dictionary<string, ISet<string>>(System.StringComparer.Ordinal);
        var deferredForeignKeys = new List<ForeignKeyConstraintData>();
        foreach (var invalid in graph.InvalidForeignKeys)
        {
            var localKey = TableKey(invalid.LocalSchema, invalid.LocalTable);
            if (!suppressedByTable.TryGetValue(localKey, out var invalidColumns))
            {
                invalidColumns = new HashSet<string>(System.StringComparer.Ordinal);
                suppressedByTable.Add(localKey, invalidColumns);
            }
            invalidColumns.Add(invalid.LocalColumn);
        }
        foreach (var foreignKey in graph.ForeignKeys)
        {
            if (!graph.CyclicIdentities.Contains(foreignKey.CanonicalIdentity)
                || builder.CyclicForeignKeyStrategy == CyclicForeignKeyStrategy.Inline)
            {
                continue;
            }

            var localKey = TableKey(foreignKey.LocalSchema, foreignKey.LocalTable);
            if (!suppressedByTable.TryGetValue(localKey, out var columns))
            {
                columns = new HashSet<string>(System.StringComparer.Ordinal);
                suppressedByTable.Add(localKey, columns);
            }

            columns.Add(foreignKey.LocalColumn);
            if (builder.CyclicForeignKeyStrategy == CyclicForeignKeyStrategy.AlterTable)
            {
                deferredForeignKeys.Add(foreignKey);
            }
            else
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    InquiryDiagnosticDescriptors.CyclicForeignKeyNotSupported,
                    foreignKey.Location?.ToLocation(),
                    foreignKey.LocalTable,
                    foreignKey.LocalColumn,
                    builder.DialectName));
            }
        }

        var finalTables = new List<(EntityData Entity, List<IColumn> Columns, SqlBuildContext Context)>();
        foreach (var entity in ordered)
        {
            var columns = ResolveColumns(entity, declaredLengths, builder);
            var ctx = new SqlBuildContext(builder, entity.Schema, entity.TableName, columns, suppressSoftDelete: false, generateForeignKeys: entity.GenerateForeignKeys)
            {
                SuppressedForeignKeyColumns = suppressedByTable.TryGetValue(TableKey(entity.Schema, entity.TableName), out var suppressed) ? suppressed : null,
                NormalizedForeignKeys = graph.ForeignKeys.Where(fk => TableKey(fk.LocalSchema, fk.LocalTable) == TableKey(entity.Schema, entity.TableName)).ToArray(),
                NormalizedIndexes = normalizedIndexes[entity.FullyQualifiedName],
                NormalizedChecks = normalizedChecks[entity.FullyQualifiedName],
            };
            finalTables.Add((entity, columns, ctx));
        }

        var manifest = BuildSchemaManifest(builder, finalTables, providerArtifacts);
        var manifestJson = SchemaManifestWriter.Write(manifest);
        var manifestSha256 = SchemaManifestWriter.Sha256(manifestJson);
        if (!SchemaManifestWriter.TryBuildTransport(manifestJson, 10_000, out var manifestChunks, out var requiredChunkCount))
        {
            context.ReportDiagnostic(Diagnostic.Create(InquiryDiagnosticDescriptors.SchemaManifestTooLarge, null, requiredChunkCount));
            return;
        }

        var ddl = new StringBuilder();
        // Index statements are collected and appended after every CREATE TABLE so a referenced table
        // always exists before its index is created.
        var indexStatements = new List<string>();
        for (var i = 0; i < finalTables.Count; i++)
        {
            if (i > 0)
            {
                ddl.Append("\n\n");
            }

            var ctx = finalTables[i].Context;
            ddl.Append(builder.BuildCreateTableSql(ctx));
            ddl.Append(';');
            indexStatements.AddRange(builder.BuildCreateIndexSql(ctx));
        }

        foreach (var foreignKey in deferredForeignKeys)
        {
            ddl.Append("\n\n").Append(builder.BuildAddForeignKeySql(foreignKey)).Append(';');
        }

        foreach (var indexStatement in indexStatements)
        {
            ddl.Append("\n\n").Append(indexStatement).Append(';');
        }

        var source = new StringBuilder();
        source.AppendLine("// <auto-generated />");
        source.AppendLine("#nullable enable");
        if (emitManifestMetadata)
        {
            source.AppendLine($"[assembly: global::System.Reflection.AssemblyMetadataAttribute(\"Inquiry.SchemaManifest.FormatVersion\", \"1\")] ");
            source.AppendLine($"[assembly: global::System.Reflection.AssemblyMetadataAttribute(\"Inquiry.SchemaManifest.Sha256\", \"{manifestSha256}\")] ");
            source.AppendLine($"[assembly: global::System.Reflection.AssemblyMetadataAttribute(\"Inquiry.SchemaManifest.ChunkCount\", \"{manifestChunks.Count}\")] ");
            for (var i = 0; i < manifestChunks.Count; i++)
                source.AppendLine($"[assembly: global::System.Reflection.AssemblyMetadataAttribute(\"Inquiry.SchemaManifest.Chunk.{i:D4}\", \"{EscapeCSharpString(manifestChunks[i])}\")] ");
        }
        source.AppendLine($"namespace {GeneratedNamespace};");
        source.AppendLine();
        source.AppendLine("/// <summary>Generated CREATE TABLE DDL for every Inquiry entity in this assembly.</summary>");
        // internal (not public): each assembly emits its own copy, so a referencing assembly that also
        // uses Inquiry does not collide on a single public Inquiry.Generated.InquiryGeneratedSchema type.
        source.AppendLine($"internal static class {GeneratedClassName}");
        source.AppendLine("{");
        source.AppendLine("    public const int SchemaManifestFormatVersion = 1;");
        source.AppendLine($"    public const string SchemaManifestJson = @\"{manifestJson.Replace("\"", "\"\"")}\";");
        source.AppendLine($"    public const string SchemaManifestSha256 = \"{manifestSha256}\";");
        source.AppendLine($"    public const int SchemaManifestChunkCount = {manifestChunks.Count};");
        if (providerArtifacts.Count > 0)
        {
            var schemaDdl = providerArtifacts
                .Where(static artifact => artifact.SchemaDdl.Length > 0)
                .GroupBy(static artifact => artifact.Schema, System.StringComparer.Ordinal)
                .OrderBy(static group => group.Key, System.StringComparer.Ordinal)
                .Select(static group => group.First().SchemaDdl);
            var artifactDdl = string.Join("", schemaDdl)
                + string.Join("\n\n", providerArtifacts.Select(static artifact => artifact.CreateDdl));
            if (ddl.Length > 0) artifactDdl += "\n\n";
            var validationSql = string.Join("\nUNION ALL\n", providerArtifacts.Select(static artifact =>
                $"SELECT N'{artifact.ValidationName.Replace("'", "''")}' AS [ArtifactName], N'{artifact.ElementSignature.Replace("'", "''")}' AS [ExpectedElementSignature] WHERE TYPE_ID(N'{artifact.ValidationName.Replace("'", "''")}') IS NULL"));
            source.AppendLine("    /// <summary>Additive setup DDL for provider-owned schema artifacts.</summary>");
            source.AppendLine($"    public const string ProviderArtifactsDdl = @\"{artifactDdl.Replace("\"", "\"\"")}\";");
            source.AppendLine("    /// <summary>Read-only validation query returning one row per missing provider artifact.</summary>");
            source.AppendLine($"    public const string ProviderArtifactsValidationSql = @\"{validationSql.Replace("\"", "\"\"")}\";");
            source.AppendLine("    /// <summary>The full provider-artifact and table/index schema DDL.</summary>");
            source.AppendLine($"    public const string Ddl = ProviderArtifactsDdl + @\"{ddl.ToString().Replace("\"", "\"\"")}\";");
        }
        else
        {
            source.AppendLine("    /// <summary>The full schema DDL, tables ordered so referenced tables precede their dependents.</summary>");
            source.AppendLine($"    public const string Ddl = @\"{ddl.ToString().Replace("\"", "\"\"")}\";");
        }
        source.AppendLine("}");

        context.AddSource($"{GeneratedClassName}.g.cs", SourceText.From(source.ToString(), Encoding.UTF8));
    }

    private static IReadOnlyList<EntityData> SuppressInvalidGeneratedKeySchemas(SourceProductionContext context, IReadOnlyList<EntityData> entities)
    {
        var valid = new List<EntityData>(entities.Count);
        foreach (var entity in entities)
        {
            var invalid = false;
            foreach (var key in entity.Keys)
            {
                if (!key.IsGenerated) continue;
                if (!string.IsNullOrEmpty(key.SqlType))
                {
                    context.ReportDiagnostic(Diagnostic.Create(InquiryDiagnosticDescriptors.GeneratedKeySchemaFacetInvalid,
                        key.SqlTypeLocation?.ToLocation() ?? key.Location?.ToLocation(), entity.TableName, key.ColumnName, "SqlType"));
                    invalid = true;
                }
                if (!string.IsNullOrEmpty(key.DefaultExpression))
                {
                    context.ReportDiagnostic(Diagnostic.Create(InquiryDiagnosticDescriptors.GeneratedKeySchemaFacetInvalid,
                        key.DefaultExpressionLocation?.ToLocation() ?? key.Location?.ToLocation(), entity.TableName, key.ColumnName, "DefaultExpression"));
                    invalid = true;
                }
                if (key.UseDatabaseDefault)
                {
                    context.ReportDiagnostic(Diagnostic.Create(InquiryDiagnosticDescriptors.GeneratedKeySchemaFacetInvalid,
                        key.UseDatabaseDefaultLocation?.ToLocation() ?? key.Location?.ToLocation(), entity.TableName, key.ColumnName, "UseDatabaseDefault"));
                    invalid = true;
                }
            }
            if (!invalid) valid.Add(entity);
        }
        return valid;
    }

    private static SchemaManifestData BuildSchemaManifest(SqlBuilder builder,
        IReadOnlyList<(EntityData Entity, List<IColumn> Columns, SqlBuildContext Context)> tables,
        IReadOnlyList<CollectionParameterArtifact> artifacts)
    {
        var result = tables.OrderBy(table => builder.GetPhysicalIdentifierSortKey(table.Entity.Schema ?? string.Empty), System.StringComparer.Ordinal)
            .ThenBy(table => table.Entity.Schema ?? string.Empty, System.StringComparer.Ordinal)
            .ThenBy(table => builder.GetPhysicalIdentifierSortKey(table.Entity.TableName), System.StringComparer.Ordinal)
            .ThenBy(table => table.Entity.TableName, System.StringComparer.Ordinal)
            .ThenBy(static table => TableKey(table.Entity.Schema, table.Entity.TableName), System.StringComparer.Ordinal)
            .Select(table =>
            {
                var keys = table.Entity.Keys.AsImmutableArray().Select(static key => key.ColumnName).ToArray();
                var columns = table.Columns.Select(column =>
                {
                    var keyOrdinal = System.Array.IndexOf(keys, column.ColumnName);
                    var generation = column.IsDatabaseGeneratedToken ? "rowversion"
                        : !string.IsNullOrEmpty(column.ComputedExpression) ? "computed"
                        : column.IsGenerated ? "identity"
                        : !string.IsNullOrEmpty(column.DefaultExpression) ? "default" : "none";
                    var concurrency = !column.IsConcurrencyToken ? "none" : column.IsDatabaseGeneratedToken ? "database" : "application";
                    var storeType = builder.GetSchemaManifestStoreType(column);
                    return new SchemaManifestColumnData(column.ColumnName, storeType, storeType is null ? "database" : "explicit",
                        TypeClassToken(column.TypeClass), !string.IsNullOrEmpty(column.ComputedExpression) || column.IsNullable, keyOrdinal < 0 ? null : keyOrdinal,
                        generation, column.DefaultExpression, column.ComputedExpression, concurrency);
                }).ToArray();
                var indexes = (table.Context.NormalizedIndexes ?? System.Array.Empty<IndexData>())
                    .OrderBy(static index => index.CanonicalIdentity, System.StringComparer.Ordinal)
                    .Select(index => new SchemaManifestIndexData(index.EmittedName!, index.IsUnique, index.KeyColumns.AsImmutableArray(), index.IncludeColumns.AsImmutableArray())).ToArray();
                var checks = (table.Context.NormalizedChecks ?? System.Array.Empty<CheckConstraintData>())
                    .OrderBy(static check => check.CanonicalIdentity, System.StringComparer.Ordinal)
                    .Select(check => new SchemaManifestCheckData(check.EmittedName!, check.Expression)).ToArray();
                var foreignKeys = (table.Context.NormalizedForeignKeys ?? System.Array.Empty<ForeignKeyConstraintData>())
                    .Where(static foreignKey => foreignKey.EmissionMode != ForeignKeyEmissionMode.Suppressed)
                    .OrderBy(static foreignKey => foreignKey.CanonicalIdentity, System.StringComparer.Ordinal)
                    .Select(foreignKey => new SchemaManifestForeignKeyData(foreignKey.EmittedName,
                        new[] { foreignKey.LocalColumn }, foreignKey.ReferencedSchema, foreignKey.ReferencedTable,
                        new[] { foreignKey.ReferencedColumn }, ActionToken(foreignKey.OnDelete), ActionToken(foreignKey.OnUpdate))).ToArray();
                return new SchemaManifestTableData(table.Entity.Schema, table.Entity.TableName, columns,
                    keys.Length == 0 ? null : keys, indexes, checks, foreignKeys);
            }).ToArray();
        var providerArtifacts = artifacts.OrderBy(static artifact => artifact.Schema, System.StringComparer.Ordinal)
            .ThenBy(static artifact => artifact.Name, System.StringComparer.Ordinal)
            .Select(artifact => new SchemaManifestArtifactData(artifact.Schema, artifact.Name,
                builder.GetProviderArtifactKind(artifact), builder.GetProviderArtifactSignature(artifact))).ToArray();
        return new SchemaManifestData(builder.ProviderId, result, providerArtifacts);
    }

    private static string TypeClassToken(DbTypeClass type) => type switch
    {
        DbTypeClass.String => "string", DbTypeClass.Boolean => "boolean", DbTypeClass.Byte => "byte",
        DbTypeClass.Int16 => "int16", DbTypeClass.Int32 => "int32", DbTypeClass.Int64 => "int64",
        DbTypeClass.Single => "single", DbTypeClass.Double => "double", DbTypeClass.Decimal => "decimal",
        DbTypeClass.DateTime => "datetime", DbTypeClass.DateTimeOffset => "datetimeoffset", DbTypeClass.DateOnly => "dateonly",
        DbTypeClass.TimeOnly => "timeonly", DbTypeClass.Guid => "guid", DbTypeClass.ByteArray => "bytearray", _ => "unknown",
    };

    private static string ActionToken(int action) => action switch
    {
        1 => "restrict", 2 => "cascade", 3 => "set-null", 4 => "set-default", _ => "no-action",
    };

    private static string EscapeCSharpString(string value)
        => value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n");

    private static void ReportDatabaseGeneratedTokenDiagnostics(
        SourceProductionContext context,
        IReadOnlyList<EntityData> entities,
        SqlBuilder builder)
    {
        if (builder.SupportsDatabaseGeneratedConcurrencyToken)
        {
            return;
        }

        foreach (var entity in entities)
        {
            foreach (var column in entity.Columns.AsImmutableArray())
            {
                if (!column.IsDatabaseGeneratedToken)
                {
                    continue;
                }

                context.ReportDiagnostic(Diagnostic.Create(
                    InquiryDiagnosticDescriptors.DatabaseGeneratedConcurrencyTokenInvalid,
                    column.Location?.ToLocation(),
                    entity.Name,
                    column.PropertyName,
                    "Provider '" + builder.DialectName + "' does not support database-generated concurrency tokens; use an ORM-managed numeric token instead."));
            }
        }
    }

    /// <summary>
    /// Reports generation-time errors for key columns that would produce invalid DDL — a
    /// database-generated key that is not an integer (INQ030), and an unbounded string key on a dialect
    /// that cannot key on unbounded text (INQ031). Location-less (the cached model carries no symbol).
    /// </summary>
    private static void ReportKeyDiagnostics(SourceProductionContext context, IReadOnlyList<EntityData> entities, SqlBuilder builder, Dictionary<string, int> declaredLengths)
    {
        foreach (var entity in entities)
        {
            foreach (var key in entity.Keys.AsImmutableArray())
            {
                if (key.IsGenerated && !IsIntegerClass(key.TypeClass))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        InquiryDiagnosticDescriptors.GeneratedKeyNotInteger, location: null, entity.TableName, key.PropertyName));
                }
                else if (builder.RequiresBoundedStringKeys && builder.MapsToUnboundedString(key))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        InquiryDiagnosticDescriptors.StringKeyRequiresLength, location: null, entity.TableName, key.PropertyName, builder.DialectName));
                }
            }

            // INQ032: a bounded dialect cannot index an unbounded string (it maps to CLOB / NVARCHAR(MAX)
            // / LONGTEXT), so the generator skips that index (see SqlBuilder.BuildCreateIndexSql). Warn so
            // the dropped index is not silent — an explicit Length has it created. Non-key only: a string
            // key is the INQ031 error, and a foreign key inherits its referenced key's Length below.
            if (!builder.RequiresBoundedStringKeys)
            {
                continue;
            }

            foreach (var column in entity.Columns.AsImmutableArray())
            {
                if ((column.IsIndexed || column.IsUnique) && !column.IsKey && IsUnboundedAfterDerivation(column, declaredLengths, builder))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        InquiryDiagnosticDescriptors.IndexedStringRequiresLength, location: null, entity.TableName, column.PropertyName, builder.DialectName));
                }
            }
        }

        ReportSchemaLints(context, entities, builder);
    }

    /// <summary>
    /// Reports advisory DDL "lints" (Info severity, off by default — raise them in <c>.editorconfig</c>
    /// to enforce). INQ061: unindexed FK column. INQ062: decimal without explicit precision. INQ066:
    /// nullable column with a DEFAULT expression. INQ067: unbounded string column (no Length or SqlType).
    /// </summary>
    private static void ReportSchemaLints(SourceProductionContext context, IReadOnlyList<EntityData> entities, SqlBuilder builder)
    {
        // The FK lint is suppressed on a dialect that auto-indexes FK columns, but only when the
        // constraint is actually emitted (no constraint ⇒ no auto-index).
        var skipForeignKeyLint = builder.ForeignKeysAreAutoIndexed;

        foreach (var entity in entities)
        {
            var entityAutoIndexesForeignKeys = skipForeignKeyLint && entity.GenerateForeignKeys;

            foreach (var column in entity.Columns.AsImmutableArray())
            {
                // INQ061: a plain foreign-key column with no backing index. A key column is treated as
                // covered by the primary-key index (a v1 simplification — strictly only the PK's leading
                // column serves a single-column lookup, but a composite-key FK is an uncommon shape); an
                // explicitly indexed or unique column is already indexed.
                if (!entityAutoIndexesForeignKeys &&
                    column.ForeignKeyTable is not null && !column.IsKey && !column.IsIndexed && !column.IsUnique)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        InquiryDiagnosticDescriptors.UnindexedForeignKey, location: null, entity.TableName, column.PropertyName, builder.DialectName));
                }

                // INQ062: a decimal column with no explicit precision (and no SqlType override) takes the
                // dialect's default precision/scale, which can silently round (money columns especially).
                // The gate is Precision == 0 to match SqlBuilder.DecimalSpec, which uses the default whenever
                // Precision is unset — a Scale set without a Precision is itself silently ignored there.
                if (column.TypeClass == DbTypeClass.Decimal && column.Precision == 0 &&
                    string.IsNullOrEmpty(column.SqlType) && string.IsNullOrEmpty(column.ComputedExpression))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        InquiryDiagnosticDescriptors.DecimalWithoutPrecision, location: null, entity.TableName, column.PropertyName, builder.DialectName));
                }

                // INQ066: a nullable column with a DEFAULT expression — new rows always receive the
                // default, so NULL is unreachable via INSERT and the nullable + default pairing is
                // likely unintentional. Computed columns are excluded (their value is always derived).
                if (column.IsNullable && column.DefaultExpression is not null &&
                    !column.IsKey && string.IsNullOrEmpty(column.ComputedExpression))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        InquiryDiagnosticDescriptors.NullableColumnWithDefault, location: null, entity.TableName, column.PropertyName));
                }

                // INQ067: a string column with no Length and no SqlType override — it takes the
                // dialect's unbounded type (TEXT / NVARCHAR(MAX) / CLOB). Key columns are covered
                // by INQ031 and indexed/unique columns by INQ032, so those are excluded here.
                if (column.TypeClass == DbTypeClass.String && column.Length == 0 &&
                    string.IsNullOrEmpty(column.SqlType) && string.IsNullOrEmpty(column.ComputedExpression) &&
                    !column.IsKey && !column.IsIndexed && !column.IsUnique)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        InquiryDiagnosticDescriptors.UnboundedStringColumn, location: null, entity.TableName, column.PropertyName));
                }
            }
        }
    }

    private static bool IsIntegerClass(DbTypeClass typeClass)
        => typeClass is DbTypeClass.Byte or DbTypeClass.Int16 or DbTypeClass.Int32 or DbTypeClass.Int64;

    private static IReadOnlyList<EntityData> SelectPhysicalTableRepresentatives(
        SourceProductionContext context, IReadOnlyList<EntityData> entities)
    {
        var selected = new List<EntityData>();
        foreach (var group in entities.GroupBy(static entity => TableKey(entity.Schema, entity.TableName), System.StringComparer.Ordinal)
                     .OrderBy(static group => group.Key, System.StringComparer.Ordinal))
        {
            var ordered = group.OrderBy(static entity => entity.FullyQualifiedName, System.StringComparer.Ordinal).ToArray();
            var owner = ordered[0];
            selected.Add(owner);
            var ownerShape = PhysicalSchemaShape(owner);
            foreach (var suppressed in ordered.Skip(1))
            {
                if (System.StringComparer.Ordinal.Equals(ownerShape, PhysicalSchemaShape(suppressed))) continue;
                var mismatch = DescribePhysicalSchemaMismatch(owner, suppressed);
                var reason = $"it conflicts with canonical mapping '{owner.FullyQualifiedName}' for the same physical table: {mismatch}";
                context.ReportDiagnostic(Diagnostic.Create(InquiryDiagnosticDescriptors.DuplicateSchemaMapping,
                    suppressed.Location?.ToLocation(), suppressed.FullyQualifiedName, reason));
            }
        }
        return selected;
    }

    private static string DescribePhysicalSchemaMismatch(EntityData owner, EntityData suppressed)
    {
        if (owner.GenerateForeignKeys != suppressed.GenerateForeignKeys) return "GenerateForeignKeys differs";
        var ownerColumns = PhysicalColumnShape(owner);
        var suppressedColumns = PhysicalColumnShape(suppressed);
        if (!System.StringComparer.Ordinal.Equals(ownerColumns, suppressedColumns)) return "column, key, default, computed, generation, or foreign-key definitions differ";
        if (!System.StringComparer.Ordinal.Equals(PhysicalIndexShape(owner), PhysicalIndexShape(suppressed))) return "index definitions differ";
        return "check constraint definitions differ";
    }

    private static string PhysicalSchemaShape(EntityData entity)
        => PhysicalColumnShape(entity) + PhysicalIndexShape(entity) + PhysicalCheckShape(entity);

    private static string PhysicalColumnShape(EntityData entity)
    {
        var shape = new StringBuilder().Append(entity.GenerateForeignKeys).Append('|');
        shape.Append("K:").Append(string.Join("\0", entity.Keys.AsImmutableArray().Select(static key => key.ColumnName))).Append('|');
        foreach (var column in entity.Columns.AsImmutableArray().OrderBy(static column => column.ColumnName, System.StringComparer.Ordinal))
            shape.Append(column.ColumnName).Append('\0').Append(column.IsKey).Append('\0').Append(column.IsGenerated).Append('\0').Append(column.UseDatabaseDefault).Append('\0')
                .Append((int)column.TypeClass).Append('\0').Append(column.IsNullable).Append('\0').Append(column.SqlType).Append('\0')
                .Append(column.Length).Append('\0').Append(column.Precision).Append('\0').Append(column.Scale).Append('\0')
                .Append(column.DefaultExpression).Append('\0').Append(column.ComputedExpression).Append('\0')
                .Append(column.IsConcurrencyToken).Append('\0').Append(column.IsDatabaseGeneratedToken).Append('\0')
                .Append(column.EnumAsString).Append('\0').Append(column.IsUnicode).Append('\0')
                .Append(column.IsIndexed).Append('\0').Append(column.IsUnique).Append('\0').Append(column.IndexName).Append('\0')
                .Append(column.ForeignKeySchema).Append('\0').Append(column.ForeignKeyTable).Append('\0').Append(column.ForeignKeyColumn).Append('\0')
                .Append(column.ForeignKeyConstraintName).Append('\0').Append(column.ForeignKeyOnDelete).Append('\0').Append(column.ForeignKeyOnUpdate).Append('|');
        return shape.ToString();
    }

    private static string PhysicalIndexShape(EntityData entity)
    {
        var columns = entity.Columns.AsImmutableArray().ToDictionary(static c => c.PropertyName, static c => c.ColumnName, System.StringComparer.Ordinal);
        return string.Join("|", entity.Indexes.AsImmutableArray().Select(index =>
            string.Join("\0", index.LogicalKeyProperties.AsImmutableArray().Select(property => columns.TryGetValue(property, out var column) ? column : property))
            + ";" + string.Join("\0", index.LogicalIncludeProperties.AsImmutableArray().Select(property => columns.TryGetValue(property, out var column) ? column : property))
            + ";" + index.IsUnique + ";" + index.RequestedName).OrderBy(static value => value, System.StringComparer.Ordinal));
    }

    private static string PhysicalCheckShape(EntityData entity)
        => string.Join("|", entity.Checks.AsImmutableArray().Select(static check => check.Expression + "\0" + check.RequestedName)
            .OrderBy(static value => value, System.StringComparer.Ordinal));

    /// <summary>
    /// Kahn's topological sort keyed by the exact ordinal schema/table identity used by the FK graph.
    /// Edges point from a referencing table to its referenced table; external targets, self references,
    /// and SCC-internal cyclic edges do not constrain this phase. Exact table identity is the deterministic
    /// tie-breaker, and the defensive final pass uses the same ordering.
    /// </summary>
    private static IReadOnlyList<EntityData> OrderByForeignKeyDependencies(
        IReadOnlyList<EntityData> entities,
        HashSet<string> cyclicIdentities)
    {
        var byTable = new Dictionary<string, EntityData>(System.StringComparer.Ordinal);
        foreach (var entity in entities)
        {
            byTable[TableKey(entity.Schema, entity.TableName)] = entity;
        }

        // Dependencies: tables this entity references via a foreign key (within this assembly, excluding self).
        var dependencies = new Dictionary<EntityData, HashSet<string>>();
        foreach (var entity in entities)
        {
            var deps = new HashSet<string>(System.StringComparer.Ordinal);
            var selfKey = TableKey(entity.Schema, entity.TableName);
            foreach (var column in entity.Columns.AsImmutableArray())
            {
                if (!entity.GenerateForeignKeys || string.IsNullOrEmpty(column.ForeignKeyTable)
                    || string.IsNullOrEmpty(column.ForeignKeyColumn))
                {
                    continue;
                }

                var referencedKey = TableKey(column.ForeignKeySchema, column.ForeignKeyTable!);
                var identity = CanonicalForeignKeyIdentity(
                    entity.Schema, entity.TableName, column.ColumnName,
                    column.ForeignKeySchema, column.ForeignKeyTable!, column.ForeignKeyColumn!);
                if (!string.Equals(referencedKey, selfKey, System.StringComparison.Ordinal)
                    && byTable.ContainsKey(referencedKey)
                    && !cyclicIdentities.Contains(identity))
                {
                    deps.Add(referencedKey);
                }
            }

            dependencies[entity] = deps;
        }

        var ordered = new List<EntityData>(entities.Count);
        var emittedEntities = new HashSet<EntityData>();
        var emittedTables = new HashSet<string>(System.StringComparer.Ordinal);

        // Repeatedly emit every entity whose dependencies are all already emitted, using the physical
        // table identity as the stable tie-breaker. Stop when a pass adds nothing.
        bool progress = true;
        while (progress && ordered.Count < entities.Count)
        {
            progress = false;
            foreach (var entity in entities.OrderBy(static value => TableKey(value.Schema, value.TableName), System.StringComparer.Ordinal))
            {
                var entityKey = TableKey(entity.Schema, entity.TableName);
                if (emittedEntities.Contains(entity))
                {
                    continue;
                }

                if (dependencies[entity].All(emittedTables.Contains))
                {
                    ordered.Add(entity);
                    emittedEntities.Add(entity);
                    emittedTables.Add(entityKey);
                    progress = true;
                }
            }
        }

        // Defensive total ordering if malformed duplicate identities leave anything behind.
        foreach (var entity in entities.OrderBy(static value => TableKey(value.Schema, value.TableName), System.StringComparer.Ordinal))
        {
            var entityKey = TableKey(entity.Schema, entity.TableName);
            if (!emittedEntities.Contains(entity))
            {
                ordered.Add(entity);
                emittedEntities.Add(entity);
                emittedTables.Add(entityKey);
            }
        }

        return ordered;
    }

    private static ForeignKeyGraph AnalyzeForeignKeys(SourceProductionContext context, IReadOnlyList<EntityData> entities, SqlBuilder builder)
    {
        var invalidMappings = new List<InvalidSchemaMapping>();
        var validEntities = entities.ToArray();
        var tableKeys = new HashSet<string>(validEntities.Select(e => TableKey(e.Schema, e.TableName)), System.StringComparer.Ordinal);
        var physicalColumns = validEntities.ToDictionary(
            static entity => TableKey(entity.Schema, entity.TableName),
            static entity => new HashSet<string>(entity.Columns.AsImmutableArray().Select(static column => column.ColumnName), System.StringComparer.Ordinal),
            System.StringComparer.Ordinal);
        var foreignKeys = new List<ForeignKeyConstraintData>();
        var adjacency = new Dictionary<string, HashSet<string>>(System.StringComparer.Ordinal);
        foreach (var key in tableKeys)
        {
            adjacency[key] = new HashSet<string>(System.StringComparer.Ordinal);
        }

        foreach (var entity in validEntities.OrderBy(static value => TableKey(value.Schema, value.TableName), System.StringComparer.Ordinal))
        {
            if (!entity.GenerateForeignKeys)
            {
                continue;
            }

            var localKey = TableKey(entity.Schema, entity.TableName);
            foreach (var column in entity.Columns.AsImmutableArray())
            {
                if (string.IsNullOrEmpty(column.ForeignKeyTable) || string.IsNullOrEmpty(column.ForeignKeyColumn))
                {
                    continue;
                }

                var canonical = CanonicalForeignKeyIdentity(
                    entity.Schema, entity.TableName, column.ColumnName,
                    column.ForeignKeySchema, column.ForeignKeyTable!, column.ForeignKeyColumn!);
                var actionError = column.ForeignKeyConstraintName is not null && !IsValidExplicitIdentifier(column.ForeignKeyConstraintName)
                    ? "explicit foreign-key constraint name is empty, contains control characters, or exceeds 63 UTF-8 bytes"
                    : !builder.SupportsReferentialAction((ReferentialActionKind)column.ForeignKeyOnDelete, ReferentialActionEvent.Delete)
                    ? "ON DELETE action is unsupported"
                    : !builder.SupportsReferentialAction((ReferentialActionKind)column.ForeignKeyOnUpdate, ReferentialActionEvent.Update)
                        ? "ON UPDATE action is unsupported"
                        : (column.ForeignKeyOnDelete == 3 || column.ForeignKeyOnUpdate == 3) && !column.IsNullable
                            ? "SET NULL requires a nullable local property"
                            : (column.ForeignKeyOnDelete == 4 || column.ForeignKeyOnUpdate == 4) && string.IsNullOrEmpty(column.DefaultExpression)
                                ? "SET DEFAULT requires DefaultExpression"
                                : null;
                if (actionError is not null)
                {
                    context.ReportDiagnostic(Diagnostic.Create(InquiryDiagnosticDescriptors.SchemaPrimitiveInvalid,
                        column.Location?.ToLocation(), entity.TableName + "." + column.ColumnName, builder.DialectName, actionError));
                    continue;
                }

                var referencedKey = TableKey(column.ForeignKeySchema, column.ForeignKeyTable!);
                if (physicalColumns.TryGetValue(referencedKey, out var referencedColumns)
                    && !referencedColumns.Contains(column.ForeignKeyColumn!))
                {
                    invalidMappings.Add(new InvalidSchemaMapping(column.Location,
                        entity.TableName + "." + column.ColumnName + " -> " + column.ForeignKeyTable + "." + column.ForeignKeyColumn,
                        "the referenced column is absent from the canonical physical-table mapping"));
                    continue;
                }

                var generatedName = string.IsNullOrEmpty(column.ForeignKeyConstraintName)
                    ? BuildForeignKeyName(entity.TableName, column.ColumnName, canonical, 8)
                    : column.ForeignKeyConstraintName!;
                foreignKeys.Add(new ForeignKeyConstraintData(
                    entity.Schema, entity.TableName, column.ColumnName,
                    column.ForeignKeySchema, column.ForeignKeyTable!, column.ForeignKeyColumn!,
                    column.Location, canonical, generatedName, column.ForeignKeyConstraintName,
                    column.ForeignKeyOnDelete, column.ForeignKeyOnUpdate)
                {
                    LocalProperty = column.PropertyName,
                    GeneratedNameCandidate = BuildForeignKeyName(entity.TableName, column.ColumnName, canonical, 8),
                });

                if (!string.Equals(localKey, referencedKey, System.StringComparison.Ordinal)
                    && tableKeys.Contains(referencedKey))
                {
                    adjacency[localKey].Add(referencedKey);
                }
            }
        }

        var componentByTable = FindStronglyConnectedComponents(adjacency, out var componentSizes);
        bool IsCyclic(ForeignKeyConstraintData foreignKey)
        {
            var localKey = TableKey(foreignKey.LocalSchema, foreignKey.LocalTable);
            var referencedKey = TableKey(foreignKey.ReferencedSchema, foreignKey.ReferencedTable);
            return componentByTable.TryGetValue(localKey, out var localComponent)
                && componentByTable.TryGetValue(referencedKey, out var referencedComponent)
                && localComponent == referencedComponent
                && componentSizes[localComponent] > 1;
        }

        var normalized = new List<ForeignKeyConstraintData>();
        var invalidForeignKeys = new List<ForeignKeyConstraintData>();
        var invalidComponents = new HashSet<int>();
        foreach (var group in foreignKeys.GroupBy(static fk => fk.CanonicalIdentity, System.StringComparer.Ordinal)
                     .OrderBy(static group => group.Key, System.StringComparer.Ordinal))
        {
            if (group.Count() > 1 && IsCyclic(group.First()))
            {
                foreach (var duplicate in group)
                {
                    invalidForeignKeys.Add(duplicate);
                    invalidMappings.Add(new InvalidSchemaMapping(
                        duplicate.Location,
                        DescribeForeignKey(duplicate),
                        "multiple properties declare the same physical foreign key"));
                }
                invalidComponents.Add(componentByTable[TableKey(group.First().LocalSchema, group.First().LocalTable)]);
                continue;
            }

            normalized.AddRange(group);
        }

        var names = new Dictionary<string, string>(NameComparer(builder.ForeignKeyConstraintNameComparison));
        var named = new List<ForeignKeyConstraintData>(normalized.Count);
        foreach (var foreignKey in normalized)
        {
            if (IsCyclic(foreignKey)
                && invalidComponents.Contains(componentByTable[TableKey(foreignKey.LocalSchema, foreignKey.LocalTable)]))
            {
                invalidForeignKeys.Add(foreignKey);
                continue;
            }

            if (!string.IsNullOrEmpty(foreignKey.RequestedName))
            {
                var explicitKey = ConstraintNameCollisionKey(builder, foreignKey, foreignKey.RequestedName!);
                if (names.TryGetValue(explicitKey, out var existingExplicit) && existingExplicit != foreignKey.CanonicalIdentity)
                {
                    invalidMappings.Add(new InvalidSchemaMapping(foreignKey.Location, DescribeForeignKey(foreignKey), "its explicit constraint name is duplicated"));
                    invalidForeignKeys.Add(foreignKey);
                    continue;
                }
                names[explicitKey] = foreignKey.CanonicalIdentity;
                named.Add(foreignKey);
                continue;
            }

            var hashBytes = 8;
            string name;
            while (true)
            {
                name = BuildForeignKeyName(foreignKey.LocalTable, foreignKey.LocalColumn, foreignKey.CanonicalIdentity, hashBytes);
                var collisionKey = ConstraintNameCollisionKey(builder, foreignKey, name);
                if (!names.TryGetValue(collisionKey, out var existing) || existing == foreignKey.CanonicalIdentity)
                {
                    names[collisionKey] = foreignKey.CanonicalIdentity;
                    named.Add(foreignKey with { ConstraintName = name });
                    break;
                }

                hashBytes++;
                if (hashBytes > 31)
                {
                    invalidMappings.Add(new InvalidSchemaMapping(
                        foreignKey.Location,
                        DescribeForeignKey(foreignKey),
                        "its generated constraint name collides after the full SHA-256 suffix"));
                    break;
                }
            }
        }

        var cyclic = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (var invalid in invalidForeignKeys)
        {
            if (IsCyclic(invalid))
            {
                cyclic.Add(invalid.CanonicalIdentity);
            }
        }
        foreach (var foreignKey in named)
        {
            if (IsCyclic(foreignKey))
            {
                cyclic.Add(foreignKey.CanonicalIdentity);
            }
        }
        var emitted = named.Select(foreignKey => ApplyForeignKeyEmissionMetadata(
            foreignKey, cyclic.Contains(foreignKey.CanonicalIdentity), builder.CyclicForeignKeyStrategy)).ToList();

        return new ForeignKeyGraph(emitted, cyclic, invalidMappings, invalidForeignKeys);
    }

    internal static ForeignKeyConstraintData ApplyForeignKeyEmissionMetadata(
        ForeignKeyConstraintData foreignKey,
        bool isCyclic,
        CyclicForeignKeyStrategy strategy)
    {
        var mode = isCyclic
            ? strategy == CyclicForeignKeyStrategy.AlterTable ? ForeignKeyEmissionMode.Deferred
                : strategy == CyclicForeignKeyStrategy.Inline ? ForeignKeyEmissionMode.Inline
                : ForeignKeyEmissionMode.Suppressed
            : ForeignKeyEmissionMode.Inline;
        var emittedName = mode == ForeignKeyEmissionMode.Suppressed ? null
            : mode == ForeignKeyEmissionMode.Deferred ? foreignKey.RequestedName ?? foreignKey.ConstraintName
            : foreignKey.RequestedName;
        return foreignKey with { EmissionMode = mode, EmittedName = emittedName };
    }

    private static Dictionary<string, int> FindStronglyConnectedComponents(
        Dictionary<string, HashSet<string>> adjacency,
        out List<int> componentSizes)
    {
        var index = 0;
        var indices = new Dictionary<string, int>(System.StringComparer.Ordinal);
        var lowLinks = new Dictionary<string, int>(System.StringComparer.Ordinal);
        var stack = new Stack<string>();
        var onStack = new HashSet<string>(System.StringComparer.Ordinal);
        var components = new Dictionary<string, int>(System.StringComparer.Ordinal);
        var sizes = new List<int>();

        void Visit(string vertex)
        {
            indices[vertex] = index;
            lowLinks[vertex] = index++;
            stack.Push(vertex);
            onStack.Add(vertex);

            foreach (var target in adjacency[vertex].OrderBy(static value => value, System.StringComparer.Ordinal))
            {
                if (!indices.ContainsKey(target))
                {
                    Visit(target);
                    lowLinks[vertex] = System.Math.Min(lowLinks[vertex], lowLinks[target]);
                }
                else if (onStack.Contains(target))
                {
                    lowLinks[vertex] = System.Math.Min(lowLinks[vertex], indices[target]);
                }
            }

            if (lowLinks[vertex] != indices[vertex])
            {
                return;
            }

            var component = sizes.Count;
            var size = 0;
            string member;
            do
            {
                member = stack.Pop();
                onStack.Remove(member);
                components[member] = component;
                size++;
            }
            while (!string.Equals(member, vertex, System.StringComparison.Ordinal));
            sizes.Add(size);
        }

        foreach (var vertex in adjacency.Keys.OrderBy(static value => value, System.StringComparer.Ordinal))
        {
            if (!indices.ContainsKey(vertex))
            {
                Visit(vertex);
            }
        }

        componentSizes = sizes;
        return components;
    }

    private static string CanonicalForeignKeyIdentity(
        string? localSchema, string localTable, string localColumn,
        string? referencedSchema, string referencedTable, string referencedColumn)
        => CanonicalPart(localSchema) + CanonicalPart(localTable) + CanonicalPart(localColumn)
            + CanonicalPart(referencedSchema) + CanonicalPart(referencedTable) + CanonicalPart(referencedColumn);

    private static string CanonicalPart(string? value)
    {
        value ??= string.Empty;
        return value.Length + ":" + value;
    }

    private static string DescribeForeignKey(ForeignKeyConstraintData foreignKey)
        => Qualify(foreignKey.LocalSchema, foreignKey.LocalTable) + "." + foreignKey.LocalColumn
            + " -> " + Qualify(foreignKey.ReferencedSchema, foreignKey.ReferencedTable) + "." + foreignKey.ReferencedColumn;

    private static string Qualify(string? schema, string table)
        => string.IsNullOrEmpty(schema) ? table : schema + "." + table;

    private static string ConstraintNameCollisionKey(SqlBuilder builder, ForeignKeyConstraintData foreignKey, string name)
        => builder.ForeignKeyConstraintNameScope == ConstraintNameScope.Table
            ? TableKey(foreignKey.LocalSchema, foreignKey.LocalTable) + "\0" + name
            : (foreignKey.LocalSchema ?? string.Empty) + "\0" + name;

    private static bool IsValidExplicitIdentifier(string value)
        => value.Length > 0 && Encoding.UTF8.GetByteCount(value) <= 63 && !value.Any(char.IsControl);

    private static System.StringComparer NameComparer(IdentifierComparison comparison)
        => comparison == IdentifierComparison.OrdinalIgnoreCase
            ? System.StringComparer.OrdinalIgnoreCase
            : System.StringComparer.Ordinal;

    private static void ValidateCrossEntityObjectNames(
        SourceProductionContext context,
        IReadOnlyList<EntityData> entities,
        SqlBuilder builder,
        Dictionary<string, IReadOnlyList<IndexData>> indexesByTable,
        Dictionary<string, IReadOnlyList<CheckConstraintData>> checksByTable)
    {
        var indexNames = new HashSet<string>(NameComparer(builder.IndexNameComparison));
        var checkNames = new HashSet<string>(NameComparer(builder.CheckConstraintNameComparison));
        foreach (var entity in entities
                     .OrderBy(static value => TableKey(value.Schema, value.TableName), System.StringComparer.Ordinal)
                     .ThenBy(static value => value.FullyQualifiedName, System.StringComparer.Ordinal))
        {
            var indexes = new List<IndexData>();
            foreach (var index in indexesByTable[entity.FullyQualifiedName])
            {
                var name = index.EmittedName!;
                var scopeKey = ObjectNameScopeKey(builder.IndexNameScope, entity.Schema, entity.TableName, name);
                if (!IsValidExplicitIdentifier(name) || !indexNames.Add(scopeKey))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        InquiryDiagnosticDescriptors.SchemaPrimitiveInvalid,
                        index.Location?.ToLocation(),
                        entity.TableName,
                        builder.DialectName,
                        !IsValidExplicitIdentifier(name) ? "emitted index name is invalid or exceeds 63 UTF-8 bytes" : "duplicate index name in provider scope"));
                    continue;
                }
                indexes.Add(index);
            }
            indexesByTable[entity.FullyQualifiedName] = indexes;

            var checks = new List<CheckConstraintData>();
            foreach (var check in checksByTable[entity.FullyQualifiedName])
            {
                var name = check.EmittedName!;
                var scopeKey = ObjectNameScopeKey(builder.CheckConstraintNameScope, entity.Schema, entity.TableName, name);
                if (!IsValidExplicitIdentifier(name) || !checkNames.Add(scopeKey))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        InquiryDiagnosticDescriptors.SchemaPrimitiveInvalid,
                        check.Location?.ToLocation(),
                        entity.TableName,
                        builder.DialectName,
                        !IsValidExplicitIdentifier(name) ? "emitted check name is invalid or exceeds 63 UTF-8 bytes" : "duplicate check constraint name in provider scope"));
                    continue;
                }
                checks.Add(check);
            }
            checksByTable[entity.FullyQualifiedName] = checks;
        }
    }

    private static string ObjectNameScopeKey(ConstraintNameScope scope, string? schema, string table, string name)
        => scope == ConstraintNameScope.Table
            ? TableKey(schema, table) + "\0" + name
            : (schema ?? string.Empty) + "\0" + name;

    private static IReadOnlyList<IndexData> NormalizeIndexes(SourceProductionContext context, EntityData entity, SqlBuilder builder, Dictionary<string, int> declaredLengths)
    {
        var result = new List<IndexData>();
        var legacyOrdinal = 0;
        foreach (var column in entity.Columns.AsImmutableArray())
        {
            if (!column.IsIndexed && !column.IsUnique) continue;
            if (builder.RequiresBoundedStringKeys && builder.MapsToUnboundedString(DeriveForeignKeyLength(column, declaredLengths))) continue;
            var legacyName = string.IsNullOrEmpty(column.IndexName)
                ? (column.IsUnique ? "UX_" : "IX_") + entity.TableName + "_" + column.ColumnName
                : column.IndexName!;
            result.Add(new IndexData(entity.Schema, entity.TableName,
                new EquatableArray<string>(ImmutableArray.Create(column.ColumnName)), default,
                column.IsUnique, column.IndexName, column.Location)
            {
                LogicalKeyProperties = new EquatableArray<string>(ImmutableArray.Create(column.PropertyName)),
                EmittedName = legacyName,
                Origin = IndexOrigin.ColumnFlag,
                Ordinal = legacyOrdinal++,
                CanonicalIdentity = CanonicalIndexIdentity(entity.Schema, entity.TableName, ImmutableArray.Create(column.ColumnName), ImmutableArray<string>.Empty, column.IsUnique),
            });
        }

        foreach (var declared in entity.Indexes.AsImmutableArray())
        {
            var keys = declared.KeyColumns.AsImmutableArray();
            var includes = declared.IncludeColumns.AsImmutableArray();
            var mappedColumns = entity.Columns.AsImmutableArray();
            var error = keys.Length == 0 || keys.Any(string.IsNullOrEmpty) ? "index keys must name mapped properties"
                : keys.Distinct(System.StringComparer.Ordinal).Count() != keys.Length ? "index keys contain duplicates"
                : includes.Any(string.IsNullOrEmpty) ? "included columns must name mapped properties"
                : includes.Distinct(System.StringComparer.Ordinal).Count() != includes.Length ? "included columns contain duplicates"
                : includes.Any(keys.Contains) ? "a column cannot be both a key and included column"
                : includes.Length > 0 && !builder.SupportsIndexIncludeColumns ? "covering INCLUDE columns are unsupported"
                : declared.RequestedName is not null && !IsValidExplicitIdentifier(declared.RequestedName) ? "explicit index name is empty, contains control characters, or exceeds 63 UTF-8 bytes"
                : builder.RequiresBoundedStringKeys && keys.Any(key => mappedColumns.Any(c => c.ColumnName == key && builder.MapsToUnboundedString(DeriveForeignKeyLength(c, declaredLengths)))) ? "index key maps to an unbounded type"
                : null;
            if (error is not null)
            {
                context.ReportDiagnostic(Diagnostic.Create(InquiryDiagnosticDescriptors.SchemaPrimitiveInvalid,
                    declared.Location?.ToLocation(), entity.TableName, builder.DialectName, error));
                continue;
            }
            var canonical = CanonicalIndexIdentity(entity.Schema, entity.TableName, keys, includes, declared.IsUnique);
            var name = string.IsNullOrEmpty(declared.RequestedName)
                ? (declared.IsUnique ? "UX" : "IX") + BuildForeignKeyName(entity.TableName, keys[0], canonical, 8).Substring(2)
                : declared.RequestedName!;
            result.Add(declared with { Schema = entity.Schema, Table = entity.TableName, EmittedName = name, CanonicalIdentity = canonical });
        }
        var valid = new List<IndexData>();
        var identities = new HashSet<string>(System.StringComparer.Ordinal);
        var names = new HashSet<string>(NameComparer(builder.IndexNameComparison));
        var ordered = result.Where(static i => i.Origin == IndexOrigin.ColumnFlag).OrderBy(static i => i.Ordinal)
            .Concat(result.Where(static i => i.Origin == IndexOrigin.TableAttribute).OrderBy(static i => i.CanonicalIdentity, System.StringComparer.Ordinal));
        foreach (var index in ordered)
        {
            var identity = index.CanonicalIdentity;
            if (!identities.Add(identity))
            {
                context.ReportDiagnostic(Diagnostic.Create(InquiryDiagnosticDescriptors.SchemaPrimitiveInvalid,
                    index.Location?.ToLocation(), entity.TableName, builder.DialectName, "duplicate index declaration or physical name"));
                continue;
            }
            var candidate = index;
            if (!names.Add(candidate.EmittedName!))
            {
                if (candidate.Origin == IndexOrigin.ColumnFlag || candidate.RequestedName is not null)
                {
                    context.ReportDiagnostic(Diagnostic.Create(InquiryDiagnosticDescriptors.SchemaPrimitiveInvalid,
                        index.Location?.ToLocation(), entity.TableName, builder.DialectName, "duplicate physical index name"));
                    continue;
                }
                var resolved = false;
                for (var hashBytes = 9; hashBytes <= 31; hashBytes++)
                {
                    var name = (candidate.IsUnique ? "UX" : "IX") + BuildForeignKeyName(entity.TableName, candidate.KeyColumns[0], identity, hashBytes).Substring(2);
                    if (names.Add(name)) { candidate = candidate with { EmittedName = name }; resolved = true; break; }
                }
                if (!resolved) { context.ReportDiagnostic(Diagnostic.Create(InquiryDiagnosticDescriptors.SchemaPrimitiveInvalid, index.Location?.ToLocation(), entity.TableName, builder.DialectName, "generated index name collision")); continue; }
            }
            valid.Add(candidate);
        }
        return valid;
    }

    private static string CanonicalIndexIdentity(string? schema, string table, IEnumerable<string> keys, IEnumerable<string> includes, bool unique)
        => CanonicalPart(schema) + CanonicalPart(table) + "K" + string.Concat(keys.Select(CanonicalPart))
            + "I" + string.Concat(includes.Select(CanonicalPart)) + (unique ? "U" : "N");

    private static IReadOnlyList<CheckConstraintData> NormalizeChecks(SourceProductionContext context, EntityData entity, SqlBuilder builder)
    {
        var result = new List<CheckConstraintData>();
        foreach (var declared in entity.Checks.AsImmutableArray())
        {
            if (!builder.SupportsCheckConstraints)
            {
                context.ReportDiagnostic(Diagnostic.Create(InquiryDiagnosticDescriptors.SchemaPrimitiveInvalid,
                    declared.Location?.ToLocation(), entity.TableName, builder.DialectName, "check constraints are unsupported"));
                continue;
            }
            if (string.IsNullOrWhiteSpace(declared.Expression)
                || declared.RequestedName is not null && !IsValidExplicitIdentifier(declared.RequestedName))
            {
                context.ReportDiagnostic(Diagnostic.Create(InquiryDiagnosticDescriptors.SchemaPrimitiveInvalid,
                    declared.Location?.ToLocation(), entity.TableName, builder.DialectName, "check expression/name is empty or name exceeds 63 UTF-8 bytes"));
                continue;
            }
            var canonical = CanonicalPart(entity.Schema) + CanonicalPart(entity.TableName) + CanonicalPart(declared.Expression);
            var name = string.IsNullOrEmpty(declared.RequestedName)
                ? "CK" + BuildForeignKeyName(entity.TableName, "Check", canonical, 8).Substring(2)
                : declared.RequestedName!;
            result.Add(declared with { Schema = entity.Schema, Table = entity.TableName, EmittedName = name, CanonicalIdentity = canonical });
        }
        var valid = new List<CheckConstraintData>();
        var expressions = new HashSet<string>(System.StringComparer.Ordinal);
        var names = new HashSet<string>(NameComparer(builder.CheckConstraintNameComparison));
        foreach (var check in result.OrderBy(static c => c.Expression, System.StringComparer.Ordinal))
        {
            if (!expressions.Add(check.Expression))
            {
                context.ReportDiagnostic(Diagnostic.Create(InquiryDiagnosticDescriptors.SchemaPrimitiveInvalid,
                    check.Location?.ToLocation(), entity.TableName, builder.DialectName, "duplicate check expression or physical name"));
                continue;
            }
            var candidate = check;
            if (!names.Add(candidate.EmittedName!))
            {
                if (candidate.RequestedName is not null)
                {
                    context.ReportDiagnostic(Diagnostic.Create(InquiryDiagnosticDescriptors.SchemaPrimitiveInvalid, check.Location?.ToLocation(), entity.TableName, builder.DialectName, "duplicate physical check name"));
                    continue;
                }
                var resolved = false;
                for (var hashBytes = 9; hashBytes <= 31; hashBytes++)
                {
                    var name = "CK" + BuildForeignKeyName(entity.TableName, "Check", candidate.CanonicalIdentity, hashBytes).Substring(2);
                    if (names.Add(name)) { candidate = candidate with { EmittedName = name }; resolved = true; break; }
                }
                if (!resolved) { context.ReportDiagnostic(Diagnostic.Create(InquiryDiagnosticDescriptors.SchemaPrimitiveInvalid, check.Location?.ToLocation(), entity.TableName, builder.DialectName, "generated check name collision")); continue; }
            }
            valid.Add(candidate);
        }
        return valid;
    }

    internal static string BuildForeignKeyName(string table, string column, string canonicalIdentity, int hashBytes)
    {
        if (hashBytes < 1 || hashBytes > 31)
        {
            throw new System.ArgumentOutOfRangeException(nameof(hashBytes));
        }
        byte[] hash;
        using (var sha = SHA256.Create())
        {
            hash = sha.ComputeHash(Encoding.UTF8.GetBytes(canonicalIdentity));
        }

        var suffix = new StringBuilder(hashBytes * 2);
        for (var i = 0; i < hashBytes; i++)
        {
            suffix.Append(hash[i].ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
        }

        var readable = "FK_" + table + "_" + column;
        var suffixText = "_" + suffix;
        var budget = 63 - Encoding.UTF8.GetByteCount(suffixText);
        return TruncateUtf8(readable, budget) + suffixText;
    }

    private static string TruncateUtf8(string value, int byteBudget)
    {
        var length = 0;
        var bytes = 0;
        while (length < value.Length)
        {
            var chars = char.IsHighSurrogate(value[length]) && length + 1 < value.Length && char.IsLowSurrogate(value[length + 1]) ? 2 : 1;
            var count = Encoding.UTF8.GetByteCount(value.Substring(length, chars));
            if (bytes + count > byteBudget)
            {
                break;
            }

            bytes += count;
            length += chars;
        }

        return value.Substring(0, length);
    }

    private sealed record ForeignKeyGraph(
        List<ForeignKeyConstraintData> ForeignKeys,
        HashSet<string> CyclicIdentities,
        List<InvalidSchemaMapping> InvalidMappings,
        List<ForeignKeyConstraintData> InvalidForeignKeys);

    private sealed record InvalidSchemaMapping(LocationData? Location, string Identity, string Reason);

    /// <summary>Indexes every declared (non-zero) column Length by (schema, table, column) for FK derivation.</summary>
    private static Dictionary<string, int> BuildColumnLengthIndex(IReadOnlyList<EntityData> entities)
    {
        var map = new Dictionary<string, int>(System.StringComparer.Ordinal);
        foreach (var entity in entities)
        {
            foreach (var column in entity.Columns.AsImmutableArray())
            {
                if (column.Length > 0)
                {
                    map[ColumnKey(entity.Schema, entity.TableName, column.ColumnName)] = column.Length;
                }
            }
        }

        return map;
    }

    // '\0' cannot appear in a SQL identifier, so it is a collision-proof composite-key separator
    // (identifiers may contain spaces, e.g. "Order Details").
    private static string TableKey(string? schema, string table) => (schema ?? string.Empty) + "\0" + table;

    private static string ColumnKey(string? schema, string table, string column) => TableKey(schema, table) + "\0" + column;

    /// <summary>
    /// A non-key string foreign-key column with no declared <see cref="ColumnData.Length"/> (and no
    /// <see cref="ColumnData.SqlType"/>) inherits its referenced column's declared Length; everything else
    /// is returned unchanged. Keys are exempt — an unbounded string key is an explicit-Length error
    /// (INQ031), not silently derived.
    /// </summary>
    private static ColumnData DeriveForeignKeyLength(ColumnData column, Dictionary<string, int> declaredLengths)
    {
        if (column.Length == 0
            && !column.IsKey
            && column.TypeClass == DbTypeClass.String
            && string.IsNullOrEmpty(column.SqlType)
            && !string.IsNullOrEmpty(column.ForeignKeyTable)
            && !string.IsNullOrEmpty(column.ForeignKeyColumn)
            && declaredLengths.TryGetValue(ColumnKey(column.ForeignKeySchema, column.ForeignKeyTable!, column.ForeignKeyColumn!), out var referencedLength))
        {
            return column with { Length = referencedLength };
        }

        return column;
    }

    private static List<IColumn> ResolveColumns(EntityData entity, Dictionary<string, int> declaredLengths, SqlBuilder builder)
    {
        var list = new List<IColumn>(entity.Columns.Count);
        foreach (var column in entity.Columns.AsImmutableArray())
        {
            var resolved = DeriveForeignKeyLength(column, declaredLengths);
            list.Add(resolved with { DefaultExpression = resolved.DefaultExpression is null ? null : builder.RenderDefaultExpression(resolved.DefaultExpression) });
        }

        return list;
    }

    /// <summary>True for a string column that is still unbounded (TEXT/LOB/MAX) after FK-length derivation.</summary>
    // A string column is unbounded for indexing if, after a foreign key inherits its referenced key's
    // declared Length, the effective Length is unset (0) or beyond the dialect's fixed-width ceiling — both
    // map to a LOB/MAX text type the dialect cannot index.
    private static bool IsUnboundedAfterDerivation(ColumnData column, Dictionary<string, int> declaredLengths, SqlBuilder builder)
    {
        if (column.TypeClass != DbTypeClass.String || !string.IsNullOrEmpty(column.SqlType))
        {
            return false;
        }

        var effectiveLength = DeriveForeignKeyLength(column, declaredLengths).Length;
        return effectiveLength == 0 || effectiveLength > builder.MaxBoundedStringLength(column.IsUnicode);
    }
}
