using System.Collections.Immutable;
using Inquiry.Generators.Abstractions;
using Inquiry.Generators.Infrastructure;

namespace Inquiry.Generators.Models;

/// <summary>
/// Everything one store's emission reads: the store, the mapped entities and projections it names, which of
/// those names have an invalid computed expression, and the stub policy. A store re-emits only when this changes.
/// </summary>
internal sealed record StoreSlice(
    StoreData Store,
    EquatableArray<EntityData> Entities,
    EquatableArray<ProjectionData> Projections,
    EquatableArray<string> InvalidEntityNames,
    bool EmitUnsupportedOperationStubs,
    bool Emit)
{
    public static StoreSlice Build(StoreData store, EntityIndex index, ProjectionIndex projections, bool emitUnsupportedOperationStubs)
    {
        if (!index.Emit)
        {
            return new StoreSlice(store, EquatableArray<EntityData>.Empty, EquatableArray<ProjectionData>.Empty,
                EquatableArray<string>.Empty, false, false);
        }

        var methods = store.Methods.AsImmutableArray();
        var relations = index.Mapped.TryGetValue(store.EntityFullyQualifiedName, out var root)
            ? root.Relations.AsImmutableArray()
            : ImmutableArray<RelationData>.Empty;
        var relationNames = relations.SelectMany(static relation => new[] { relation.ChildEntityFullyQualifiedName, relation.JunctionEntityFullyQualifiedName });
        var resultSetNames = methods.SelectMany(static method => method.ProcedureResultSetTypeFqns.AsImmutableArray());
        var resultNames = methods.Select(static method => method.ResultElementTypeFqn);

        var entities = index.Lookup(new[] { store.EntityFullyQualifiedName }.Concat(relationNames).Concat(resultSetNames));
        var invalid = relationNames.Concat(resultNames).Concat(resultSetNames)
            .Where(name => name is not null && index.ComputedInvalid.Contains(name))
            .Select(static name => name!)
            .Distinct(StringComparer.Ordinal)
            .ToImmutableArray();

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var mappedProjections = ImmutableArray.CreateBuilder<ProjectionData>();
        foreach (var name in resultNames)
        {
            if (name is not null && seen.Add(name) && projections.Mapped.TryGetValue(name, out var projection))
            {
                mappedProjections.Add(projection);
            }
        }

        return new StoreSlice(store, entities, mappedProjections.ToImmutable(), invalid, emitUnsupportedOperationStubs, true);
    }
}

/// <summary>Registration and collection-parameter artifacts of one store, which registration and the schema read.</summary>
internal sealed record StoreFacts(string StoreName, StoreRegistration? Registration, EquatableArray<CollectionParameterArtifact> Artifacts);

/// <summary>The buffered output of one store: its sources and diagnostics in order, and its facts.</summary>
internal sealed record StoreEmission(
    string StoreName,
    EquatableArray<EmittedSource> Sources,
    EquatableArray<EmittedDiagnostic> Diagnostics,
    StoreFacts Facts)
{
    public static StoreEmission Create(StoreSlice slice, SqlBuilder sqlBuilder, CancellationToken cancellationToken)
    {
        var store = slice.Store;
        if (!slice.Emit)
        {
            return new StoreEmission(store.Name, EquatableArray<EmittedSource>.Empty, EquatableArray<EmittedDiagnostic>.Empty,
                new StoreFacts(store.Name, null, EquatableArray<CollectionParameterArtifact>.Empty));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var collector = new EmissionCollector();
        foreach (var diagnostic in store.Diagnostics)
        {
            collector.ReportDiagnostic(diagnostic.ToDiagnostic());
        }

        var entities = new Dictionary<string, EntityData>();
        foreach (var entity in slice.Entities)
        {
            entities[entity.FullyQualifiedName] = entity;
        }

        var projections = new Dictionary<string, ProjectionData>();
        foreach (var projection in slice.Projections)
        {
            projections[projection.FullyQualifiedName] = projection;
        }

        var emission = StoreProcessor.Emit(
            collector,
            store,
            entities,
            projections,
            sqlBuilder,
            slice.EmitUnsupportedOperationStubs,
            new HashSet<string>(slice.InvalidEntityNames.AsImmutableArray(), StringComparer.Ordinal));
        return new StoreEmission(store.Name, collector.Sources, collector.Diagnostics,
            new StoreFacts(store.Name, emission?.Registration, emission?.Artifacts ?? ImmutableArray<CollectionParameterArtifact>.Empty));
    }
}
