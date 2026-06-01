namespace Inquiry.Entities;

/// <summary>
/// Marks a mapped column as the entity's optimistic-concurrency token (W6). Generated UPDATE and
/// DELETE statements append <c>AND &lt;token&gt; = @&lt;token&gt;</c> so a mutation only affects the
/// row whose token still matches the value last read; a 0-row result means a concurrent writer won.
/// </summary>
/// <remarks>
/// <para>
/// Derives from <see cref="InquiryColumnAttribute"/> so the property is discovered as a column (like
/// <c>[InquiryKey]</c>) — it is not an orthogonal marker and does not need a separate
/// <c>[InquiryColumn]</c>. At most one token per entity, and the token may not also be the key.
/// </para>
/// <para>
/// The default (ORM-managed) form is a numeric column the ORM bumps with <c>SET &lt;token&gt; =
/// &lt;token&gt; + 1</c> on every UPDATE. Setting <see cref="DatabaseGenerated"/> selects the
/// database-managed form (SQL Server <c>rowversion</c>): the database supplies and advances the value,
/// so it is excluded from INSERT and never SET by the ORM. Database-managed tokens are only supported
/// on dialects with a native row-version type (currently SQL Server).
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class InquiryConcurrencyTokenAttribute : InquiryColumnAttribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InquiryConcurrencyTokenAttribute"/> class.
    /// </summary>
    public InquiryConcurrencyTokenAttribute()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="InquiryConcurrencyTokenAttribute"/> class with an
    /// explicit column name.
    /// </summary>
    public InquiryConcurrencyTokenAttribute(string name)
        : base(name)
    {
    }

    /// <summary>
    /// Gets or sets a value indicating whether the token is database-managed (e.g. SQL Server
    /// <c>rowversion</c>). When <see langword="false"/> (default), the ORM manages a numeric token.
    /// </summary>
    public bool DatabaseGenerated { get; set; }
}
