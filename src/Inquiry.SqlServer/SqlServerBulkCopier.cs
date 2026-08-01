using Inquiry.BulkCopy;
using Inquiry.Connections;
using Microsoft.Data.SqlClient;

namespace Inquiry.SqlServer;

/// <summary>
/// SQL Server <see cref="IInquiryBulkCopier"/>: streams rows into the destination table through
/// <see cref="SqlBulkCopy"/> on a dedicated connection opened from the registered
/// <see cref="IInquiryConnectionFactory"/>.
/// </summary>
internal sealed class SqlServerBulkCopier : IInquiryBulkCopier
{
    private readonly IInquiryConnectionFactory _connectionFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqlServerBulkCopier"/> class.
    /// </summary>
    public SqlServerBulkCopier(IInquiryConnectionFactory connectionFactory)
        => _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));

    /// <inheritdoc />
    public async Task<long> BulkInsertAsync<TEntity>(
        InquiryBulkInsertDefinition<TEntity> definition,
        IEnumerable<TEntity> rows,
        CancellationToken cancellationToken = default)
        where TEntity : class
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        if (connection is not SqlConnection sqlConnection)
        {
            throw new InvalidOperationException(
                $"SQL Server bulk insert requires a SqlConnection but received {connection.GetType().Name}. " +
                "If using a connection wrapper, unwrap the inner connection first.");
        }

        using var bulk = new SqlBulkCopy(sqlConnection)
        {
            DestinationTableName = QualifiedTableName(definition.Schema, definition.Table),
            EnableStreaming = true,
        };

        // Map each source ordinal to the destination column NAME, never positionally: the
        // destination table may carry columns the definition omits (e.g. an IDENTITY key), so
        // positional mapping would misalign the copy.
        for (var i = 0; i < definition.Columns.Count; i++)
        {
            bulk.ColumnMappings.Add(i, definition.Columns[i]);
        }

        using var reader = new InquiryBulkRowReader<TEntity>(definition, rows);
        await bulk.WriteToServerAsync(reader, cancellationToken).ConfigureAwait(false);
        return reader.RowsRead;
    }

    private static string QualifiedTableName(string? schema, string table)
        => schema is null ? Quote(table) : Quote(schema) + "." + Quote(table);

    private static string Quote(string identifier)
        => "[" + identifier.Replace("]", "]]") + "]";
}
