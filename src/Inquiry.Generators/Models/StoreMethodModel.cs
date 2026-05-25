using Microsoft.CodeAnalysis;

namespace Inquiry.Generators.Models;

internal sealed class StoreMethodModel
{
    public StoreMethodModel(
        IMethodSymbol symbol,
        StoreOperation operation,
        ColumnModel? fieldColumn = null,
        string? procedureName = null,
        bool returnsEntity = false)
    {
        Symbol = symbol;
        Operation = operation;
        FieldColumn = fieldColumn;
        ProcedureName = procedureName;
        ReturnsEntity = returnsEntity;
    }

    public IMethodSymbol Symbol { get; }

    public StoreOperation Operation { get; }

    public ColumnModel? FieldColumn { get; }

    /// <summary>The stored procedure name; only set for <see cref="StoreOperation.StoredProcedure"/>.</summary>
    public string? ProcedureName { get; }

    /// <summary>Whether a mutation operation returns the database row after mutation.</summary>
    public bool ReturnsEntity { get; }
}
