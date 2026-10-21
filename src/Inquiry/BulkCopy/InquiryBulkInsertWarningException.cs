namespace Inquiry.BulkCopy;

/// <summary>
/// Thrown when a native bulk insert wrote rows but the server reported warnings, such as truncated
/// or converted values. The rows were written. When <see cref="IsEnlisted"/> is <see langword="true"/>
/// they stay in the caller's Inquiry transaction until it commits or rolls back. Otherwise they were
/// written on a dedicated connection and are committed, unless the provider enlisted that connection
/// in an ambient <c>System.Transactions</c> scope.
/// </summary>
public sealed class InquiryBulkInsertWarningException: InvalidOperationException
{
    internal InquiryBulkInsertWarningException(string message, long rowsWritten, bool isEnlisted, IReadOnlyList<string> warnings)
        : base(message)
    {
        RowsWritten = rowsWritten;
        IsEnlisted = isEnlisted;
        Warnings = warnings;
    }

    /// <summary>The number of rows the server reported as written.</summary>
    public long RowsWritten { get; }

    /// <summary>
    /// True when the rows were written in the caller's ambient Inquiry transaction, which the caller can
    /// roll back. False when they were written on a dedicated connection. This flag does not track
    /// <c>System.Transactions</c> enlistment.
    /// </summary>
    public bool IsEnlisted { get; }

    /// <summary>The server warning messages.</summary>
    public IReadOnlyList<string> Warnings { get; }
}
