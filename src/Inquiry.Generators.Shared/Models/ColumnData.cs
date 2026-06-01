using Inquiry.Generators.Abstractions;

namespace Inquiry.Generators.Models;

/// <summary>
/// Value-equatable replacement for the old <c>ColumnModel</c>. Still implements <see cref="IColumn"/>
/// so it feeds <c>SqlBuildContext</c> / <c>SqlBuilder</c> unchanged at emit time.
/// </summary>
/// <remarks>
/// FOUNDATION CONVENTION (Phase 0 / F1): additive column metadata MUST be added as init-only
/// properties with sensible defaults in this record body — never as new positional constructor
/// parameters. There is a single construction site (<c>EntityProcessor.DiscoverColumns</c>) using an
/// object initializer, so optional additions (e.g. concurrency-token, soft-delete, converter, DDL
/// metadata) default cleanly and parallel feature branches do not conflict on the constructor.
/// </remarks>
internal sealed record ColumnData : IColumn
{
    public required string PropertyName { get; init; }
    public required string ColumnName { get; init; }
    public required TypeData Type { get; init; }
    public bool IsKey { get; init; }
    public bool IsGenerated { get; init; }
    public bool UseDatabaseDefault { get; init; }
    public SoftDeleteKind SoftDelete { get; init; } = SoftDeleteKind.None;
    public bool IsConcurrencyToken { get; init; }
    public bool IsDatabaseGeneratedToken { get; init; }
    public bool EnumAsString { get; init; }

    // W7 DDL generation metadata.
    public DbTypeClass TypeClass { get; init; }
    public bool IsNullable { get; init; }
    public string? SqlType { get; init; }
    public int Length { get; init; }
    public int Precision { get; init; }
    public int Scale { get; init; }
    public string? DefaultExpression { get; init; }
    public string? ForeignKeyTable { get; init; }
    public string? ForeignKeyColumn { get; init; }
}
