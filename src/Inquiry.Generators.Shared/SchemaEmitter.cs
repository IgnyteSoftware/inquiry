using Inquiry.Generators.Abstractions;
using Inquiry.Generators.Diagnostics;
using Inquiry.Generators.Infrastructure;
using Inquiry.Generators.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Generic;
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
        IReadOnlyList<CollectionParameterArtifact> providerArtifacts)
    {
        if (entities.Count == 0 && providerArtifacts.Count == 0)
        {
            return;
        }

        // A string foreign-key column with no declared Length inherits its referenced column's declared
        // Length, so on a bounded dialect it emits a valid bounded VARCHAR instead of an unindexable/
        // unkeyable LOB. Indexed by (schema, table, column) across every entity (the referenced table may be any).
        var declaredLengths = BuildColumnLengthIndex(entities);

        ReportDatabaseGeneratedTokenDiagnostics(context, entities, builder);

        ReportKeyDiagnostics(context, entities, builder, declaredLengths);

        var graph = AnalyzeForeignKeys(entities);
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

        var ddl = new StringBuilder();
        // Index statements are collected and appended after every CREATE TABLE so a referenced table
        // always exists before its index is created.
        var indexStatements = new List<string>();
        for (var i = 0; i < ordered.Count; i++)
        {
            if (i > 0)
            {
                ddl.Append("\n\n");
            }

            var entity = ordered[i];
            var columns = ResolveColumns(entity, declaredLengths);
            var ctx = new SqlBuildContext(
                builder,
                entity.Schema,
                entity.TableName,
                columns,
                suppressSoftDelete: false,
                generateForeignKeys: entity.GenerateForeignKeys)
            {
                SuppressedForeignKeyColumns = suppressedByTable.TryGetValue(TableKey(entity.Schema, entity.TableName), out var suppressed)
                    ? suppressed
                    : null,
            };
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
        source.AppendLine($"namespace {GeneratedNamespace};");
        source.AppendLine();
        source.AppendLine("/// <summary>Generated CREATE TABLE DDL for every Inquiry entity in this assembly.</summary>");
        // internal (not public): each assembly emits its own copy, so a referencing assembly that also
        // uses Inquiry does not collide on a single public Inquiry.Generated.InquiryGeneratedSchema type.
        source.AppendLine($"internal static class {GeneratedClassName}");
        source.AppendLine("{");
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
        var emitted = new HashSet<string>(System.StringComparer.Ordinal);

        // Repeatedly emit every entity whose dependencies are all already emitted, using the physical
        // table identity as the stable tie-breaker. Stop when a pass adds nothing.
        bool progress = true;
        while (progress && ordered.Count < entities.Count)
        {
            progress = false;
            foreach (var entity in entities.OrderBy(static value => TableKey(value.Schema, value.TableName), System.StringComparer.Ordinal))
            {
                var entityKey = TableKey(entity.Schema, entity.TableName);
                if (emitted.Contains(entityKey))
                {
                    continue;
                }

                if (dependencies[entity].All(emitted.Contains))
                {
                    ordered.Add(entity);
                    emitted.Add(entityKey);
                    progress = true;
                }
            }
        }

        // Defensive total ordering if malformed duplicate identities leave anything behind.
        foreach (var entity in entities.OrderBy(static value => TableKey(value.Schema, value.TableName), System.StringComparer.Ordinal))
        {
            var entityKey = TableKey(entity.Schema, entity.TableName);
            if (!emitted.Contains(entityKey))
            {
                ordered.Add(entity);
                emitted.Add(entityKey);
            }
        }

        return ordered;
    }

    private static ForeignKeyGraph AnalyzeForeignKeys(IReadOnlyList<EntityData> entities)
    {
        var invalidMappings = new List<InvalidSchemaMapping>();
        var validEntities = entities.ToArray();
        var tableKeys = new HashSet<string>(validEntities.Select(e => TableKey(e.Schema, e.TableName)), System.StringComparer.Ordinal);
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
                foreignKeys.Add(new ForeignKeyConstraintData(
                    entity.Schema, entity.TableName, column.ColumnName,
                    column.ForeignKeySchema, column.ForeignKeyTable!, column.ForeignKeyColumn!,
                    column.Location, canonical, string.Empty));

                var referencedKey = TableKey(column.ForeignKeySchema, column.ForeignKeyTable!);
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

        var names = new Dictionary<string, string>(System.StringComparer.Ordinal);
        var named = new List<ForeignKeyConstraintData>(normalized.Count);
        foreach (var foreignKey in normalized)
        {
            if (IsCyclic(foreignKey)
                && invalidComponents.Contains(componentByTable[TableKey(foreignKey.LocalSchema, foreignKey.LocalTable)]))
            {
                invalidForeignKeys.Add(foreignKey);
                continue;
            }

            var hashBytes = 8;
            string name;
            while (true)
            {
                name = BuildForeignKeyName(foreignKey.LocalTable, foreignKey.LocalColumn, foreignKey.CanonicalIdentity, hashBytes);
                if (!names.TryGetValue(name, out var existing) || existing == foreignKey.CanonicalIdentity)
                {
                    names[name] = foreignKey.CanonicalIdentity;
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

        return new ForeignKeyGraph(named, cyclic, invalidMappings, invalidForeignKeys);
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

    private static List<IColumn> ResolveColumns(EntityData entity, Dictionary<string, int> declaredLengths)
    {
        var list = new List<IColumn>(entity.Columns.Count);
        foreach (var column in entity.Columns.AsImmutableArray())
        {
            list.Add(DeriveForeignKeyLength(column, declaredLengths));
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
