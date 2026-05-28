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
        IReadOnlyList<ColumnModel> keys,
        List<RelationModel> relations)
    {
        Symbol = symbol;
        TableName = tableName;
        Schema = schema;
        Columns = columns;
        Keys = keys;
        Relations = relations;
    }

    public INamedTypeSymbol Symbol { get; }

    public string TableName { get; }

    public string? Schema { get; }

    public List<ColumnModel> Columns { get; }

    /// <summary>The key column(s) for the entity, in declaration order. Always contains at least one.</summary>
    public IReadOnlyList<ColumnModel> Keys { get; }

    /// <summary>The first key column. Convenience accessor for single-key entities.</summary>
    public ColumnModel Key => Keys[0];

    /// <summary>Navigation properties marked with <c>[InquiryRelation]</c>.</summary>
    public List<RelationModel> Relations { get; }
}
