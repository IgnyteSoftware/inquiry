using Inquiry.BulkCopy;
using Inquiry.Connections;
using Microsoft.Data.SqlClient;
using System.Data.Common;
using System.Diagnostics;

namespace Inquiry.SqlServer;

/// <summary>
/// SQL Server <see cref="IInquiryBulkCopier"/>: streams rows into the destination table through
/// <see cref="SqlBulkCopy"/> on the ambient transaction connection when present, or on a dedicated
/// connection opened from the registered <see cref="IInquiryConnectionFactory"/>.
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
        InquiryBulkInsertContext context,
        CancellationToken cancellationToken = default)
        where TEntity : class
    {
        DbConnection? dedicatedConnection = null;
        var rawConnection = context.Connection;
        if (rawConnection is null)
        {
            var openTimestamp = Stopwatch.GetTimestamp();
            try
            {
                rawConnection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
                dedicatedConnection = rawConnection;
            }
            finally
            {
                context.RecordConnectionOpened(Stopwatch.GetElapsedTime(openTimestamp));
            }
        }

        if (rawConnection is not SqlConnection sqlConnection)
        {
            if (dedicatedConnection is not null) await dedicatedConnection.DisposeAsync().ConfigureAwait(false);
            throw new InvalidOperationException(
                $"SQL Server bulk insert requires a SqlConnection but received {rawConnection.GetType().Name}. " +
                "If using a connection wrapper, unwrap the inner connection first.");
        }
        if (context.Transaction is not null and not SqlTransaction)
            throw new InvalidOperationException($"SQL Server bulk insert requires a SqlTransaction but received {context.Transaction.GetType().Name}.");

        try
        {
            var options = context.Options;
            var copyOptions = options.TableLock ? SqlBulkCopyOptions.TableLock : SqlBulkCopyOptions.Default;
            using var bulk = new SqlBulkCopy(sqlConnection, copyOptions, (SqlTransaction?)context.Transaction)
            {
                DestinationTableName = QualifiedTableName(definition.Schema, definition.Table),
                EnableStreaming = true,
            };
            if (options.Timeout is { } timeout) bulk.BulkCopyTimeout = (int)Math.Ceiling(timeout.TotalSeconds);
            if (options.BatchSize is { } batchSize) bulk.BatchSize = batchSize;
            if (options.NotifyAfter is { } notifyAfter) bulk.NotifyAfter = notifyAfter;
            if (options.RowsCopied is { } rowsCopied)
                bulk.SqlRowsCopied += (_, args) => rowsCopied(args.RowsCopied);

            // Map each source ordinal to the destination column NAME, never positionally: the
            // destination table may carry columns the definition omits (e.g. an IDENTITY key), so
            // positional mapping would misalign the copy.
            for (var i = 0; i < definition.Columns.Count; i++)
            {
                bulk.ColumnMappings.Add(i, definition.Columns[i]);
            }

            using var reader = new InquiryBulkRowReader<TEntity>(definition, rows);
            var copyTimestamp = Stopwatch.GetTimestamp();
            try
            {
                await bulk.WriteToServerAsync(reader, cancellationToken).ConfigureAwait(false);
                context.RecordCopyCompleted(Stopwatch.GetElapsedTime(copyTimestamp), reader.RowsRead);
                return reader.RowsRead;
            }
            catch
            {
                context.RecordCopyCompleted(Stopwatch.GetElapsedTime(copyTimestamp));
                throw;
            }
        }
        finally
        {
            if (dedicatedConnection is not null) await dedicatedConnection.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static string QualifiedTableName(string? schema, string table)
        => schema is null ? Quote(table) : Quote(schema) + "." + Quote(table);

    private static string Quote(string identifier)
        => "[" + identifier.Replace("]", "]]") + "]";
}
