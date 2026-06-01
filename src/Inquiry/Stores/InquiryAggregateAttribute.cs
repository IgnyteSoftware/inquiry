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
/// The method takes only a <see cref="System.Threading.CancellationToken"/> and returns
/// <c>Task&lt;T&gt;</c> where <c>T</c> is the aggregate's result type (use a nullable <c>T</c> to
/// receive <see langword="null"/> when there are no rows). Respects the soft-delete active filter
/// when the entity declares one.
/// </summary>
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
