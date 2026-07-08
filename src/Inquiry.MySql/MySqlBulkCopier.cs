using Inquiry.BulkCopy;
using Inquiry.Connections;
using MySqlConnector;
using System.Linq;

namespace Inquiry.MySql;

/// <summary>
/// Bulk-insert implementation backed by MySqlConnector's <see cref="MySqlBulkCopy"/>, which streams
/// rows to the server via <c>LOAD DATA LOCAL INFILE</c>. Resolved by the core pipeline for
/// <c>[InquiryBulkInsert]</c> store methods.
/// </summary>
internal sealed class MySqlBulkCopier : IInquiryBulkCopier
{
    private readonly IInquiryConnectionFactory _connectionFactory;

    /// <summary>
    /// Initializes a new instance of <see cref="MySqlBulkCopier"/>.
    /// </summary>
    public MySqlBulkCopier(IInquiryConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    /// <inheritdoc />
    public async Task<long> BulkInsertAsync<TEntity>(
        InquiryBulkInsertDefinition<TEntity> definition,
        IEnumerable<TEntity> rows,
        CancellationToken cancellationToken = default)
        where TEntity : class
    {
        // The bulk connection alone carries AllowLoadLocalInfile (security-scoped — see the
        // factory); a custom factory falls back to its regular connection, where the actionable
        // error below explains the missing client flag.
        await using var connection = _connectionFactory is MySqlInquiryConnectionFactory mysqlFactory
            ? await mysqlFactory.OpenBulkCopyConnectionAsync(cancellationToken).ConfigureAwait(false)
            : await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        if (connection is not MySqlConnection mysqlConnection)
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw new InvalidOperationException(
                $"MySQL bulk insert requires a MySqlConnection but received {connection.GetType().Name}. " +
                "If using a connection wrapper, unwrap the inner connection first.");
        }

        var bulkCopy = new MySqlBulkCopy(mysqlConnection)
        {
            DestinationTableName = QualifyTableName(definition.Schema, definition.Table),
        };

        // The destination table can have columns the definition omits (the AUTO_INCREMENT key), so
        // positional mapping would shift values into the wrong columns. Map each source ordinal to
        // its destination column by name instead.
        for (var i = 0; i < definition.Columns.Count; i++)
        {
            bulkCopy.ColumnMappings.Add(new MySqlBulkCopyColumnMapping(i, definition.Columns[i]));
        }

        using var reader = new InquiryBulkRowReader<TEntity>(definition, rows);
        try
        {
            var result = await bulkCopy.WriteToServerAsync(reader, cancellationToken).ConfigureAwait(false);
            if (result.Warnings.Count > 0)
            {
                throw new InvalidOperationException(
                    "MySQL bulk insert completed with warnings (rows were written but may contain truncated data): " +
                    string.Join("; ", result.Warnings.Select(w => w.Message)));
            }
            return result.RowsInserted;
        }
        catch (Exception ex) when (IsLocalInfileDisabled(ex))
        {
            throw new InvalidOperationException(
                "MySQL bulk insert streams rows via LOAD DATA LOCAL INFILE, which is disabled. Inquiry's MySQL " +
                "provider enables the client side automatically on its dedicated bulk-insert connection; the " +
                "server must allow it too (local_infile=1). With a custom connection factory, also set " +
                "AllowLoadLocalInfile=true on the connection string used for bulk inserts.",
                ex);
        }
    }

    /// <summary>
    /// Backtick-quotes the destination name; <see cref="MySqlBulkCopy.DestinationTableName"/> is
    /// pasted verbatim into the generated <c>LOAD DATA</c> statement.
    /// </summary>
    private static string QualifyTableName(string? schema, string table)
        => schema is null ? Quote(table) : Quote(schema) + "." + Quote(table);

    private static string Quote(string identifier)
        => "`" + identifier.Replace("`", "``") + "`";

    /// <summary>
    /// Matches the errors raised when <c>LOAD DATA LOCAL INFILE</c> support is switched off:
    /// server-side by error number — 3948 "Loading local data is disabled" and 1148
    /// ER_NOT_ALLOWED_COMMAND — plus MySqlConnector's client-side refusal, which names the
    /// <c>AllowLoadLocalInfile</c> setting. Keying off the number (not message text) avoids
    /// misclassifying unrelated failures behind the actionable message.
    /// </summary>
    private static bool IsLocalInfileDisabled(Exception exception)
        => exception is MySqlException { Number: 1148 or 3948 }
            || exception.Message.Contains("AllowLoadLocalInfile", StringComparison.OrdinalIgnoreCase);
}
