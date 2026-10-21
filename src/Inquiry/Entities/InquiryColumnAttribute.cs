namespace Inquiry.Entities;

/// <summary>
/// Maps a CLR property to a relational database column.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public class InquiryColumnAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InquiryColumnAttribute"/> class.
    /// </summary>
    public InquiryColumnAttribute()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="InquiryColumnAttribute"/> class.
    /// </summary>
    public InquiryColumnAttribute(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Column name cannot be empty.", nameof(name));
        }

        Name = name;
    }

    /// <summary>
    /// Gets the mapped column name, or <see langword="null"/> to use the CLR property name.
    /// </summary>
    public string? Name { get; }

    /// <summary>
    /// Gets or sets a value indicating whether INSERT statements should omit this column
    /// so the database default expression supplies the value.
    /// </summary>
    public bool UseDatabaseDefault { get; set; }

    /// <summary>
    /// DDL generation: explicit physical SQL type for this column (e.g. <c>"NVARCHAR(64)"</c>).
    /// Used verbatim, overriding the inferred type. Dialect-specific — only set it when targeting a
    /// single provider; otherwise rely on <see cref="Length"/>/<see cref="Precision"/>/inference.
    /// </summary>
    public string? SqlType { get; set; }

    /// <summary>DDL generation: declared length for string/binary columns; 0 uses the dialect default.</summary>
    public int Length { get; set; }

    /// <summary>DDL generation: declared numeric precision for decimal columns; 0 uses the dialect default.</summary>
    public int Precision { get; set; }

    /// <summary>DDL generation: declared numeric scale for decimal columns; 0 uses the dialect default.</summary>
    public int Scale { get; set; }

    /// <summary>
    /// Raw SQL <c>DEFAULT</c> expression for the column (for example, <c>"0"</c> or
    /// <c>"(UUID())"</c>), or null. Inquiry emits this expression into generated DDL. MySQL also
    /// uses it to evaluate a non-<c>AUTO_INCREMENT</c> database-default key exactly once when
    /// emulating insert-returning. For that MySQL capture path it must be a standalone scalar that
    /// does not reference mapped columns, and it must match the deployed database default.
    /// </summary>
    public string? DefaultExpression { get; set; }

    /// <summary>
    /// DDL generation: a raw SQL expression making this a <b>server-computed column</b> (EF
    /// <c>HasComputedColumnSql</c> / XPO persistent-alias analog), e.g.
    /// <c>"FirstName || ' ' || LastName"</c>. The database calculates the value, so the column is
    /// excluded from generated INSERT/UPDATE but read normally in SELECTs and materialized into the
    /// property. The generated <c>CREATE TABLE</c> emits the dialect's computed-column form (stored
    /// on PostgreSQL/MySQL; the standard expression form elsewhere). A computed column cannot be a
    /// key, database-generated, database-defaulted, an auditing column, the soft-delete indicator,
    /// or a concurrency token. The expression is raw SQL — keep it free of untrusted input.
    /// </summary>
    public string? Computed { get; set; }

    /// <summary>
    /// DDL generation: emit a single-column index on this column. Each flagged column produces its
    /// own index (there is no composite/multi-column index in v1). Redundant on a primary-key column
    /// (the PK already indexes it). Index DDL is idempotent (<c>IF NOT EXISTS</c>) only on SQLite and
    /// PostgreSQL; on SQL Server/MySQL/Oracle the generated <c>CREATE INDEX</c> is run-once.
    /// </summary>
    public bool IsIndexed { get; set; }

    /// <summary>Gets or sets a value indicating whether string columns and parameters use Unicode types.</summary>
    public bool IsUnicode { get; set; } = true;

    /// <summary>DDL generation: emit a single-column UNIQUE index on this column. See <see cref="IsIndexed"/> for caveats.</summary>
    public bool IsUnique { get; set; }

    /// <summary>
    /// DDL generation: explicit index name; defaults to <c>IX_&lt;table&gt;_&lt;column&gt;</c>
    /// (<c>UX_</c> when unique). The default omits the schema, so set an explicit name to avoid a clash
    /// when the same table name exists in multiple schemas, or to stay within an engine's identifier
    /// length limit (e.g. Oracle's).
    /// </summary>
    public string? IndexName { get; set; }

    /// <summary>
    /// A value-converter type implementing <see cref="IInquiryValueConverter{TModel,TProvider}"/>
    /// that maps this property's CLR type to/from a provider primitive. Must be stateless with a public
    /// parameterless constructor.
    /// </summary>
    public Type? Converter { get; set; }
}
