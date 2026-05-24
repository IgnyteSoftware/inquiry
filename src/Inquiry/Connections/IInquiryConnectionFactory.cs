using System.Data.Common;

namespace Inquiry.Connections;

/// <summary>
/// Creates and opens database connections for generated Inquiry stores.
/// </summary>
public interface IInquiryConnectionFactory
{
    /// <summary>
    /// Opens a database connection.
    /// </summary>
    ValueTask<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken = default);
}
