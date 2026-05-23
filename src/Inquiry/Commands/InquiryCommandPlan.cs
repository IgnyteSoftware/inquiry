namespace Inquiry;

public sealed class InquiryCommandPlan<TEntity>
{
    public InquiryCommandPlan(
        string commandText,
        IReadOnlyList<InquiryCommandParameter<TEntity>> parameters,
        IReadOnlyList<IInquiryPropertyDescriptor<TEntity>> projection)
    {
        CommandText = commandText ?? throw new ArgumentNullException(nameof(commandText));
        Parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
        Projection = projection ?? throw new ArgumentNullException(nameof(projection));
    }

    public string CommandText { get; }

    public IReadOnlyList<InquiryCommandParameter<TEntity>> Parameters { get; }

    public IReadOnlyList<IInquiryPropertyDescriptor<TEntity>> Projection { get; }
}

public sealed class InquiryCommandParameter<TEntity>
{
    public InquiryCommandParameter(string name, IInquiryPropertyDescriptor<TEntity> property)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Property = property ?? throw new ArgumentNullException(nameof(property));
    }

    public string Name { get; }

    public IInquiryPropertyDescriptor<TEntity> Property { get; }
}
