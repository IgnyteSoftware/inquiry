using System.Collections.Concurrent;

namespace Inquiry;

public sealed class InquiryMetadataRegistry
{
    private readonly ConcurrentDictionary<Type, object> _descriptors = new();
    private readonly InquiryConventionOptions _conventions;

    public InquiryMetadataRegistry()
        : this(new InquiryConventionOptions())
    {
    }

    public InquiryMetadataRegistry(InquiryConventionOptions conventions)
    {
        _conventions = conventions ?? throw new ArgumentNullException(nameof(conventions));
    }

    public InquiryMetadataRegistry Register<TEntity>(IInquiryEntityDescriptor<TEntity> descriptor)
    {
        _descriptors[typeof(TEntity)] = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        return this;
    }

    public IInquiryEntityDescriptor<TEntity> GetDescriptor<TEntity>()
    {
        var descriptor = _descriptors.GetOrAdd(
            typeof(TEntity),
            static (type, state) => ReflectionInquiryEntityDescriptor.Create(type, state._conventions),
            this);

        return (IInquiryEntityDescriptor<TEntity>)descriptor;
    }
}
