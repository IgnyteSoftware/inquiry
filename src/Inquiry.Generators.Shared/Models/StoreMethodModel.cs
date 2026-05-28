using Microsoft.CodeAnalysis;
using System.Collections.Generic;

namespace Inquiry.Generators.Models;

internal sealed class StoreMethodModel
{
    public StoreMethodModel(
        IMethodSymbol symbol,
        StoreOperation operation,
        IReadOnlyList<ColumnModel>? fieldColumns = null,
        string? procedureName = null,
        bool returnsEntity = false,
        bool returnsList = false)
    {
        Symbol = symbol;
        Operation = operation;
        FieldColumns = fieldColumns ?? System.Array.Empty<ColumnModel>();
        ProcedureName = procedureName;
        ReturnsEntity = returnsEntity;
        ReturnsList = returnsList;
    }

    public IMethodSymbol Symbol { get; }

    public StoreOperation Operation { get; }

    /// <summary>
    /// The column(s) referenced by an <see cref="StoreOperation.SelectAllByField"/> method,
    /// in attribute-declaration order. Empty for other operations.
    /// </summary>
    public IReadOnlyList<ColumnModel> FieldColumns { get; }

    /// <summary>The stored procedure name; only set for <see cref="StoreOperation.StoredProcedure"/>.</summary>
    public string? ProcedureName { get; }

    /// <summary>Whether a mutation operation returns the database row after mutation.</summary>
    public bool ReturnsEntity { get; }

    /// <summary>
    /// Whether a SelectAll-style method declared its return type as <c>Task&lt;IReadOnlyList&lt;T&gt;&gt;</c>
    /// (buffered) vs <c>IAsyncEnumerable&lt;T&gt;</c> (streaming). Buffered methods bypass the
    /// IAsyncEnumerable state machine on the hot path.
    /// </summary>
    public bool ReturnsList { get; }
}
