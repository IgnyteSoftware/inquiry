using Microsoft.CodeAnalysis;

namespace Inquiry.Generators.Models;

internal sealed class ColumnModel
{
    public ColumnModel(IPropertySymbol symbol, string propertyName, string columnName, TypeInfo type, bool isKey, bool isGenerated, ForeignKeyModel? foreignKey)
    {
        Symbol = symbol;
        PropertyName = propertyName;
        ColumnName = columnName;
        Type = type;
        IsKey = isKey;
        IsGenerated = isGenerated;
        ForeignKey = foreignKey;
    }

    public IPropertySymbol Symbol { get; }

    public string PropertyName { get; }

    public string ColumnName { get; }

    public TypeInfo Type { get; }

    public bool IsKey { get; }

    public bool IsGenerated { get; }

    public ForeignKeyModel? ForeignKey { get; }
}
