namespace Inquiry.Stores;

/// <summary>The scalar aggregate function applied by <see cref="InquiryAggregateAttribute"/>.</summary>
public enum InquiryAggregateFunction
{
    /// <summary>SQL <c>SUM(column)</c>.</summary>
    Sum,

    /// <summary>SQL <c>AVG(column)</c>.</summary>
    Avg,

    /// <summary>SQL <c>MIN(column)</c>.</summary>
    Min,

    /// <summary>SQL <c>MAX(column)</c>.</summary>
    Max,
}

/// <summary>
/// Generates a method returning a scalar aggregate (<c>SUM/AVG/MIN/MAX</c>) over a mapped column.
/// The method returns <c>Task&lt;T&gt;</c> where <c>T</c> is the aggregate's result type. It can take
/// parameters for <see cref="InquiryWhereAttribute"/> criteria followed by a cancellation token.
/// Use a nullable <c>T</c> to receive <see langword="null"/> when there are no matching rows.
/// </summary>
/// <remarks>
/// The scalar result is coerced to <c>T</c> from the provider's returned type. SUM/AVG over a
/// floating-point column can come back as a <c>double</c>; coercing that to a <c>decimal T</c> carries
/// IEEE-754 rounding. Use a decimal/integer column (and matching <c>T</c>) when exact results matter.
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class InquiryAggregateAttribute : Attribute
{
    /// <summary>Initializes a new instance of the <see cref="InquiryAggregateAttribute"/> class.</summary>
    /// <param name="function">The aggregate function to apply.</param>
    /// <param name="column">The mapped property or column name to aggregate.</param>
    public InquiryAggregateAttribute(InquiryAggregateFunction function, string column)
    {
        if (string.IsNullOrWhiteSpace(column))
        {
            throw new ArgumentException("Aggregate column cannot be empty.", nameof(column));
        }

        Function = function;
        Column = column;
    }

    /// <summary>Gets the aggregate function.</summary>
    public InquiryAggregateFunction Function { get; }

    /// <summary>Gets the mapped property or column name to aggregate.</summary>
    public string Column { get; }
}
