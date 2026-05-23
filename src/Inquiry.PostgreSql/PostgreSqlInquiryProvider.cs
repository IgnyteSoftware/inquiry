using System.Data.Common;

namespace Inquiry;

public static class InquiryPostgreSqlProvider
{
    public static IInquiryProvider Instance { get; } = new InquiryProvider("postgresql", new PostgreSqlInquirySqlDialect());
}

public sealed class PostgreSqlInquirySqlDialect : InquirySqlDialect
{
    public override string QuoteIdentifier(string identifier)
    {
        return $"\"{EscapeDoubleQuote(identifier)}\"";
    }

    public override string BuildUpsert<TEntity>(IInquiryEntityDescriptor<TEntity> descriptor)
    {
        var insertColumns = descriptor.Properties
            .Where(property => property.IsInsertable || property.IsKey)
            .ToArray();
        var updateColumns = descriptor.Properties
            .Where(property => property.IsUpdateable)
            .ToArray();
        var tableName = FormatTableName(descriptor.Schema, descriptor.TableName);
        var columns = string.Join(", ", insertColumns.Select(property => QuoteIdentifier(property.ColumnName)));
        var values = string.Join(", ", insertColumns.Select((property, index) => CreateParameterName(property.PropertyName, index)));
        var conflictColumns = string.Join(", ", descriptor.Keys.Select(property => QuoteIdentifier(property.ColumnName)));

        return updateColumns.Length == 0
            ? $"INSERT INTO {tableName} ({columns}) VALUES ({values}) ON CONFLICT ({conflictColumns}) DO NOTHING"
            : $"INSERT INTO {tableName} ({columns}) VALUES ({values}) ON CONFLICT ({conflictColumns}) DO UPDATE SET {string.Join(", ", updateColumns.Select(property => $"{QuoteIdentifier(property.ColumnName)} = excluded.{QuoteIdentifier(property.ColumnName)}"))}";
    }
}

public static class PostgreSqlInquiryOptionsExtensions
{
    public static InquiryOptions UsePostgreSql(this InquiryOptions options)
    {
        return options.UseProvider(InquiryPostgreSqlProvider.Instance);
    }

    public static InquiryOptions UsePostgreSql(this InquiryOptions options, Func<DbConnection> connectionFactory, bool ownsConnection = true)
    {
        options.UseProvider(InquiryPostgreSqlProvider.Instance);
        options.UseConnectionFactory(connectionFactory, ownsConnection);
        return options;
    }

    public static InquiryOptions UsePostgreSql(this InquiryOptions options, DbConnection connection)
    {
        options.UseProvider(InquiryPostgreSqlProvider.Instance);
        options.UseConnection(connection);
        return options;
    }
}
