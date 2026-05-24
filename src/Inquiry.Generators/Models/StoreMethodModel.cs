using Microsoft.CodeAnalysis;

namespace Inquiry.Generators.Models;

internal sealed class StoreMethodModel
{
    public StoreMethodModel(IMethodSymbol symbol, StoreOperation operation, ColumnModel? fieldColumn = null)
    {
        Symbol = symbol;
        Operation = operation;
        FieldColumn = fieldColumn;
    }

    public IMethodSymbol Symbol { get; }

    public StoreOperation Operation { get; }

    public ColumnModel? FieldColumn { get; }
}
