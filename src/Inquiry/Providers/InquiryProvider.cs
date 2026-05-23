namespace Inquiry;

public sealed class InquiryProvider : IInquiryProvider
{
    public InquiryProvider(string name, IInquirySqlDialect dialect)
        : this(name, dialect, new DefaultInquiryTypeMapper(), new InquiryCommandFactory(dialect))
    {
    }

    public InquiryProvider(
        string name,
        IInquirySqlDialect dialect,
        IInquiryTypeMapper typeMapper,
        IInquiryCommandFactory commandFactory)
    {
        Name = string.IsNullOrWhiteSpace(name)
            ? throw new ArgumentException("Provider name cannot be empty.", nameof(name))
            : name;
        Dialect = dialect ?? throw new ArgumentNullException(nameof(dialect));
        TypeMapper = typeMapper ?? throw new ArgumentNullException(nameof(typeMapper));
        CommandFactory = commandFactory ?? throw new ArgumentNullException(nameof(commandFactory));
    }

    public string Name { get; }

    public IInquirySqlDialect Dialect { get; }

    public IInquiryTypeMapper TypeMapper { get; }

    public IInquiryCommandFactory CommandFactory { get; }
}

public sealed class DefaultInquiryTypeMapper : IInquiryTypeMapper
{
    public Type GetProviderType(Type modelType)
    {
        return Nullable.GetUnderlyingType(modelType) ?? modelType;
    }
}
