using Inquiry.BulkCopy;
using Inquiry.Connections;
using MySqlConnector;
using System.Data.Common;
using System.Diagnostics;
using System.Linq;

namespace Inquiry.MySql;

/// <summary>
/// Bulk-insert implementation backed by MySqlConnector's <see cref="MySqlBulkCopy"/>, which streams
/// rows to the server via <c>LOAD DATA LOCAL INFILE</c>.
/// </summary>
internal sealed class MySqlBulkCopier : IInquiryBulkCopier
{
    private readonly IInquiryConnectionFactory _connectionFactory;

    public MySqlBulkCopier(IInquiryConnectionFactory connectionFactory)
        => _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));

    /// <inheritdoc />
    public async Task<long> BulkInsertAsync<TEntity>(
        InquiryBulkInsertDefinition<TEntity> definition,
        IEnumerable<TEntity> rows,
        InquiryBulkInsertContext context,
        CancellationToken cancellationToken = default)
        where TEntity : class
    {
        if (context.Options.BatchSize is not null)
            throw new InvalidOperationException("MySQL bulk insert cannot honor option BatchSize before writing any rows.");
        if (context.Options.TableLock)
            throw new InvalidOperationException("MySQL bulk insert cannot honor option TableLock before writing any rows.");

        DbConnection? dedicatedConnection = null;
        var rawConnection = context.Connection;
        if (rawConnection is null)
        {
            var openTimestamp = Stopwatch.GetTimestamp();
            try
            {
                rawConnection = _connectionFactory is MySqlInquiryConnectionFactory mysqlFactory
                    ? await mysqlFactory.OpenBulkCopyConnectionAsync(cancellationToken).ConfigureAwait(false)
                    : await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
                dedicatedConnection = rawConnection;
            }
            finally
            {
                context.RecordConnectionOpened(Stopwatch.GetElapsedTime(openTimestamp));
            }
        }

        if (rawConnection is not MySqlConnection mysqlConnection)
        {
            if (dedicatedConnection is not null) await dedicatedConnection.DisposeAsync().ConfigureAwait(false);
            throw new InvalidOperationException(
                $"MySQL bulk insert requires a MySqlConnection but received {rawConnection.GetType().Name}. " +
                "If using a connection wrapper, unwrap the inner connection first.");
        }
        if (context.Transaction is not null and not MySqlTransaction)
            throw new InvalidOperationException($"MySQL bulk insert requires a MySqlTransaction but received {context.Transaction.GetType().Name}.");
        if (context.IsEnlisted && !new MySqlConnectionStringBuilder(mysqlConnection.ConnectionString).AllowLoadLocalInfile)
            throw new InvalidOperationException(
                "MySQL bulk insert cannot enlist in this Inquiry transaction because its connection does not enable " +
                "AllowLoadLocalInfile. Inquiry deliberately enables that security-sensitive setting only on dedicated " +
                "bulk-copy connections. Use [InquiryInsert] with a collection for transaction-bound inserts.");

        try
        {
            var bulkCopy = new MySqlBulkCopy(mysqlConnection, (MySqlTransaction?)context.Transaction)
            {
                DestinationTableName = QualifyTableName(definition.Schema, definition.Table),
            };
            if (context.Options.Timeout is { } timeout) bulkCopy.BulkCopyTimeout = (int)Math.Ceiling(timeout.TotalSeconds);
            if (context.Options.NotifyAfter is { } notifyAfter) bulkCopy.NotifyAfter = notifyAfter;
            if (context.Options.RowsCopied is { } rowsCopied)
                bulkCopy.MySqlRowsCopied += (_, args) => rowsCopied(args.RowsCopied);

            for (var i = 0; i < definition.Columns.Count; i++)
            {
                bulkCopy.ColumnMappings.Add(new MySqlBulkCopyColumnMapping(i, definition.Columns[i]));
            }

            using var reader = new InquiryBulkRowReader<TEntity>(definition, rows);
            var copyTimestamp = Stopwatch.GetTimestamp();
            try
            {
                var result = await bulkCopy.WriteToServerAsync(reader, cancellationToken).ConfigureAwait(false);
                if (result.Warnings.Count > 0)
                {
                    throw new InvalidOperationException(
                        "MySQL bulk insert completed with warnings (rows were written but may contain truncated data): " +
                        string.Join("; ", result.Warnings.Select(w => w.Message)));
                }
                context.RecordCopyCompleted(Stopwatch.GetElapsedTime(copyTimestamp), result.RowsInserted);
                return result.RowsInserted;
            }
            catch (Exception exception) when (IsLocalInfileDisabled(exception))
            {
                context.RecordCopyCompleted(Stopwatch.GetElapsedTime(copyTimestamp));
                throw new InvalidOperationException(
                    "MySQL bulk insert streams rows via LOAD DATA LOCAL INFILE, which is disabled. Inquiry's MySQL " +
                    "provider enables the client side automatically on its dedicated bulk-insert connection; the " +
                    "server must allow it too (local_infile=1). With a custom connection factory, also set " +
                    "AllowLoadLocalInfile=true on the connection string used for bulk inserts.",
                    exception);
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

    private static string QualifyTableName(string? schema, string table)
        => schema is null ? Quote(table) : Quote(schema) + "." + Quote(table);

    private static string Quote(string identifier)
        => "`" + identifier.Replace("`", "``") + "`";

    private static bool IsLocalInfileDisabled(Exception exception)
        => exception is MySqlException { Number: 1148 or 3948 }
            || exception.Message.Contains("AllowLoadLocalInfile", StringComparison.OrdinalIgnoreCase);
}
