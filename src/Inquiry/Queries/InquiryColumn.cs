namespace Inquiry;

public readonly struct InquiryColumn
{
    public InquiryColumn(string name)
    {
        Name = string.IsNullOrWhiteSpace(name)
            ? throw new ArgumentException("Column name cannot be empty.", nameof(name))
            : name;
    }

    public string Name { get; }

    public string Asc()
    {
        return $"{Name} ASC";
    }

    public string Desc()
    {
        return $"{Name} DESC";
    }

    public InquirySqlCondition Equal(object? value)
    {
        return new InquirySqlCondition($"{Name} = @{Name}", new Dictionary<string, object?> { [Name] = value });
    }
}

public readonly struct InquirySqlCondition
{
    public InquirySqlCondition(string sql, IReadOnlyDictionary<string, object?> parameters)
    {
        Sql = sql ?? throw new ArgumentNullException(nameof(sql));
        Parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
    }

    public string Sql { get; }

    public IReadOnlyDictionary<string, object?> Parameters { get; }
}

public static class InquiryQueryExtensions
{
    public static InquiryQuery<TEntity> Where<TEntity>(this InquiryQuery<TEntity> query, InquirySqlCondition condition)
    {
        return query.Where(condition.Sql, condition.Parameters);
    }
}
