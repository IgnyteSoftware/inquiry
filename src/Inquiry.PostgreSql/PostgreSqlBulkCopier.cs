using Inquiry.BulkCopy;
using Inquiry.Connections;
using Npgsql;
using NpgsqlTypes;
using System.Data;

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
        var npgsqlTypes = MapColumnTypes(definition);

        var rawConnection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        if (rawConnection is not NpgsqlConnection connection)
        {
            await rawConnection.DisposeAsync().ConfigureAwait(false);
            throw new InvalidOperationException(
                $"PostgreSQL bulk insert requires an NpgsqlConnection but received {rawConnection.GetType().Name}. " +
                "If using a connection wrapper, unwrap the inner connection first.");
        }

        await using (connection)
        await using (var writer = await connection.BeginBinaryImportAsync(sql, cancellationToken).ConfigureAwait(false))
        {
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
                    else if (npgsqlTypes is not null)
                    {
                        await writer.WriteAsync(value, npgsqlTypes[ordinal], cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        await writer.WriteAsync(value, cancellationToken).ConfigureAwait(false);
                    }
                }
            }

            var written = await writer.CompleteAsync(cancellationToken).ConfigureAwait(false);
            return (long)written;
        }
    }

    private static NpgsqlDbType[]? MapColumnTypes<TEntity>(InquiryBulkInsertDefinition<TEntity> definition)
        where TEntity : class
    {
        if (definition.ColumnTypes is not { } dbTypes)
            return null;

        var result = new NpgsqlDbType[dbTypes.Count];
        for (var i = 0; i < dbTypes.Count; i++)
        {
            result[i] = dbTypes[i] switch
            {
                DbType.Boolean => NpgsqlDbType.Boolean,
                DbType.Byte => NpgsqlDbType.Smallint,
                DbType.Int16 => NpgsqlDbType.Smallint,
                DbType.Int32 => NpgsqlDbType.Integer,
                DbType.Int64 => NpgsqlDbType.Bigint,
                DbType.Single => NpgsqlDbType.Real,
                DbType.Double => NpgsqlDbType.Double,
                DbType.Decimal => NpgsqlDbType.Numeric,
                DbType.Currency => NpgsqlDbType.Money,
                DbType.String => NpgsqlDbType.Text,
                DbType.AnsiString => NpgsqlDbType.Text,
                DbType.StringFixedLength => NpgsqlDbType.Text,
                DbType.AnsiStringFixedLength => NpgsqlDbType.Text,
                DbType.DateTime => NpgsqlDbType.Timestamp,
                DbType.DateTime2 => NpgsqlDbType.Timestamp,
                DbType.DateTimeOffset => NpgsqlDbType.TimestampTz,
                DbType.Date => NpgsqlDbType.Date,
                DbType.Time => NpgsqlDbType.Time,
                DbType.Guid => NpgsqlDbType.Uuid,
                DbType.Binary => NpgsqlDbType.Bytea,
                _ => NpgsqlDbType.Unknown,
            };
        }
        return result;
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
