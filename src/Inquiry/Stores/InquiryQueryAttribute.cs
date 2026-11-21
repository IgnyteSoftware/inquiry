namespace Inquiry.Stores;

/// <summary>
/// Implements a store method with handwritten SQL. The generator binds each named parameter in the
/// SQL, such as <c>@memberKey</c>, to the method parameter with that exact name, and reports a build
/// error for a name with no matching parameter and for a parameter the SQL does not use.
/// </summary>
/// <remarks>
/// <para>
/// The SQL is an attribute argument, so it is a compile-time constant. Concatenated <c>const</c>
/// strings are allowed, for example to share one CTE between a page query and its count. The
/// generator does not read names inside string literals, quoted identifiers or comments. Write
/// parameters with <c>@</c> on every dialect. Oracle also accepts <c>:name</c>, and SQLite accepts
/// <c>:name</c> and <c>$name</c>. The generated command uses the dialect's own parameter form.
/// </para>
/// <para>
/// The method ends with a <see cref="System.Threading.CancellationToken"/> and returns one of:
/// <c>Task&lt;IReadOnlyList&lt;T&gt;&gt;</c> or <c>Task&lt;T?&gt;</c> (single row or <see langword="null"/>),
/// where <c>T</c> is an <c>[InquiryTable]</c>, <c>[InquiryView]</c>, <c>[InquiryProjection]</c> or
/// <c>[InquiryAdHoc]</c> type of the same assembly; <c>Task&lt;int&gt;</c> for the affected-row
/// count; or <c>Task&lt;TScalar&gt;</c> for the first column of the first row, where <c>TScalar</c> is
/// <c>byte</c>, <c>short</c>, <c>int</c>, <c>long</c>, <c>float</c>, <c>double</c>, <c>decimal</c>, <c>bool</c>,
/// <c>string</c> or <see cref="DateTime"/>, or a nullable one. Result columns map to
/// the row type by name. Set <see cref="Scalar"/> to read a <c>Task&lt;int&gt;</c> method as a scalar.
/// </para>
/// <para>
/// The method runs in the active Inquiry transaction when there is one. Global filters, soft-delete
/// filters and audit values do not apply to handwritten SQL. A store over an
/// <c>[InquiryView]</c> entity cannot declare query methods, because the SQL can write.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class InquiryQueryAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of <see cref="InquiryQueryAttribute"/> with the SQL to run.
    /// </summary>
    public InquiryQueryAttribute(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            throw new ArgumentException("SQL cannot be empty.", nameof(sql));
        }

        Sql = sql;
    }

    /// <summary>Gets the SQL the method runs.</summary>
    public string Sql { get; }

    /// <summary>
    /// When <see langword="true"/>, a <c>Task&lt;int&gt;</c> method returns the first column of the
    /// first row instead of the affected-row count. Other scalar return types are always read this way.
    /// </summary>
    public bool Scalar { get; set; }
}
