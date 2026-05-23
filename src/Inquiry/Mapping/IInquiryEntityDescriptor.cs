using System.Data.Common;

namespace Inquiry;

public interface IInquiryEntityDescriptor<TEntity>
{
    string TableName { get; }

    string? Schema { get; }

    IReadOnlyList<IInquiryPropertyDescriptor<TEntity>> Properties { get; }

    IReadOnlyList<IInquiryPropertyDescriptor<TEntity>> Keys { get; }

    IInquiryPropertyDescriptor<TEntity>? ConcurrencyToken { get; }
}

public interface IInquiryPropertyDescriptor<TEntity>
{
    string PropertyName { get; }

    string ColumnName { get; }

    Type PropertyType { get; }

    bool IsKey { get; }

    bool IsDatabaseGenerated { get; }

    bool IsInsertable { get; }

    bool IsUpdateable { get; }

    object? GetValue(TEntity entity);

    void SetValue(TEntity entity, object? value);
}

public interface IInquiryMaterializer<TEntity>
{
    TEntity Materialize(DbDataReader reader);
}

public interface IInquiryTypeConverter<TModel, TDatabase>
{
    TDatabase ToDatabase(TModel value);

    TModel FromDatabase(TDatabase value);
}
