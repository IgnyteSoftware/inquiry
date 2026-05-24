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
        Inquiry = inquiry ?? throw new ArgumentNullException(nameof(inquiry));
    }

    /// <summary>
    /// Gets the high-level Inquiry facade used by generated store methods and user-defined custom queries.
    /// </summary>
    protected IInquiry Inquiry { get; }
}
