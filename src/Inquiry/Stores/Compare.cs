namespace Inquiry.Stores;

/// <summary>
/// The comparison operator a <see cref="InquiryWhereAttribute"/> criterion applies to its field.
/// </summary>
/// <remarks>
/// <see cref="Between"/> consumes two positional parameters (low and high bound); <see cref="In"/>
/// consumes one collection parameter that is expanded at run time; <see cref="IsNull"/> and
/// <see cref="IsNotNull"/> consume no parameters. All other operators consume one scalar parameter.
/// </remarks>
public enum Compare
{
    /// <summary><c>column = @param</c>.</summary>
    Equal,

    /// <summary><c>column &lt;&gt; @param</c>.</summary>
    NotEqual,

    /// <summary><c>column &gt; @param</c>.</summary>
    GreaterThan,

    /// <summary><c>column &gt;= @param</c>.</summary>
    GreaterThanOrEqual,

    /// <summary><c>column &lt; @param</c>.</summary>
    LessThan,

    /// <summary><c>column &lt;= @param</c>.</summary>
    LessThanOrEqual,

    /// <summary><c>column LIKE @param</c> (string field; caller escapes <c>%</c>/<c>_</c> wildcards).</summary>
    Like,

    /// <summary><c>column IN (…)</c> expanded at run time from a single collection parameter.</summary>
    In,

    /// <summary><c>column BETWEEN @lo AND @hi</c> (consumes two parameters).</summary>
    Between,

    /// <summary><c>column IS NULL</c> (consumes no parameters).</summary>
    IsNull,

    /// <summary><c>column IS NOT NULL</c> (consumes no parameters).</summary>
    IsNotNull,
}
