using Inquiry.Generators.Abstractions;

namespace Inquiry.Generators.Models;

/// <summary>
/// Value-equatable replacement for the old <c>ColumnModel</c>. Still implements <see cref="IColumn"/>
/// so it feeds <c>SqlBuildContext</c> / <c>SqlBuilder</c> unchanged at emit time.
/// </summary>
/// <remarks>
/// FOUNDATION CONVENTION: additive column metadata MUST be added as init-only
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

    /// <summary>Insert/upsert assigns a v7 GUID when this key is unset ([InquiryKey(SequentialGuid = true)]).</summary>
    public bool IsSequentialGuid { get; init; }

    /// <summary>[InquiryCreatedAt]: stamped on insert when unset; excluded from UPDATE SET and bind.</summary>
    public bool IsCreatedAt { get; init; }

    /// <summary>[InquiryModifiedAt]: stamped on every generated insert/update/upsert before binding.</summary>
    public bool IsModifiedAt { get; init; }

    /// <summary>[InquiryCreatedBy]: stamped from the ambient user on insert when unset; excluded from UPDATE.</summary>
    public bool IsCreatedBy { get; init; }

    /// <summary>[InquiryModifiedBy]: stamped from the ambient user on every generated insert/update/upsert.</summary>
    public bool IsModifiedBy { get; init; }

    /// <summary>True for any write-once-on-insert auditing column (created-at / created-by).</summary>
    public bool IsCreatedAudit => IsCreatedAt || IsCreatedBy;
    public bool UseDatabaseDefault { get; init; }
    public SoftDeleteKind SoftDelete { get; init; } = SoftDeleteKind.None;
    public bool IsConcurrencyToken { get; init; }
    public bool IsDatabaseGeneratedToken { get; init; }
    public bool EnumAsString { get; init; }

    // DDL generation metadata.
    public DbTypeClass TypeClass { get; init; }
    public bool IsNullable { get; init; }
    public string? SqlType { get; init; }
    public int Length { get; init; }
    public int Precision { get; init; }
    public int Scale { get; init; }
    public string? DefaultExpression { get; init; }
    public string? ForeignKeyTable { get; init; }
    public string? ForeignKeySchema { get; init; }
    public string? ForeignKeyColumn { get; init; }
    public bool IsIndexed { get; init; }
    public bool IsUnique { get; init; }
    public string? IndexName { get; init; }

    /// <summary>The value converter applied to this column, or null for a directly-mapped type.</summary>
    public ConverterData? Converter { get; init; }
}
