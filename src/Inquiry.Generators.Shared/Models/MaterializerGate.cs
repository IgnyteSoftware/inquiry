using System.Collections.Immutable;
using Inquiry.Generators.Infrastructure;

namespace Inquiry.Generators.Models;

/// <summary>
/// Decides whether a model gets its own materializer output. <see cref="Emit"/> is true when this
/// generator owns the dialect and some model needs code. A fully qualified name that more than one
/// mapped declaration uses is left to the shared model output, which keeps one materializer for it.
/// </summary>
internal sealed record MaterializerGate(
    bool Emit,
    EquatableArray<string> DuplicateEntityNames,
    EquatableArray<string> DuplicateProjectionNames,
    EquatableArray<string> DuplicateAdHocNames)
{
    public static MaterializerGate Create(
        bool emit,
        ImmutableArray<ResolvedEntity> tables,
        ImmutableArray<ResolvedEntity> views,
        ImmutableArray<ProjectionData> projections,
        ImmutableArray<AdHocData> adHocs)
        => new(
            emit,
            Duplicates(tables.Concat(views).Where(static item => item.Entity.IsMapped).Select(static item => item.Entity.FullyQualifiedName)),
            Duplicates(projections.Where(static item => item.IsMapped).Select(static item => item.FullyQualifiedName)),
            Duplicates(adHocs.Where(static item => item.IsMapped).Select(static item => item.FullyQualifiedName)));

    public bool Emits(EntityData entity) => Emit && entity.IsMapped && !IsDuplicateEntity(entity.FullyQualifiedName);

    public bool Emits(ProjectionData projection) => Emit && projection.IsMapped && !IsDuplicateProjection(projection.FullyQualifiedName);

    public bool Emits(AdHocData adHoc) => Emit && adHoc.IsMapped && !IsDuplicateAdHoc(adHoc.FullyQualifiedName);

    public bool IsDuplicateEntity(string fullyQualifiedName) => Contains(DuplicateEntityNames, fullyQualifiedName);

    public bool IsDuplicateProjection(string fullyQualifiedName) => Contains(DuplicateProjectionNames, fullyQualifiedName);

    public bool IsDuplicateAdHoc(string fullyQualifiedName) => Contains(DuplicateAdHocNames, fullyQualifiedName);

    private static bool Contains(EquatableArray<string> names, string name)
        => names.Count > 0 && names.AsImmutableArray().Contains(name, System.StringComparer.Ordinal);

    private static EquatableArray<string> Duplicates(IEnumerable<string> names)
        => new(names.GroupBy(static name => name, System.StringComparer.Ordinal)
            .Where(static group => group.Count() > 1)
            .Select(static group => group.Key)
            .ToImmutableArray());
}
