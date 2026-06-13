namespace Inquiry.Generators.Abstractions;

/// <summary>
/// The soft-delete representation a column carries. <see cref="None"/> for ordinary columns;
/// <see cref="BooleanFlag"/> for a <c>bool</c> indicator (active = <c>0</c>); <see cref="Timestamp"/>
/// for a nullable <c>DateTime</c>/<c>DateTimeOffset</c> indicator (active = <c>NULL</c>).
/// </summary>
public enum SoftDeleteKind
{
    None,
    BooleanFlag,
    Timestamp,
}

/// <summary>
/// Dialect-neutral classification of a column's CLR type, used by <c>SqlBuilder.MapColumnType</c>
/// to pick a physical column type for DDL generation without leaking Roslyn types into the provider
/// contract. The generator collapses enums to their underlying integer class during discovery, so this
/// enum has no dedicated enum member.
/// </summary>
public enum DbTypeClass
{
    String,
    Boolean,
    Byte,
    Int16,
    Int32,
    Int64,
    Single,
    Double,
    Decimal,
    DateTime,
    DateTimeOffset,
    DateOnly,
    TimeOnly,
    Guid,
    ByteArray,
}

/// <summary>
/// Minimal column contract consumed by <see cref="SqlBuilder"/> implementations. Provider analyzers
/// only see this surface; the generator's richer internal column model implements it.
/// </summary>
public interface IColumn
{
    string PropertyName { get; }
    string ColumnName { get; }
    bool IsKey { get; }
    bool IsGenerated { get; }
    bool UseDatabaseDefault { get; }

    /// <summary>The soft-delete role this column plays; <see cref="SoftDeleteKind.None"/> for most.</summary>
    SoftDeleteKind SoftDelete { get; }

    /// <summary>True when this column is the entity's optimistic-concurrency token.</summary>
    bool IsConcurrencyToken { get; }

    /// <summary>
    /// True when the concurrency token is database-managed (e.g. SQL Server <c>rowversion</c>) — the
    /// database supplies its value, so it is excluded from INSERT and never SET by the ORM.
    /// </summary>
    bool IsDatabaseGeneratedToken { get; }

    /// <summary>
    /// True when this column is the entity's <c>[InquiryCreatedAt]</c> auditing timestamp — written
    /// once by INSERT and excluded from every UPDATE SET (including upsert conflict branches).
    /// </summary>
    bool IsCreatedAt { get; }

    // ---- DDL generation metadata ---------------------------------------------------------

    /// <summary>Dialect-neutral type class driving <c>SqlBuilder.MapColumnType</c>.</summary>
    DbTypeClass TypeClass { get; }

    /// <summary>Whether the column allows NULL. Inferred from the CLR type (keys are always NOT NULL).</summary>
    bool IsNullable { get; }

    /// <summary>Explicit physical SQL type override (single-dialect escape hatch); null to infer.</summary>
    string? SqlType { get; }

    /// <summary>Declared length for string/binary types (0 = unspecified → dialect default).</summary>
    int Length { get; }

    /// <summary>Declared numeric precision (0 = unspecified → dialect default).</summary>
    int Precision { get; }

    /// <summary>Declared numeric scale (0 = unspecified).</summary>
    int Scale { get; }

    /// <summary>Raw SQL default expression for the column, or null for none.</summary>
    string? DefaultExpression { get; }

    /// <summary>Referenced table name when this column is a foreign key, or null.</summary>
    string? ForeignKeyTable { get; }

    /// <summary>Referenced schema name when this column is a foreign key, or null.</summary>
    string? ForeignKeySchema { get; }

    /// <summary>Referenced column name when this column is a foreign key, or null.</summary>
    string? ForeignKeyColumn { get; }

    /// <summary>Emit a single-column index on this column.</summary>
    bool IsIndexed { get; }

    /// <summary>Emit a single-column UNIQUE index on this column.</summary>
    bool IsUnique { get; }

    /// <summary>Explicit index name, or null to use the default.</summary>
    string? IndexName { get; }
}
