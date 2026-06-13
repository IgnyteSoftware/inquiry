using Inquiry.BulkCopy;
using Inquiry.Connections;
using Npgsql;

namespace Inquiry.PostgreSql;

/// <summary>
/// <see cref="IInquiryBulkCopier"/> for PostgreSQL: streams rows into the target table through
/// Npgsql's binary <c>COPY ... FROM STDIN</c> protocol on a dedicated connection.
/// </summary>
internal sealed class PostgreSqlBulkCopier : IInquiryBulkCopier
{
    private readonly IInquiryConnectionFactory _connectionFactory;

    /// <summary>Initializes the copier over the registered connection factory.</summary>
    public PostgreSqlBulkCopier(IInquiryConnectionFactory connectionFactory)
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
        var columnCount = definition.Columns.Count;
        var sql = BuildCopyCommand(definition);

        await using var connection = (NpgsqlConnection)await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var writer = await connection.BeginBinaryImportAsync(sql, cancellationToken).ConfigureAwait(false);

        foreach (var row in rows)
        {
            await writer.StartRowAsync(cancellationToken).ConfigureAwait(false);
            for (var ordinal = 0; ordinal < columnCount; ordinal++)
            {
                var value = definition.GetValue(row, ordinal);
                if (value is DBNull)
                {
                    await writer.WriteNullAsync(cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    // Untyped write: the definition supplies provider primitives (long, string,
                    // decimal, Guid, DateTime, ...), so Npgsql infers the handler per value.
                    await writer.WriteAsync(value, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        var written = await writer.CompleteAsync(cancellationToken).ConfigureAwait(false);
        return (long)written;
    }

    private static string BuildCopyCommand<TEntity>(InquiryBulkInsertDefinition<TEntity> definition)
        where TEntity : class
    {
        var table = definition.Schema is null
            ? QuoteIdentifier(definition.Table)
            : QuoteIdentifier(definition.Schema) + "." + QuoteIdentifier(definition.Table);
        var columns = string.Join(", ", definition.Columns.Select(QuoteIdentifier));
        return "COPY " + table + " (" + columns + ") FROM STDIN (FORMAT BINARY)";
    }

    // Case-preserving PostgreSQL identifier quoting; matches PostgreSqlSqlBuilder.QuoteIdentifier.
    private static string QuoteIdentifier(string identifier)
        => "\"" + identifier.Replace("\"", "\"\"") + "\"";
}
