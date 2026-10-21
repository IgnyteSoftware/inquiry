namespace Inquiry;

/// <summary>
/// Thrown when an optimistic-concurrency conflict is detected on a token entity: a generated
/// UPDATE or DELETE affected 0 rows because the row's concurrency token no longer matched the value
/// last read. Only raised when <see cref="InquiryOptions.ThrowOnConcurrencyConflict"/> is enabled;
/// otherwise the mutation simply reports <c>false</c>.
/// </summary>
public sealed class InquiryConcurrencyException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InquiryConcurrencyException"/> class.
    /// </summary>
    public InquiryConcurrencyException()
        : base("The operation affected 0 rows because of an optimistic-concurrency conflict (the row was modified or removed by another writer).")
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="InquiryConcurrencyException"/> class with a
    /// specified error message.
    /// </summary>
    public InquiryConcurrencyException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="InquiryConcurrencyException"/> class with a
    /// specified error message and inner exception.
    /// </summary>
    public InquiryConcurrencyException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
