using Inquiry.BulkCopy;
using Inquiry.Connections;
using MySqlConnector;

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
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var bulkCopy = new MySqlBulkCopy((MySqlConnection)connection)
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
            return result.RowsInserted;
        }
        catch (Exception ex) when (IsLocalInfileDisabled(ex))
        {
            throw new InvalidOperationException(
                "MySQL bulk insert streams rows via LOAD DATA LOCAL INFILE, which is disabled. Enable it on " +
                "both sides: set AllowLoadLocalInfile=true in the connection string (Inquiry's MySQL connection " +
                "factory applies this automatically) and local_infile=1 on the server.",
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
    /// Matches the client- and server-side errors raised when <c>LOAD DATA LOCAL INFILE</c> support
    /// is switched off (e.g. MySQL error 3948 "Loading local data is disabled" or 1148 ER_NOT_ALLOWED_COMMAND).
    /// </summary>
    private static bool IsLocalInfileDisabled(Exception exception)
        => exception.Message.Contains("local data", StringComparison.OrdinalIgnoreCase)
            || exception.Message.Contains("LOAD DATA", StringComparison.OrdinalIgnoreCase);
}
