namespace Inquiry.Stores;

/// <summary>
/// Declares one WHERE criterion on a <see cref="InquirySelectAllByPredicateAttribute"/> method.
/// Apply it multiple times; the criteria are combined in declaration order and bind positionally to
/// the method's non-<see cref="System.Threading.CancellationToken"/> parameters.
/// </summary>
/// <remarks>
/// Parameter consumption per operator: scalar operators (comparison and <see cref="Compare.Like"/>)
/// take one parameter; <see cref="Compare.Between"/> takes two (low then high); <see cref="Compare.In"/>
/// takes one collection parameter; <see cref="Compare.IsNull"/> and <see cref="Compare.IsNotNull"/>
/// take none. Criteria join with AND unless <see cref="Or"/> is set. Use <see cref="OpenGroups"/>,
/// <see cref="CloseGroups"/>, and <see cref="Not"/> for explicit Boolean grouping.
/// </remarks>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class InquiryWhereAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InquiryWhereAttribute"/> class.
    /// </summary>
    /// <param name="field">A mapped property or column name.</param>
    /// <param name="op">The comparison operator. Defaults to <see cref="Compare.Equal"/>.</param>
    public InquiryWhereAttribute(string field, Compare op = Compare.Equal)
    {
        if (string.IsNullOrWhiteSpace(field))
        {
            throw new ArgumentException("Field name cannot be empty.", nameof(field));
        }

        Field = field;
        Op = op;
    }

    /// <summary>Gets the mapped property or column name this criterion filters on.</summary>
    public string Field { get; }

    /// <summary>Gets the comparison operator applied to <see cref="Field"/>.</summary>
    public Compare Op { get; }

    /// <summary>
    /// Gets or sets a value indicating whether this criterion joins to the previous one with OR
    /// (rather than the default AND). Has no effect on the first criterion.
    /// </summary>
    public bool Or { get; set; }

    /// <summary>Gets or sets the number of parenthesized groups opened before this criterion.</summary>
    public int OpenGroups { get; set; }

    /// <summary>Gets or sets the number of parenthesized groups closed after this criterion.</summary>
    public int CloseGroups { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this criterion, or the group opened by it, is negated.
    /// </summary>
    public bool Not { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether a null parameter disables this criterion. Optional
    /// criteria support one-parameter scalar operators and keep one constant SQL shape.
    /// </summary>
    public bool Optional { get; set; }

    /// <summary>
    /// Gets or sets a JSON path (e.g. <c>"$.address.city"</c>) to filter <em>inside</em> a JSON column.
    /// When set, <see cref="Field"/> must name a plain <see cref="string"/> column holding JSON text (no
    /// value converter), and the criterion compares the dialect's JSON-extraction of that path
    /// (<c>json_extract</c> / <c>JSON_VALUE</c> / <c>#&gt;&gt;</c>) against the bound parameter as text.
    /// Null (the default) for an ordinary column comparison. See INQ060 for the placement rules.
    /// </summary>
    public string? JsonPath { get; set; }
}
