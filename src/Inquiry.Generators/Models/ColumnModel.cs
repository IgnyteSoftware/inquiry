using Microsoft.CodeAnalysis;

namespace Inquiry.Generators.Models;

internal sealed class ColumnModel
{
    public ColumnModel(
        IPropertySymbol symbol,
        string propertyName,
        string columnName,
        TypeInfo type,
        bool isKey,
        bool isGenerated,
        bool useDatabaseDefault)
    {
        Symbol = symbol;
        PropertyName = propertyName;
        ColumnName = columnName;
        Type = type;
        IsKey = isKey;
        IsGenerated = isGenerated;
        UseDatabaseDefault = useDatabaseDefault;
    }

    public IPropertySymbol Symbol { get; }

    public string PropertyName { get; }

    public string ColumnName { get; }

    public TypeInfo Type { get; }

    public bool IsKey { get; }

    public bool IsGenerated { get; }

    public bool UseDatabaseDefault { get; }
}
