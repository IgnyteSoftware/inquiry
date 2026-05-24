namespace Inquiry.Stores;

/// <summary>
/// Base type for generated and user-defined Inquiry stores when a public key type is useful.
/// </summary>
public abstract class InquiryStore<TEntity, TKey> : InquiryStore<TEntity>
    where TEntity : class
    where TKey : notnull
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InquiryStore{TEntity,TKey}"/> class.
    /// </summary>
    protected InquiryStore(IInquiry inquiry)
        : base(inquiry)
    {
    }
}
