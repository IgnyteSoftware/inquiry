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
    /// W7 (DDL generation): explicit physical SQL type for this column (e.g. <c>"NVARCHAR(64)"</c>).
    /// Used verbatim, overriding the inferred type. Dialect-specific — only set it when targeting a
    /// single provider; otherwise rely on <see cref="Length"/>/<see cref="Precision"/>/inference.
    /// </summary>
    public string? SqlType { get; set; }

    /// <summary>W7 (DDL generation): declared length for string/binary columns; 0 uses the dialect default.</summary>
    public int Length { get; set; }

    /// <summary>W7 (DDL generation): declared numeric precision for decimal columns; 0 uses the dialect default.</summary>
    public int Precision { get; set; }

    /// <summary>W7 (DDL generation): declared numeric scale for decimal columns; 0 uses the dialect default.</summary>
    public int Scale { get; set; }

    /// <summary>W7 (DDL generation): raw SQL <c>DEFAULT</c> expression for the column (e.g. <c>"0"</c>), or null.</summary>
    public string? DefaultExpression { get; set; }

    /// <summary>W7b (DDL generation): emit a single-column index on this column.</summary>
    public bool IsIndexed { get; set; }

    /// <summary>W7b (DDL generation): emit a single-column UNIQUE index on this column.</summary>
    public bool IsUnique { get; set; }

    /// <summary>W7b (DDL generation): explicit index name; defaults to <c>IX_&lt;table&gt;_&lt;column&gt;</c> (<c>UX_</c> when unique).</summary>
    public string? IndexName { get; set; }

    /// <summary>
    /// W10b: a value-converter type implementing <see cref="IInquiryValueConverter{TModel,TProvider}"/>
    /// that maps this property's CLR type to/from a provider primitive. Must be stateless with a public
    /// parameterless constructor.
    /// </summary>
    public Type? Converter { get; set; }
}
