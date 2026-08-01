using Inquiry.Generators.Abstractions;
using Inquiry.Generators.Diagnostics;
using Inquiry.Generators.Infrastructure;
using Inquiry.Generators.Models;
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Inquiry.Generators;

/// <summary>
/// Turns an auto-managed <c>[InquiryManyToMany]</c> — the parameterless form — into a real junction
/// <see cref="EntityData"/>, and rewrites the declaring relation to name it. Everything downstream then
/// sees exactly the shape an explicitly mapped junction produces: relation validation resolves it, the
/// SQL builders read its table and columns, and the schema emitter gives it <c>CREATE TABLE</c> with
/// foreign keys, topological ordering, and hashed constraint names for free.
/// </summary>
/// <remarks>
/// Naming is derived from an order-independent pair — the two mapped tables sorted ordinally by
/// <c>(Schema, TableName)</c> — rather than from "parent" and "child". Both sides of a bidirectional
/// association must synthesize the identical table, including primary-key column ORDER: the schema
/// emitter hashes key columns in order to detect colliding physical mappings, so a parent-first key
/// would make the two sides disagree and fire a spurious INQ070.
/// </remarks>
internal static class AutoJunctionSynthesizer
{
    /// <summary>Marks a synthesized entity's identity so it can never collide with a real type name.</summary>
    private const string IdentityPrefix = "<auto-junction>";

    /// <summary>
    /// Returns <paramref name="entities"/> with a synthesized junction appended per auto-managed
    /// association and every auto relation rewritten to reference it. Returns the input unchanged when
    /// no entity declares one.
    /// </summary>
    public static ImmutableArray<EntityData> Synthesize(
        SourceProductionContext context, ImmutableArray<EntityData> entities, SqlBuilder sqlBuilder)
    {
        if (!entities.Any(static e => e.IsMapped && e.Relations.AsImmutableArray().Any(static r => r.IsAutoJunction)))
        {
            return entities;
        }

        var byName = new Dictionary<string, EntityData>(StringComparer.Ordinal);
        foreach (var entity in entities)
        {
            if (entity.IsMapped)
            {
                byName[entity.FullyQualifiedName] = entity;
            }
        }

        // Physical tables a declared type already owns. A synthesized junction must never land on one:
        // that object can carry soft-delete or global-filter columns the auto read path knows nothing
        // about, so links would surface rows its own store hides. Views count — a view owns its name in
        // the database just as a table does — and so do entities that failed their own validation, whose
        // table is just as real as a valid one's.
        var claimedTables = new Dictionary<string, List<string?>>(StringComparer.OrdinalIgnoreCase);
        foreach (var entity in entities)
        {
            if (!claimedTables.TryGetValue(entity.TableName, out var schemas))
            {
                schemas = new List<string?>();
                claimedTables[entity.TableName] = schemas;
            }

            schemas.Add(entity.Schema);
        }

        // One entry per linked ENTITY PAIR, plus a table-name index. Both are needed: keying only by
        // table would silently merge two unrelated associations that resolve to the same name, and
        // keying only by pair would miss a second pair claiming a table the first already owns.
        //
        // The table index is case-INSENSITIVE, matching claimedTables. Two junctions whose names differ
        // only in case are one object on SQL Server, MySQL, and Oracle, and SchemaEmitter's own INQ070
        // grouping is ordinal, so an ordinal index here would let them both be emitted and collapse at
        // the server with no diagnostic from anywhere.
        var byPair = new Dictionary<string, JunctionShape>(StringComparer.Ordinal);
        var pairByIdentity = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var rewrites = new Dictionary<string, Dictionary<string, RelationData>>(StringComparer.Ordinal);

        foreach (var entity in entities)
        {
            if (!entity.IsMapped) continue;

            foreach (var relation in entity.Relations)
            {
                if (!relation.IsAutoJunction) continue;

                var shape = TryBuildShape(context, sqlBuilder, byName, claimedTables, entity, relation);
                if (shape is null) continue;

                if (Describe(byPair, pairByIdentity, shape) is { } disagreement)
                {
                    Report(context, entity, relation, disagreement);
                    continue;
                }

                // The shape that actually gets synthesized. For a reverse navigation this is the one
                // already recorded, NOT the candidate: identities are compared case-insensitively, so
                // the two sides can spell the table differently and only one entity is ever emitted.
                // Rewriting the relation with the candidate's spelling would name an entity that does
                // not exist, and the resulting lookup miss lands in a diagnostic path that never
                // expected a synthesized identity.
                JunctionShape effective;
                if (byPair.TryGetValue(shape.PairKey, out var owner))
                {
                    // The reverse navigation of an association already recorded. Remember that this side
                    // has now declared, so a THIRD navigation between the same two entities is rejected
                    // no matter which side it comes from — comparing against a single remembered
                    // declarer would accept every extra navigation from the other side.
                    owner.NoteDeclaredBy(entity.FullyQualifiedName);
                    effective = owner;
                }
                else
                {
                    shape.NoteDeclaredBy(entity.FullyQualifiedName);
                    byPair.Add(shape.PairKey, shape);
                    pairByIdentity.Add(shape.Identity, shape.PairKey);
                    effective = shape;
                }

                if (!rewrites.TryGetValue(entity.FullyQualifiedName, out var perEntity))
                {
                    perEntity = new Dictionary<string, RelationData>(StringComparer.Ordinal);
                    rewrites[entity.FullyQualifiedName] = perEntity;
                }

                perEntity[relation.PropertyName] = relation with
                {
                    JunctionEntityFullyQualifiedName = effective.Identity,
                    JunctionParentForeignKeyProperty = effective.ColumnFor(entity),
                    JunctionChildForeignKeyProperties = new EquatableArray<string>(
                        ImmutableArray.Create(effective.ColumnForOther(entity))),
                };
            }
        }

        if (byPair.Count == 0 && rewrites.Count == 0)
        {
            return entities;
        }

        var result = ImmutableArray.CreateBuilder<EntityData>(entities.Length + byPair.Count);
        foreach (var entity in entities)
        {
            if (rewrites.TryGetValue(entity.FullyQualifiedName, out var perEntity) && entity.IsMapped)
            {
                var updated = entity.Relations.AsImmutableArray()
                    .Select(relation => perEntity.TryGetValue(relation.PropertyName, out var rewritten) ? rewritten : relation)
                    .ToImmutableArray();
                result.Add(entity with { Relations = new EquatableArray<RelationData>(updated) });
            }
            else
            {
                result.Add(entity);
            }
        }

        foreach (var shape in byPair.Values.OrderBy(static s => s.Identity, StringComparer.Ordinal))
        {
            result.Add(shape.ToEntity());
        }

        return result.ToImmutable();
    }

    /// <summary>
    /// Validates one auto-managed relation and derives its junction shape, or reports INQ090 and returns
    /// null. Every rejection here is a case where synthesizing anyway would produce a table that is
    /// wrong rather than merely unhelpful.
    /// </summary>
    private static JunctionShape? TryBuildShape(
        SourceProductionContext context,
        SqlBuilder sqlBuilder,
        Dictionary<string, EntityData> byName,
        Dictionary<string, List<string?>> claimedTables,
        EntityData parent,
        RelationData relation)
    {
        if (!relation.IsCollection)
        {
            // Shape errors on the declaration itself stay INQ063 — the same reason an explicit
            // many-to-many reports, and not something auto-managed junctions make different.
            return null;
        }

        if (!byName.TryGetValue(relation.ChildEntityFullyQualifiedName, out var child))
        {
            Report(context, parent, relation, "the related type is not a mapped [InquiryTable] entity");
            return null;
        }

        // A synthesized junction declares a FOREIGN KEY to each side, and no provider accepts a foreign
        // key referencing a view. A view is also skipped by the schema emitter, so the reference would
        // point at an object Inquiry never creates.
        if (parent.IsView || child.IsView)
        {
            Report(context, parent, relation,
                $"'{(parent.IsView ? parent.Name : child.Name)}' is mapped with [InquiryView], and a synthesized junction "
                + "declares a foreign key to each side — no provider allows a foreign key to a view");
            return null;
        }

        // The multi-round-trip eager path materializes a junction ROW through its entity materializer,
        // which a synthesized junction has no CLR type for. Every provider supports the grid today, so
        // this is unreachable — but it would emit a reference to a type that does not exist.
        if (!sqlBuilder.SupportsMultiResultBatch)
        {
            Report(context, parent, relation,
                $"the '{sqlBuilder.DialectName}' dialect cannot return multiple result sets from one command, and the "
                + "multi-round-trip fallback materializes a junction row, which an auto-managed junction has no type for");
            return null;
        }

        if (parent.Keys.Count != 1 || child.Keys.Count != 1)
        {
            var composite = parent.Keys.Count != 1 ? parent.Name : child.Name;
            Report(context, parent, relation,
                $"'{composite}' has a composite primary key, and an auto-managed junction names one column per side. "
                + "Map the junction explicitly and use the three-argument constructor, which supports composite keys");
            return null;
        }

        if (string.Equals(parent.FullyQualifiedName, child.FullyQualifiedName, StringComparison.Ordinal))
        {
            Report(context, parent, relation,
                "it is self-referential, so both sides would derive the same column name. Map the junction explicitly "
                + "and use the three-argument constructor, which names each column");
            return null;
        }

        // Canonical ordering. Everything below is derived from (sideA, sideB), never from
        // (parent, child), so the other side of a bidirectional association derives the same table.
        var parentFirst = ComparePhysical(parent, child) <= 0;
        var sideA = parentFirst ? parent : child;
        var sideB = parentFirst ? child : parent;

        // With no override, the two sides must already agree on a schema — picking one silently would
        // put the table in whichever schema sorts first, and move it if either is ever renamed.
        if (relation.AutoJunctionSchema is null &&
            !string.Equals(parent.Schema ?? string.Empty, child.Schema ?? string.Empty, StringComparison.Ordinal))
        {
            Report(context, parent, relation,
                $"'{parent.Name}' and '{child.Name}' are mapped to different schemas "
                + $"('{parent.Schema ?? "(none)"}' and '{child.Schema ?? "(none)"}'), so there is no schema to derive. "
                + "Set JunctionSchema on both sides");
            return null;
        }

        var schema = relation.AutoJunctionSchema ?? sideA.Schema;
        var table = relation.AutoJunctionTable ?? sideA.TableName + "_" + sideB.TableName;

        var parentColumn = relation.AutoParentColumn ?? DeriveColumn(parent);
        var childColumn = relation.AutoChildColumn ?? DeriveColumn(child);

        // Derived names are concatenations, so they can exceed the identifier budget even when both
        // inputs are fine. Held to the same rule as an explicitly named constraint or index rather than
        // shipping a name a provider will truncate — silent truncation collapses two junctions that
        // share a prefix onto one physical table.
        foreach (var identifier in new[] { table, parentColumn, childColumn })
        {
            if (!SchemaEmitter.IsValidExplicitIdentifier(identifier))
            {
                Report(context, parent, relation,
                    $"'{identifier}' is not a usable identifier — it must be non-empty, at most 63 bytes, and free of "
                    + "control characters. Set JunctionTable, ParentColumn, or ChildColumn to a shorter name");
                return null;
            }
        }

        if (schema is not null && !SchemaEmitter.IsValidExplicitIdentifier(schema))
        {
            Report(context, parent, relation, $"'{schema}' is not a usable schema identifier");
            return null;
        }

        if (string.Equals(parentColumn, childColumn, StringComparison.OrdinalIgnoreCase))
        {
            Report(context, parent, relation,
                $"both sides resolve to the column name '{parentColumn}'. Set ParentColumn and ChildColumn to distinct names");
            return null;
        }

        if (IsClaimed(claimedTables, schema, table))
        {
            Report(context, parent, relation,
                $"the junction table '{Qualified(schema, table)}' is already mapped by an entity. An auto-managed junction "
                + "cannot share a table with a mapped entity — that entity may carry soft-delete or filter columns the "
                + "auto read path does not apply. Set JunctionTable, or map the junction explicitly");
            return null;
        }

        // PhysicalKey, not Qualified: joining schema and table with a dot would make (schema "a", table
        // "b.c") and (schema "a.b", table "c") the same identity for two different physical tables.
        return new JunctionShape(
            IdentityPrefix + PhysicalKey(schema, table),
            PhysicalKey(sideA.FullyQualifiedName, sideB.FullyQualifiedName),
            $"'{sideA.Name}' and '{sideB.Name}'",
            schema,
            table,
            sideA,
            sideB,
            parentFirst ? parentColumn : childColumn,
            parentFirst ? childColumn : parentColumn,
            parent.FullyQualifiedName,
            relation.PropertyName,
            parentColumn,
            childColumn);
    }

    /// <summary>
    /// Returns why a candidate shape cannot join the set already synthesized, or null when it is the
    /// legitimate reverse side of one. The only accepted repeat is the OTHER navigation of the same
    /// entity pair describing the same table the same way; every other repeat produces a table that
    /// silently means two different things.
    /// </summary>
    private static string? Describe(
        Dictionary<string, JunctionShape> byPair,
        Dictionary<string, string> pairByIdentity,
        JunctionShape candidate)
    {
        // A DIFFERENT pair already owns this table. Merging would give one link table two meanings, and
        // each side's eager load would read the other's rows. Checked first: it is the only failure that
        // does not depend on having seen this pair before.
        if (pairByIdentity.TryGetValue(candidate.Identity, out var owningPair) &&
            !string.Equals(owningPair, candidate.PairKey, StringComparison.Ordinal))
        {
            var owner = byPair[owningPair];
            return $"table '{Qualified(owner.Schema, owner.Table)}' is already the junction for a different pair of "
                + $"entities ({owner.PairDescription}). Give this association its own JunctionTable";
        }

        if (!byPair.TryGetValue(candidate.PairKey, out var existing))
        {
            return null;
        }

        // The same pair resolved to a different table — one side overrode JunctionTable or JunctionSchema
        // and the other did not. Emitting both would give one association two link tables, so links
        // written through either are invisible from the other side. Two auto-managed associations between
        // the same pair are indistinguishable from this, which is why the remedy is explicit mapping
        // rather than "use a different table name".
        if (!string.Equals(existing.Identity, candidate.Identity, StringComparison.OrdinalIgnoreCase))
        {
            return $"it resolves to table '{Qualified(candidate.Schema, candidate.Table)}' while another "
                + $"[InquiryManyToMany] between the same two entities resolves to '{Qualified(existing.Schema, existing.Table)}'. "
                + "Both sides of one association must state the same JunctionTable and JunctionSchema, or neither; two "
                + "separate associations between the same pair need an explicitly mapped junction entity";
        }

        if (!string.Equals(existing.ColumnA, candidate.ColumnA, StringComparison.Ordinal) ||
            !string.Equals(existing.ColumnB, candidate.ColumnB, StringComparison.Ordinal))
        {
            return $"another [InquiryManyToMany] describes table '{Qualified(existing.Schema, existing.Table)}' with columns "
                + $"'{existing.ColumnA}'/'{existing.ColumnB}', not '{candidate.ColumnA}'/'{candidate.ColumnB}'. Each side names "
                + "its own ParentColumn (the column referencing itself) and ChildColumn, so the reverse side states the two "
                + "swapped";
        }

        // Same pair, same table, same columns — but this side has already declared a navigation. A pair
        // supports at most one auto-managed navigation per side; a second would silently share the same
        // link rows, so both collections would always return identical contents.
        if (existing.WasDeclaredBy(candidate.DeclaringEntity))
        {
            return $"'{candidate.DeclaringEntity}' already declares an auto-managed association with these entities via "
                + $"table '{Qualified(existing.Schema, existing.Table)}'. Each side may declare one; two separate "
                + "associations between the same pair need an explicitly mapped junction entity";
        }

        return null;
    }

    /// <summary>
    /// True when a declared type already owns this table name. A null schema is treated as matching ANY
    /// schema: it means "the provider's default", which on SQL Server spells out as <c>dbo</c> and on
    /// PostgreSQL as <c>public</c> — the same physical object, so comparing the literal strings would
    /// let a junction bind to a mapped entity's table and read link rows that entity's own filters hide.
    /// Two explicit, different schemas are genuinely different objects and do not collide.
    /// </summary>
    private static bool IsClaimed(Dictionary<string, List<string?>> claimedTables, string? schema, string table)
    {
        if (!claimedTables.TryGetValue(table, out var schemas))
        {
            return false;
        }

        foreach (var claimed in schemas)
        {
            if (claimed is null || schema is null ||
                string.Equals(claimed, schema, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Orders two entities so naming never depends on which side declared the relation. The
    /// fully-qualified name is the final tiebreaker: without it, two distinct entities mapped to the
    /// same physical table would compare equal, each declaration would put itself first, and the two
    /// sides of one association would derive different pair keys.
    /// </summary>
    private static int ComparePhysical(EntityData left, EntityData right)
    {
        var bySchema = StringComparer.Ordinal.Compare(left.Schema ?? string.Empty, right.Schema ?? string.Empty);
        if (bySchema != 0) return bySchema;

        var byTable = StringComparer.Ordinal.Compare(left.TableName, right.TableName);
        return byTable != 0 ? byTable : StringComparer.Ordinal.Compare(left.FullyQualifiedName, right.FullyQualifiedName);
    }

    private static string DeriveColumn(EntityData entity) => entity.TableName + "_" + entity.Keys[0].ColumnName;

    private static string PhysicalKey(string? schema, string table) => (schema ?? string.Empty) + "\0" + table;

    private static string Qualified(string? schema, string table)
        => string.IsNullOrEmpty(schema) ? table : schema + "." + table;

    private static void Report(SourceProductionContext context, EntityData entity, RelationData relation, string reason)
        => context.ReportDiagnostic(Diagnostic.Create(
            InquiryDiagnosticDescriptors.AutoJunctionInvalid, relation.Location?.ToLocation(),
            entity.Name, relation.PropertyName, reason));

    /// <summary>The derived shape of one synthesized junction, in canonical (side A, side B) order.</summary>
    private sealed class JunctionShape(
        string identity,
        string pairKey,
        string pairDescription,
        string? schema,
        string table,
        EntityData sideA,
        EntityData sideB,
        string columnA,
        string columnB,
        string declaringEntity,
        string declaringProperty,
        string declaringParentColumn,
        string declaringChildColumn)
    {
        public string Identity { get; } = identity;

        /// <summary>The two linked entities, in canonical order — identifies the association itself.</summary>
        public string PairKey { get; } = pairKey;

        public string PairDescription { get; } = pairDescription;
        public string? Schema { get; } = schema;
        public string Table { get; } = table;
        public string ColumnA { get; } = columnA;
        public string ColumnB { get; } = columnB;
        public string DeclaringEntity { get; } = declaringEntity;
        public string DeclaringProperty { get; } = declaringProperty;

        /// <summary>
        /// Every entity that has declared a navigation onto this junction. Tracked as a set rather than
        /// a single remembered declarer: with only one, the side that did NOT declare first could add
        /// any number of extra navigations and each would compare against the other side and pass.
        /// </summary>
        private readonly HashSet<string> _declaredBy = new(StringComparer.Ordinal);

        public void NoteDeclaredBy(string entityFullyQualifiedName) => _declaredBy.Add(entityFullyQualifiedName);

        public bool WasDeclaredBy(string entityFullyQualifiedName) => _declaredBy.Contains(entityFullyQualifiedName);

        /// <summary>The column referencing <paramref name="entity"/>, which must be one of the two sides.</summary>
        public string ColumnFor(EntityData entity)
            => string.Equals(entity.FullyQualifiedName, DeclaringEntity, StringComparison.Ordinal)
                ? declaringParentColumn
                : (string.Equals(entity.FullyQualifiedName, sideA.FullyQualifiedName, StringComparison.Ordinal) ? ColumnA : ColumnB);

        /// <summary>The column referencing the side that is not <paramref name="entity"/>.</summary>
        public string ColumnForOther(EntityData entity)
            => string.Equals(entity.FullyQualifiedName, DeclaringEntity, StringComparison.Ordinal)
                ? declaringChildColumn
                : (string.Equals(entity.FullyQualifiedName, sideA.FullyQualifiedName, StringComparison.Ordinal) ? ColumnB : ColumnA);

        public EntityData ToEntity()
        {
            var columnA = LinkColumn(ColumnA, sideA);
            var columnB = LinkColumn(ColumnB, sideB);
            var columns = ImmutableArray.Create(columnA, columnB);

            return new EntityData(
                FullyQualifiedName: Identity,
                HintName: "AutoJunction_" + Table,
                Name: Table,
                Namespace: null,
                TableName: Table,
                Schema: Schema,
                Columns: new EquatableArray<ColumnData>(columns),
                Keys: new EquatableArray<ColumnData>(columns),
                Relations: EquatableArray<RelationData>.Empty,
                ClassMaterializerName: string.Empty,
                StructMaterializerName: string.Empty,
                ClassMaterializerFullName: string.Empty,
                StructMaterializerFullName: string.Empty,
                IsMapped: true,
                Diagnostics: EquatableArray<DiagnosticData>.Empty)
            {
                IsSynthesizedJunction = true,
            };
        }

        /// <summary>
        /// Clones the referenced entity's key column, keeping everything that decides the physical type —
        /// length, precision/scale, SqlType, unicode, and the value converter — and clearing every facet
        /// that belongs to the owning entity rather than to the type. Cloning, rather than deriving a
        /// bare foreign-key column, is what makes the junction column storage-compatible with the key it
        /// references; carrying the converter is also what keeps the generated dictionary key types
        /// agreeing on the eager path.
        /// </summary>
        private ColumnData LinkColumn(string name, EntityData referenced)
        {
            var key = referenced.Keys[0];
            return key with
            {
                Location = null,
                PropertyName = name,
                ColumnName = name,
                IsKey = true,
                IsNullable = false,
                IsGenerated = false,
                IsSequentialGuid = false,
                IsCreatedAt = false,
                IsModifiedAt = false,
                IsCreatedBy = false,
                IsModifiedBy = false,
                UseDatabaseDefault = false,
                UseDatabaseDefaultLocation = null,
                SoftDelete = SoftDeleteKind.None,
                IsGlobalFilter = false,
                IsConcurrencyToken = false,
                IsDatabaseGeneratedToken = false,
                DefaultExpression = null,
                DefaultExpressionLocation = null,
                ComputedExpression = null,
                ComputedExpressionLocation = null,
                ComputedExpressionOverrides = EquatableArray<ComputedExpressionOverrideData>.Empty,
                IsIndexed = false,
                IsUnique = false,
                IndexName = null,
                ForeignKeySchema = referenced.Schema,
                ForeignKeyTable = referenced.TableName,
                ForeignKeyColumn = key.ColumnName,
                ForeignKeyConstraintName = null,
                ForeignKeyOnDelete = 0,
                ForeignKeyOnUpdate = 0,
            };
        }
    }
}
