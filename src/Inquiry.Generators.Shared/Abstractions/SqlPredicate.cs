namespace Inquiry.Generators.Abstractions;

/// <summary>
/// The closed set of comparison operators a <c>[InquiryWhere]</c> criterion can express. Mirrors the
/// public <c>Inquiry.Stores.Compare</c> enum value-for-value; the generator translates the public enum
/// into this analyzer-facing one when building <see cref="SqlPredicate"/> instances for a
/// <see cref="SqlBuilder"/>.
/// </summary>
public enum SqlCompareOp
{
    Equal,
    NotEqual,
    GreaterThan,
    GreaterThanOrEqual,
    LessThan,
    LessThanOrEqual,
    Like,
    In,
    Between,
    IsNull,
    IsNotNull,
    NotLike,
    NotBetween,
}

/// <summary>
/// One rendered WHERE criterion handed to a <see cref="SqlBuilder"/>. The generator resolves the
/// public attribute into this shape (column + operator + parameter name(s) + AND/OR linkage) so the
/// builder only does dialect-specific string shaping, never field resolution.
/// </summary>
/// <remarks>
/// <see cref="ParameterName"/> is the single bound parameter for scalar/LIKE/IN ops (the low bound for
/// <see cref="SqlCompareOp.Between"/>); <see cref="ParameterNameHi"/> is the high bound for BETWEEN and
/// null otherwise. Null operators carry no parameters. <see cref="IsOr"/> is true when this criterion
/// joins to the previous one with OR rather than AND (a single flat OR level — no nested grouping).
/// </remarks>
public sealed class SqlPredicate
{
    public SqlPredicate(IColumn column, SqlCompareOp op, string? parameterName, string? parameterNameHi, bool isOr, string? jsonPath = null)
    {
        Column = column;
        Op = op;
        ParameterName = parameterName;
        ParameterNameHi = parameterNameHi;
        IsOr = isOr;
        JsonPath = jsonPath;
    }

    public IColumn Column { get; }
    public SqlCompareOp Op { get; }
    public string? ParameterName { get; }
    public string? ParameterNameHi { get; }
    public bool IsOr { get; }

    /// <summary>
    /// A JSON path (<c>$.a.b</c>) when this criterion filters inside <see cref="Column"/> (a JSON text
    /// column), or null for an ordinary column comparison. When set, <see cref="SqlBuilder.RenderPredicate"/>
    /// compares the dialect's JSON extraction of this path rather than the bare column.
    /// </summary>
    public string? JsonPath { get; }

    /// <summary>Number of bound parameters the operator consumes (IN counts its single sentinel as 1).</summary>
    public static int ParameterArity(SqlCompareOp op) => op switch
    {
        SqlCompareOp.IsNull or SqlCompareOp.IsNotNull => 0,
        SqlCompareOp.Between or SqlCompareOp.NotBetween => 2,
        _ => 1,
    };
}
