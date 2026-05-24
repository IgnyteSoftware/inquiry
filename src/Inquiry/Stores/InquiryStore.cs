namespace Inquiry.Stores;

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
