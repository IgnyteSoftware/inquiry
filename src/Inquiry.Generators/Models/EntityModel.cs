using Microsoft.CodeAnalysis;
using System.Collections.Generic;

namespace Inquiry.Generators.Models;

internal sealed class EntityModel
{
    public EntityModel(INamedTypeSymbol symbol, string tableName, string? schema, List<ColumnModel> columns, ColumnModel key)
    {
        Symbol = symbol;
        TableName = tableName;
        Schema = schema;
        Columns = columns;
        Key = key;
    }

    public INamedTypeSymbol Symbol { get; }

    public string TableName { get; }

    public string? Schema { get; }

    public List<ColumnModel> Columns { get; }

    public ColumnModel Key { get; }
}
