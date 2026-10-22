namespace Inquiry.Stores;

/// <summary>
/// Base type for generated and user-defined Inquiry stores.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S2326:Unused type parameters should be removed", Justification = "Published API; generated and user stores derive from InquiryStore<TEntity>, so removing the parameter breaks them.")]
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
