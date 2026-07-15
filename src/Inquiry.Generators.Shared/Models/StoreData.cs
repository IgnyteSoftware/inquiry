using Inquiry.Generators.Infrastructure;

namespace Inquiry.Generators.Models;

/// <summary>
/// Value-equatable replacement for the old store candidate. Carries store-local facts extracted in
/// the discovery transform; it is linked to its <see cref="EntityFullyQualifiedName"/> entity in the
/// combined emit stage (which also resolves field columns, validates entity-dependent rules, and
/// emits the partial class).
/// </summary>
internal sealed record StoreData(
    string Name,
    string HintName,
    string? Namespace,
    string FullyQualifiedName,
    string EntityFullyQualifiedName,
    bool IsEmittable,
    bool GenerateInterface,
    EquatableArray<StoreMethodData> Methods,
    LocationData? Location,
    EquatableArray<DiagnosticData> Diagnostics);
