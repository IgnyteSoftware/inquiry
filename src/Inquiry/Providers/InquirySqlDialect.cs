using System.Text;

namespace Inquiry;

public abstract class InquirySqlDialect : IInquirySqlDialect
{
    public abstract string QuoteIdentifier(string identifier);

    public virtual string FormatTableName(string? schema, string table)
    {
        return string.IsNullOrWhiteSpace(schema)
            ? QuoteIdentifier(table)
            : $"{QuoteIdentifier(schema)}.{QuoteIdentifier(table)}";
    }

    public virtual string CreateParameterName(string logicalName, int ordinal)
    {
        if (string.IsNullOrWhiteSpace(logicalName))
        {
            logicalName = $"p{ordinal}";
        }

        var normalized = new string(logicalName.Where(char.IsLetterOrDigit).ToArray());
        if (string.IsNullOrWhiteSpace(normalized))
        {
            normalized = $"p{ordinal}";
        }

        return $"@{normalized}";
    }

    public virtual string LimitOffset(string sql, int? limit, int? offset)
    {
        if (limit is null && offset is null)
        {
            return sql;
        }

        var builder = new StringBuilder(sql);
        if (limit is not null)
        {
            builder.Append(" LIMIT ").Append(limit.Value);
        }

        if (offset is not null)
        {
            if (limit is null)
            {
                builder.Append(" LIMIT -1");
            }

            builder.Append(" OFFSET ").Append(offset.Value);
        }

        return builder.ToString();
    }

    public virtual string BuildInsert<TEntity>(IInquiryEntityDescriptor<TEntity> descriptor)
    {
        return new InquiryCommandFactory(this).BuildInsert(descriptor).CommandText;
    }

    public virtual string BuildUpdate<TEntity>(IInquiryEntityDescriptor<TEntity> descriptor)
    {
        return new InquiryCommandFactory(this).BuildUpdate(descriptor).CommandText;
    }

    public virtual string BuildDelete<TEntity>(IInquiryEntityDescriptor<TEntity> descriptor)
    {
        return new InquiryCommandFactory(this).BuildDelete(descriptor).CommandText;
    }

    public virtual string BuildUpsert<TEntity>(IInquiryEntityDescriptor<TEntity> descriptor)
    {
        throw new InquiryProviderException(
            $"Provider dialect '{GetType().FullName}' does not implement upsert SQL generation.");
    }

    protected static string EscapeDoubleQuote(string identifier)
    {
        return identifier.Replace("\"", "\"\"", StringComparison.Ordinal);
    }

    protected static string EscapeSquareBracket(string identifier)
    {
        return identifier.Replace("]", "]]", StringComparison.Ordinal);
    }

    protected static string EscapeBacktick(string identifier)
    {
        return identifier.Replace("`", "``", StringComparison.Ordinal);
    }
}
