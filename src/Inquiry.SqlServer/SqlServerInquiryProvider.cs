using System.Data.Common;

namespace Inquiry;

public static class InquirySqlServerProvider
{
    public static IInquiryProvider Instance { get; } = new InquiryProvider("sqlserver", new SqlServerInquirySqlDialect());
}

public sealed class SqlServerInquirySqlDialect : InquirySqlDialect
{
    public override string QuoteIdentifier(string identifier)
    {
        return $"[{EscapeSquareBracket(identifier)}]";
    }

    public override string LimitOffset(string sql, int? limit, int? offset)
    {
        if (limit is null && offset is null)
        {
            return sql;
        }

        var actualOffset = offset ?? 0;
        var actualLimit = limit ?? int.MaxValue;
        return $"{sql} OFFSET {actualOffset} ROWS FETCH NEXT {actualLimit} ROWS ONLY";
    }

    public override string BuildUpsert<TEntity>(IInquiryEntityDescriptor<TEntity> descriptor)
    {
        var upsertColumns = descriptor.Properties
            .Where(property => property.IsInsertable || property.IsKey)
            .ToArray();
        var updateColumns = descriptor.Properties
            .Where(property => property.IsUpdateable)
            .ToArray();
        var sourceColumns = string.Join(
            ", ",
            upsertColumns.Select((property, index) => $"{CreateParameterName(property.PropertyName, index)} AS {QuoteIdentifier(property.ColumnName)}"));
        var keyPredicate = string.Join(
            " AND ",
            descriptor.Keys.Select(property => $"target.{QuoteIdentifier(property.ColumnName)} = source.{QuoteIdentifier(property.ColumnName)}"));
        var insertColumns = string.Join(", ", upsertColumns.Select(property => QuoteIdentifier(property.ColumnName)));
        var insertValues = string.Join(", ", upsertColumns.Select(property => $"source.{QuoteIdentifier(property.ColumnName)}"));
        var updateSql = updateColumns.Length == 0
            ? string.Empty
            : $"WHEN MATCHED THEN UPDATE SET {string.Join(", ", updateColumns.Select(property => $"target.{QuoteIdentifier(property.ColumnName)} = source.{QuoteIdentifier(property.ColumnName)}"))} ";

        return
            $"MERGE {FormatTableName(descriptor.Schema, descriptor.TableName)} AS target " +
            $"USING (SELECT {sourceColumns}) AS source ON {keyPredicate} " +
            updateSql +
            $"WHEN NOT MATCHED THEN INSERT ({insertColumns}) VALUES ({insertValues});";
    }
}

public static class SqlServerInquiryOptionsExtensions
{
    public static InquiryOptions UseSqlServer(this InquiryOptions options)
    {
        return options.UseProvider(InquirySqlServerProvider.Instance);
    }

    public static InquiryOptions UseSqlServer(this InquiryOptions options, Func<DbConnection> connectionFactory, bool ownsConnection = true)
    {
        options.UseProvider(InquirySqlServerProvider.Instance);
        options.UseConnectionFactory(connectionFactory, ownsConnection);
        return options;
    }

    public static InquiryOptions UseSqlServer(this InquiryOptions options, DbConnection connection)
    {
        options.UseProvider(InquirySqlServerProvider.Instance);
        options.UseConnection(connection);
        return options;
    }
}
