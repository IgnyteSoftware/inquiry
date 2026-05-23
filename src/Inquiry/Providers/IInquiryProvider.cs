namespace Inquiry;

public interface IInquiryProvider
{
    string Name { get; }

    IInquirySqlDialect Dialect { get; }

    IInquiryTypeMapper TypeMapper { get; }

    IInquiryCommandFactory CommandFactory { get; }
}

public interface IInquiryTypeMapper
{
    Type GetProviderType(Type modelType);
}

public interface IInquiryCommandFactory
{
    InquiryCommandPlan<TEntity> BuildFind<TEntity>(IInquiryEntityDescriptor<TEntity> descriptor);

    InquiryCommandPlan<TEntity> BuildSelect<TEntity>(IInquiryEntityDescriptor<TEntity> descriptor, InquiryQuery<TEntity>? query);

    InquiryCommandPlan<TEntity> BuildInsert<TEntity>(IInquiryEntityDescriptor<TEntity> descriptor);

    InquiryCommandPlan<TEntity> BuildUpdate<TEntity>(IInquiryEntityDescriptor<TEntity> descriptor, IReadOnlyList<string>? propertyNames = null);

    InquiryCommandPlan<TEntity> BuildDelete<TEntity>(IInquiryEntityDescriptor<TEntity> descriptor);

    InquiryCommandPlan<TEntity> BuildUpsert<TEntity>(IInquiryEntityDescriptor<TEntity> descriptor);
}

public interface IInquirySqlDialect
{
    string QuoteIdentifier(string identifier);

    string FormatTableName(string? schema, string table);

    string CreateParameterName(string logicalName, int ordinal);

    string LimitOffset(string sql, int? limit, int? offset);

    string BuildInsert<TEntity>(IInquiryEntityDescriptor<TEntity> descriptor);

    string BuildUpdate<TEntity>(IInquiryEntityDescriptor<TEntity> descriptor);

    string BuildDelete<TEntity>(IInquiryEntityDescriptor<TEntity> descriptor);

    string BuildUpsert<TEntity>(IInquiryEntityDescriptor<TEntity> descriptor);
}
