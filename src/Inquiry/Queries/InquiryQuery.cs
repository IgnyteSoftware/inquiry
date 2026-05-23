namespace Inquiry;

public sealed class InquiryQuery<TEntity>
{
    private readonly Dictionary<string, object?> _parameters = new(StringComparer.OrdinalIgnoreCase);

    public string? WhereSql { get; private set; }

    public string? OrderBySql { get; private set; }

    public int? LimitCount { get; private set; }

    public int? OffsetCount { get; private set; }

    public IReadOnlyDictionary<string, object?> Parameters => _parameters;

    public InquiryQuery<TEntity> Where(string sql, object? parameters = null)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            throw new ArgumentException("WHERE SQL cannot be empty.", nameof(sql));
        }

        WhereSql = WhereSql is null ? sql : $"({WhereSql}) AND ({sql})";
        AddParameters(parameters);
        return this;
    }

    public InquiryQuery<TEntity> OrderBy(string columnOrExpression)
    {
        if (string.IsNullOrWhiteSpace(columnOrExpression))
        {
            throw new ArgumentException("ORDER BY expression cannot be empty.", nameof(columnOrExpression));
        }

        OrderBySql = columnOrExpression;
        return this;
    }

    public InquiryQuery<TEntity> Limit(int count)
    {
        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "Limit cannot be negative.");
        }

        LimitCount = count;
        return this;
    }

    public InquiryQuery<TEntity> Offset(int count)
    {
        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "Offset cannot be negative.");
        }

        OffsetCount = count;
        return this;
    }

    private void AddParameters(object? parameters)
    {
        foreach (var parameter in InquiryParameterReader.Read(parameters))
        {
            _parameters[parameter.Key] = parameter.Value;
        }
    }
}
