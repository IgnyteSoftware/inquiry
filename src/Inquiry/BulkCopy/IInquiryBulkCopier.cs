using System.Collections.Generic;
using System.ComponentModel;

namespace Inquiry.BulkCopy;

/// <summary>
/// Provider-native bulk-insert implementation (SqlBulkCopy / binary COPY / MySqlBulkCopy).
/// Registered by provider packages whose engine has a bulk-copy API; resolved by
/// <see cref="IInquiry.BulkInsertAsync{TEntity}"/>. Generated stores on dialects without one fall
/// back to batch SQL at compile time and never resolve this service.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IInquiryBulkCopier
{
    /// <summary>
    /// Streams <paramref name="rows"/> into the table described by <paramref name="definition"/>
    /// on a dedicated connection and returns the number of rows written.
    /// </summary>
    Task<long> BulkInsertAsync<TEntity>(
        InquiryBulkInsertDefinition<TEntity> definition,
        IEnumerable<TEntity> rows,
        CancellationToken cancellationToken = default)
        where TEntity : class;
}
