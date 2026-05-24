using Inquiry.Commands;
using System.Data.Common;

namespace Inquiry.Pipeline;

/// <summary>
/// Executes Inquiry database requests.
/// </summary>
public interface IInquiryRequestPipeline
{
    /// <summary>
    /// Executes a query and streams materialized rows.
    /// </summary>
    IAsyncEnumerable<T> QueryAsync<T>(
        InquiryCommand command,
        Func<DbDataReader, T> materialize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a query and returns the first materialized row, or <see langword="null"/> when no row is returned.
    /// </summary>
    Task<T?> QuerySingleOrDefaultAsync<T>(
        InquiryCommand command,
        Func<DbDataReader, T> materialize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a non-query command and returns the affected row count.
    /// </summary>
    Task<int> ExecuteAsync(
        InquiryCommand command,
        CancellationToken cancellationToken = default);
}
