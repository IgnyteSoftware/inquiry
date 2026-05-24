namespace Inquiry;

/// <summary>
/// Base type for generated and user-defined Inquiry stores.
/// </summary>
public abstract class InquiryStore<TEntity>
    where TEntity : class
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InquiryStore{TEntity}"/> class.
    /// </summary>
    protected InquiryStore(IInquiry inquiry)
    {
        _inquiry = inquiry ?? throw new ArgumentNullException(nameof(inquiry));
    }

    /// <summary>
    /// Gets the high-level Inquiry facade for custom store queries.
    /// </summary>
    protected IInquiry Inquiry => _inquiry;

    /// <summary>
    /// Provides direct access to the high-level Inquiry facade for custom store queries.
    /// </summary>
    protected readonly IInquiry _inquiry;
}

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
