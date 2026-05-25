using Microsoft.CodeAnalysis;
using System.Collections.Generic;

namespace Inquiry.Generators.Models;

internal sealed class EntityModel
{
    public EntityModel(
        INamedTypeSymbol symbol,
        string tableName,
        string? schema,
        List<ColumnModel> columns,
        ColumnModel key,
        List<RelationModel> relations)
    {
        Symbol = symbol;
        TableName = tableName;
        Schema = schema;
        Columns = columns;
        Key = key;
        Relations = relations;
    }

    public INamedTypeSymbol Symbol { get; }

    public string TableName { get; }

    public string? Schema { get; }

    public List<ColumnModel> Columns { get; }

    public ColumnModel Key { get; }

    /// <summary>Navigation properties marked with <c>[InquiryRelation]</c>.</summary>
    public List<RelationModel> Relations { get; }
}
