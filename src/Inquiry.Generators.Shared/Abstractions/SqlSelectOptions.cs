using System.Collections.Generic;

namespace Inquiry.Generators.Abstractions;

/// <summary>
/// One ORDER BY term, already resolved to a quoted column identifier with its sort direction. Produced
/// in <c>StoreProcessor</c> (which has the columns and the builder) so providers only assemble strings.
/// </summary>
public sealed class OrderByTerm
{
    public OrderByTerm(string quotedColumn, bool descending)
    {
        QuotedColumn = quotedColumn;
        Descending = descending;
    }

    public string QuotedColumn { get; }
    public bool Descending { get; }
}

/// <summary>
/// Value object carrying the already-resolved, already-quoted pieces a <see cref="SqlBuilder"/> needs to
/// append ORDER BY, offset pagination, and keyset pagination to a SELECT. Field resolution and quoting
/// happen in <c>StoreProcessor</c>; builders only concatenate strings. Designed to be entity-agnostic
/// (quoted column fragments + parameter names) so other workstreams (e.g. projections) can reuse it.
/// </summary>
public sealed class SqlSelectOptions
{
    public SqlSelectOptions(
        IReadOnlyList<OrderByTerm> orderBy,
        string? offsetParameter = null,
        string? limitParameter = null,
        IReadOnlyList<string>? keysetColumns = null,
        IReadOnlyList<string>? keysetCursorParameters = null,
        bool keysetDescending = false)
    {
        OrderBy = orderBy;
        OffsetParameter = offsetParameter;
        LimitParameter = limitParameter;
        KeysetColumns = keysetColumns ?? System.Array.Empty<string>();
        KeysetCursorParameters = keysetCursorParameters ?? System.Array.Empty<string>();
        KeysetDescending = keysetDescending;
    }

    /// <summary>The ORDER BY terms (quoted column + direction), most-significant first.</summary>
    public IReadOnlyList<OrderByTerm> OrderBy { get; }

    /// <summary>The offset parameter name (e.g. <c>@__offset</c>), or null when not offset-paginated.</summary>
    public string? OffsetParameter { get; }

    /// <summary>The limit parameter name (e.g. <c>@__limit</c>), or null when not offset-paginated.</summary>
    public string? LimitParameter { get; }

    /// <summary>The quoted keyset comparison columns, most-significant first (keyset paging only).</summary>
    public IReadOnlyList<string> KeysetColumns { get; }

    /// <summary>The cursor parameter names aligned to <see cref="KeysetColumns"/> (keyset paging only).</summary>
    public IReadOnlyList<string> KeysetCursorParameters { get; }

    /// <summary>True when the keyset walks descending (uses <c>&lt;</c> instead of <c>&gt;</c>).</summary>
    public bool KeysetDescending { get; }
}
