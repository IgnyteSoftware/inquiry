using System.Collections.Immutable;
using Inquiry.Generators.Abstractions;
using Inquiry.Generators.Infrastructure;

namespace Inquiry.Generators.Models;

/// <summary>
/// The resolved entity model after auto-junction synthesis, built once per run from every table and view.
/// It is cheap to rebuild and compares by value, so the per-entity and per-store steps that read it re-run
/// only when the entities they name change.
/// </summary>
internal sealed class EntityIndex : IEquatable<EntityIndex>
{
    public static readonly EntityIndex Empty = new(false, EquatableArray<EntityData>.Empty, 0,
        EquatableArray<string>.Empty, EquatableArray<EmittedDiagnostic>.Empty);

    private EntityIndex(
        bool emit,
        EquatableArray<EntityData> entities,
        int declaredCount,
        EquatableArray<string> computedInvalidNames,
        EquatableArray<EmittedDiagnostic> junctionDiagnostics)
    {
        Emit = emit;
        Entities = entities;
        DeclaredCount = declaredCount;
        ComputedInvalidNames = computedInvalidNames;
        JunctionDiagnostics = junctionDiagnostics;

        // A name declared twice keeps the last mapped declaration at the first one's position. Relation checks
        // and registration depend on that order.
        foreach (var entity in entities)
        {
            if (entity.IsMapped)
            {
                Mapped[entity.FullyQualifiedName] = entity;
            }
        }

        Winners = Mapped.Values.ToImmutableArray();
        ComputedInvalid = new HashSet<string>(computedInvalidNames.AsImmutableArray(), StringComparer.Ordinal);
    }

    /// <summary>Whether this generator owns the dialect and some model needs code.</summary>
    public bool Emit { get; }

    /// <summary>Tables, then views, then synthesized junctions, with auto-junction relations rewritten.</summary>
    public EquatableArray<EntityData> Entities { get; }

    /// <summary>The number of declared tables and views at the start of <see cref="Entities"/>.</summary>
    public int DeclaredCount { get; }

    public EquatableArray<string> ComputedInvalidNames { get; }

    /// <summary>INQ090 diagnostics from junction synthesis, in the order it reported them.</summary>
    public EquatableArray<EmittedDiagnostic> JunctionDiagnostics { get; }

    public Dictionary<string, EntityData> Mapped { get; } = new();

    public ImmutableArray<EntityData> Winners { get; }

    public HashSet<string> ComputedInvalid { get; }

    public bool AnyMapped => Mapped.Count > 0;

    public static EntityIndex Build(
        ImmutableArray<ResolvedEntity> tables,
        ImmutableArray<ResolvedEntity> views,
        MaterializerGate gate,
        SqlBuilder sqlBuilder,
        CancellationToken cancellationToken)
    {
        if (!gate.Emit)
        {
            return Empty;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var computedInvalidNames = new SortedSet<string>(StringComparer.Ordinal);
        var resolved = ImmutableArray.CreateBuilder<EntityData>(tables.Length + views.Length);
        foreach (var item in tables.Concat(views))
        {
            resolved.Add(item.Entity);
            if (item.ComputedInvalid)
            {
                computedInvalidNames.Add(item.Entity.FullyQualifiedName);
            }
        }

        // Auto-managed [InquiryManyToMany] junctions become real entities here, before anything looks at the
        // entity set. Each declaring relation is rewritten to name the synthesized junction, so relation
        // validation, the SQL builders and the schema emitter see the shape an explicit junction produces.
        var collector = new EmissionCollector();
        var entities = AutoJunctionSynthesizer.Synthesize(collector, resolved.MoveToImmutable(), sqlBuilder);
        return new EntityIndex(true, entities, tables.Length + views.Length,
            computedInvalidNames.ToImmutableArray(), collector.Diagnostics);
    }

    /// <summary>One relation slice per mapped entity, in the order relation validation reports.</summary>
    public ImmutableArray<RelationSlice> RelationSlices()
        => Winners.Select(entity => new RelationSlice(entity, Lookup(entity.Relations.AsImmutableArray()
            .SelectMany(static relation => new[] { relation.ChildEntityFullyQualifiedName, relation.JunctionEntityFullyQualifiedName }))))
            .ToImmutableArray();

    /// <summary>Synthesized junctions, which rotating-token diagnostics check after the declared entities.</summary>
    public EquatableArray<EntityData> SynthesizedJunctions()
        => Entities.AsImmutableArray().Skip(DeclaredCount).Where(static entity => entity.IsMapped).ToImmutableArray();

    /// <summary>Registration facts for mapped entities. Synthesized junctions have no CLR type to register.</summary>
    public EquatableArray<EntityRegistration> Registrations()
        => Winners.Where(static entity => !entity.IsSynthesizedJunction)
            .Select(static entity => new EntityRegistration(entity.FullyQualifiedName, entity.ClassMaterializerFullName))
            .ToImmutableArray();

    /// <summary>The mapped entities with the given names, each once, in first-seen order. Unmapped names are skipped.</summary>
    public EquatableArray<EntityData> Lookup(IEnumerable<string?> names)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = ImmutableArray.CreateBuilder<EntityData>();
        foreach (var name in names)
        {
            if (name is not null && seen.Add(name) && Mapped.TryGetValue(name, out var entity))
            {
                result.Add(entity);
            }
        }

        return result.ToImmutable();
    }

    public bool Equals(EntityIndex? other)
        => other is not null && (ReferenceEquals(this, other) ||
            Emit == other.Emit
            && DeclaredCount == other.DeclaredCount
            && Entities.Equals(other.Entities)
            && ComputedInvalidNames.Equals(other.ComputedInvalidNames)
            && JunctionDiagnostics.Equals(other.JunctionDiagnostics));

    public override bool Equals(object? obj) => Equals(obj as EntityIndex);

    public override int GetHashCode() => (Entities.Count * 31) + DeclaredCount;
}

/// <summary>Mapped projections by name, last declaration winning at the first one's position.</summary>
internal sealed class ProjectionIndex : IEquatable<ProjectionIndex>
{
    private ProjectionIndex(EquatableArray<ProjectionData> projections)
    {
        Projections = projections;
        foreach (var projection in projections)
        {
            if (projection.IsMapped)
            {
                Mapped[projection.FullyQualifiedName] = projection;
            }
        }
    }

    public EquatableArray<ProjectionData> Projections { get; }

    public Dictionary<string, ProjectionData> Mapped { get; } = new();

    public static ProjectionIndex Build(ImmutableArray<ProjectionData> projections) => new(projections);

    public EquatableArray<EntityRegistration> Registrations()
        => Mapped.Values.Select(static projection => new EntityRegistration(projection.FullyQualifiedName, projection.ClassMaterializerFullName))
            .ToImmutableArray();

    public bool Equals(ProjectionIndex? other)
        => other is not null && (ReferenceEquals(this, other) || Projections.Equals(other.Projections));

    public override bool Equals(object? obj) => Equals(obj as ProjectionIndex);

    public override int GetHashCode() => Projections.Count;
}

/// <summary>One mapped entity and the mapped entities its relations name, which is all relation validation reads.</summary>
internal sealed record RelationSlice(EntityData Entity, EquatableArray<EntityData> Related)
{
    public Dictionary<string, EntityData> CreateLookup()
    {
        var lookup = new Dictionary<string, EntityData>();
        foreach (var entity in Related)
        {
            lookup[entity.FullyQualifiedName] = entity;
        }

        return lookup;
    }
}

/// <summary>
/// Models whose fully qualified name more than one mapped declaration uses. They keep one materializer in a
/// shared output, because two outputs cannot add the same hint name. Normally all three are empty.
/// </summary>
internal sealed record DuplicateMaterializers(
    EquatableArray<EntityData> Entities,
    EquatableArray<ProjectionData> Projections,
    EquatableArray<AdHocData> AdHocs)
{
    public static readonly DuplicateMaterializers None = new(
        EquatableArray<EntityData>.Empty, EquatableArray<ProjectionData>.Empty, EquatableArray<AdHocData>.Empty);

    public static DuplicateMaterializers Build(
        EntityIndex index, MaterializerGate gate, ProjectionIndex projections, ImmutableArray<AdHocData> adHocs)
        => !gate.Emit
            ? None
            : new(
                index.Winners.Where(entity => !entity.IsSynthesizedJunction && gate.IsDuplicateEntity(entity.FullyQualifiedName)).ToImmutableArray(),
                projections.Mapped.Values.Where(projection => gate.IsDuplicateProjection(projection.FullyQualifiedName)).ToImmutableArray(),
                MappedAdHocs(adHocs).Where(adHoc => gate.IsDuplicateAdHoc(adHoc.FullyQualifiedName)).ToImmutableArray());

    /// <summary>Registration facts for mapped DTOs, one per fully qualified name.</summary>
    public static EquatableArray<EntityRegistration> Registrations(ImmutableArray<AdHocData> adHocs)
        => MappedAdHocs(adHocs)
            .Select(static adHoc => new EntityRegistration(adHoc.FullyQualifiedName, adHoc.ClassMaterializerFullName))
            .ToImmutableArray();

    private static Dictionary<string, AdHocData>.ValueCollection MappedAdHocs(ImmutableArray<AdHocData> adHocs)
    {
        var mapped = new Dictionary<string, AdHocData>();
        foreach (var adHoc in adHocs)
        {
            if (adHoc.IsMapped)
            {
                mapped[adHoc.FullyQualifiedName] = adHoc;
            }
        }

        return mapped.Values;
    }
}
