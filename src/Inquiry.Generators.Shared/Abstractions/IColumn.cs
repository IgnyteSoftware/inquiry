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

    /// <summary>
    /// True when this column is an <c>[InquiryGlobalFilter]</c>: every generated SELECT AND-composes
    /// <c>"col" = </c><see cref="GlobalFilterKeepWhenTrue"/> so non-matching rows are invisible to reads.
    /// </summary>
    bool IsGlobalFilter { get; }

    /// <summary>
    /// The bool value a <see cref="IsGlobalFilter"/> column must equal for a row to stay visible
    /// (<c>InquiryGlobalFilter.KeepWhen</c>, default true). Ignored when <see cref="IsGlobalFilter"/> is false.
    /// </summary>
    bool GlobalFilterKeepWhenTrue { get; }

    /// <summary>
    /// <c>InquiryGlobalFilter.Name</c>, or null for an unnamed (non-bypassable) filter. Ignored when
    /// <see cref="IsGlobalFilter"/> is false.
    /// </summary>
    string? GlobalFilterName { get; }

    /// <summary>
    /// <c>InquiryGlobalFilter.ContextKey</c>, or null for the constant-bool mode. Non-null switches
    /// the composed predicate to an equality against an execute-time-bound ambient parameter.
    /// Ignored when <see cref="IsGlobalFilter"/> is false.
    /// </summary>
    string? GlobalFilterContextKey { get; }

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

    /// <summary>
    /// True when this column is the entity's <c>[InquiryCreatedBy]</c> auditing user column — like
    /// <see cref="IsCreatedAt"/>, written once by INSERT and excluded from every UPDATE SET.
    /// </summary>
    bool IsCreatedBy { get; }

    // ---- DDL generation metadata ---------------------------------------------------------

    /// <summary>Dialect-neutral type class driving <c>SqlBuilder.MapColumnType</c>.</summary>
    DbTypeClass TypeClass { get; }

    /// <summary>Whether the column allows NULL. Inferred from the CLR type (keys are always NOT NULL).</summary>
    bool IsNullable { get; }

    /// <summary>Explicit physical SQL type override (single-dialect escape hatch); null to infer.</summary>
    string? SqlType { get; }

    /// <summary>The effective provider CLR type after converter/enum projection.</summary>
    string ProviderClrTypeName { get; }

    /// <summary>Whether a converter may produce null independently of the collection element.</summary>
    bool ProviderValueIsNullable { get; }

    /// <summary>Declared length for string/binary types (0 = unspecified → dialect default).</summary>
    int Length { get; }

    bool IsLengthSpecified { get; }

    /// <summary>Declared numeric precision (0 = unspecified → dialect default).</summary>
    int Precision { get; }

    bool IsPrecisionSpecified { get; }

    /// <summary>Declared numeric scale (0 = unspecified).</summary>
    int Scale { get; }

    bool IsScaleSpecified { get; }

    /// <summary>Raw SQL default expression for the column, or null for none.</summary>
    string? DefaultExpression { get; }

    /// <summary>
    /// Raw SQL expression for a server-computed column (<c>[InquiryColumn(Computed = …)]</c>), or
    /// null. When set, the DDL renders the dialect's computed-column form and the column is excluded
    /// from generated INSERT/UPDATE.
    /// </summary>
    string? ComputedExpression { get; }

    /// <summary>Referenced table name when this column is a foreign key, or null.</summary>
    string? ForeignKeyTable { get; }

    /// <summary>Referenced schema name when this column is a foreign key, or null.</summary>
    string? ForeignKeySchema { get; }

    /// <summary>Referenced column name when this column is a foreign key, or null.</summary>
    string? ForeignKeyColumn { get; }

    /// <summary>Emit a single-column index on this column.</summary>
    bool IsIndexed { get; }

    /// <summary>Whether string columns and parameters use Unicode types.</summary>
    bool IsUnicode { get; }

    bool IsUnicodeSpecified { get; }

    /// <summary>Emit a single-column UNIQUE index on this column.</summary>
    bool IsUnique { get; }

    /// <summary>Explicit index name, or null to use the default.</summary>
    string? IndexName { get; }
}
