namespace Inquiry;

public sealed class InquiryPropertyDescriptor<TEntity> : IInquiryPropertyDescriptor<TEntity>
{
    private readonly Func<TEntity, object?> _getter;
    private readonly Action<TEntity, object?> _setter;

    public InquiryPropertyDescriptor(
        string propertyName,
        string columnName,
        Type propertyType,
        bool isKey,
        bool isDatabaseGenerated,
        bool isInsertable,
        bool isUpdateable,
        Func<TEntity, object?> getter,
        Action<TEntity, object?> setter)
    {
        PropertyName = string.IsNullOrWhiteSpace(propertyName)
            ? throw new ArgumentException("Property name cannot be empty.", nameof(propertyName))
            : propertyName;
        ColumnName = string.IsNullOrWhiteSpace(columnName)
            ? throw new ArgumentException("Column name cannot be empty.", nameof(columnName))
            : columnName;
        PropertyType = propertyType ?? throw new ArgumentNullException(nameof(propertyType));
        IsKey = isKey;
        IsDatabaseGenerated = isDatabaseGenerated;
        IsInsertable = isInsertable;
        IsUpdateable = isUpdateable;
        _getter = getter ?? throw new ArgumentNullException(nameof(getter));
        _setter = setter ?? throw new ArgumentNullException(nameof(setter));
    }

    public string PropertyName { get; }

    public string ColumnName { get; }

    public Type PropertyType { get; }

    public bool IsKey { get; }

    public bool IsDatabaseGenerated { get; }

    public bool IsInsertable { get; }

    public bool IsUpdateable { get; }

    public object? GetValue(TEntity entity)
    {
        return _getter(entity);
    }

    public void SetValue(TEntity entity, object? value)
    {
        _setter(entity, value);
    }
}
