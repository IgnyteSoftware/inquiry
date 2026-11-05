namespace Inquiry.Stores;

/// <summary>
/// Declares one WHERE criterion on a <see cref="InquirySelectAllByPredicateAttribute"/>,
/// <see cref="InquiryCountAttribute"/>, <see cref="InquiryExistsAttribute"/>,
/// <see cref="InquiryAggregateAttribute"/>, <see cref="InquiryUpdateAttribute"/>,
/// <see cref="InquiryDeleteAttribute"/> or <see cref="InquiryHardDeleteAttribute"/> method. Other
/// operations do not apply criteria and report build error INQ115. Apply it multiple times; the
/// criteria are combined in declaration order. By default they bind positionally to the method's non-<see cref="System.Threading.CancellationToken"/> parameters. Set
/// <see cref="Parameter"/> (and <see cref="UpperParameter"/> for a range) to bind by name instead.
/// </summary>
/// <remarks>
/// Parameter consumption per operator: scalar operators (comparison and <see cref="Compare.Like"/>)
/// take one parameter; <see cref="Compare.Between"/> takes two (low then high); <see cref="Compare.In"/>
/// takes one collection parameter; <see cref="Compare.IsNull"/> and <see cref="Compare.IsNotNull"/>
/// take none. Criteria join with AND unless <see cref="Or"/> is set. Use <see cref="OpenGroups"/>,
/// <see cref="CloseGroups"/>, and <see cref="Not"/> for explicit Boolean grouping.
/// <para>
/// A method uses one binding form. When any value-consuming criterion sets <see cref="Parameter"/>,
/// every value-consuming criterion must, and each value parameter must be bound exactly once. Offset
/// paging and <see cref="System.Threading.CancellationToken"/> parameters stay trailing and positional.
/// </para>
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
    /// With criterion-level <see cref="Not"/>, null still disables the criterion; only the comparison
    /// is negated. Inside a negated group, null makes this criterion true before group negation.
    /// It does not disable the enclosing group.
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

    /// <summary>
    /// Gets or sets the name of the method parameter that supplies this criterion's value, usually
    /// written as <c>nameof(parameter)</c>. For <see cref="Compare.Between"/> and
    /// <see cref="Compare.NotBetween"/> it names the lower bound. For <see cref="Compare.In"/> and
    /// <see cref="Compare.NotIn"/> it names the collection parameter. <see cref="Compare.IsNull"/> and
    /// <see cref="Compare.IsNotNull"/> take no parameter. Null (the default) binds by position.
    /// On a specification attribute, write the name as a string; every method that uses the
    /// specification must declare a parameter with that name.
    /// </summary>
    public string? Parameter { get; set; }

    /// <summary>
    /// Gets or sets the name of the method parameter that supplies the upper bound of a
    /// <see cref="Compare.Between"/> or <see cref="Compare.NotBetween"/> criterion. Set it together with
    /// <see cref="Parameter"/>, which names the lower bound. Other operators do not accept it.
    /// </summary>
    public string? UpperParameter { get; set; }
}
