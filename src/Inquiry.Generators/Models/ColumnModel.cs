namespace Inquiry.Generators.Models;

internal sealed class ColumnModel
{
    public ColumnModel(
        string propertyName,
        string columnName,
        TypeInfo type,
        bool isKey,
        bool isGenerated,
        bool useDatabaseDefault)
    {
        PropertyName = propertyName;
        ColumnName = columnName;
        Type = type;
        IsKey = isKey;
        IsGenerated = isGenerated;
        UseDatabaseDefault = useDatabaseDefault;
    }

    public string PropertyName { get; }

    public string ColumnName { get; }

    public TypeInfo Type { get; }

    public bool IsKey { get; }

    public bool IsGenerated { get; }

    public bool UseDatabaseDefault { get; }
}
