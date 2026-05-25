using Microsoft.CodeAnalysis;

namespace Inquiry.Generators.Models;

internal sealed class StoreMethodModel
{
    public StoreMethodModel(
        IMethodSymbol symbol,
        StoreOperation operation,
        ColumnModel? fieldColumn = null,
        string? procedureName = null)
    {
        Symbol = symbol;
        Operation = operation;
        FieldColumn = fieldColumn;
        ProcedureName = procedureName;
    }

    public IMethodSymbol Symbol { get; }

    public StoreOperation Operation { get; }

    public ColumnModel? FieldColumn { get; }

    /// <summary>The stored procedure name; only set for <see cref="StoreOperation.StoredProcedure"/>.</summary>
    public string? ProcedureName { get; }
}
