namespace Inquiry;

public sealed class InquiryCommandFactory : IInquiryCommandFactory
{
    private readonly IInquirySqlDialect _dialect;

    public InquiryCommandFactory(IInquirySqlDialect dialect)
    {
        _dialect = dialect;
    }

    public InquiryCommandPlan<TEntity> BuildFind<TEntity>(IInquiryEntityDescriptor<TEntity> descriptor)
    {
        EnsureSingleKey(descriptor, nameof(BuildFind));

        var key = descriptor.Keys[0];
        var parameterName = _dialect.CreateParameterName(key.PropertyName, 0);
        var sql = $"{BuildSelectPrefix(descriptor)} WHERE {_dialect.QuoteIdentifier(key.ColumnName)} = {parameterName}";

        return new InquiryCommandPlan<TEntity>(
            sql,
            new[] { new InquiryCommandParameter<TEntity>(parameterName, key) },
            descriptor.Properties);
    }

    public InquiryCommandPlan<TEntity> BuildSelect<TEntity>(IInquiryEntityDescriptor<TEntity> descriptor, InquiryQuery<TEntity>? query)
    {
        var sql = BuildSelectPrefix(descriptor);
        var queryParameters = Array.Empty<InquiryCommandParameter<TEntity>>();

        if (query is not null)
        {
            if (!string.IsNullOrWhiteSpace(query.WhereSql))
            {
                sql += $" WHERE {query.WhereSql}";
            }

            if (!string.IsNullOrWhiteSpace(query.OrderBySql))
            {
                sql += $" ORDER BY {query.OrderBySql}";
            }

            sql = _dialect.LimitOffset(sql, query.LimitCount, query.OffsetCount);
        }

        return new InquiryCommandPlan<TEntity>(sql, queryParameters, descriptor.Properties);
    }

    public InquiryCommandPlan<TEntity> BuildInsert<TEntity>(IInquiryEntityDescriptor<TEntity> descriptor)
    {
        var columns = descriptor.Properties.Where(property => property.IsInsertable).ToArray();
        if (columns.Length == 0)
        {
            throw new InquiryMappingException($"Entity '{typeof(TEntity).FullName}' has no insertable columns.");
        }

        var parameters = columns
            .Select((property, index) => new InquiryCommandParameter<TEntity>(_dialect.CreateParameterName(property.PropertyName, index), property))
            .ToArray();
        var columnSql = string.Join(", ", columns.Select(property => _dialect.QuoteIdentifier(property.ColumnName)));
        var parameterSql = string.Join(", ", parameters.Select(parameter => parameter.Name));
        var sql = $"INSERT INTO {_dialect.FormatTableName(descriptor.Schema, descriptor.TableName)} ({columnSql}) VALUES ({parameterSql})";

        return new InquiryCommandPlan<TEntity>(sql, parameters, descriptor.Properties);
    }

    public InquiryCommandPlan<TEntity> BuildUpdate<TEntity>(IInquiryEntityDescriptor<TEntity> descriptor, IReadOnlyList<string>? propertyNames = null)
    {
        EnsureHasKeys(descriptor, nameof(BuildUpdate));

        var filter = propertyNames is null
            ? null
            : new HashSet<string>(propertyNames, StringComparer.OrdinalIgnoreCase);
        var setColumns = descriptor.Properties
            .Where(property => property.IsUpdateable && (filter is null || filter.Contains(property.PropertyName)))
            .ToArray();

        if (setColumns.Length == 0)
        {
            throw new InquiryMappingException($"Entity '{typeof(TEntity).FullName}' has no updateable columns.");
        }

        var parameters = new List<InquiryCommandParameter<TEntity>>(setColumns.Length + descriptor.Keys.Count + 1);
        var setSql = setColumns
            .Select(property =>
            {
                var parameter = new InquiryCommandParameter<TEntity>(_dialect.CreateParameterName(property.PropertyName, parameters.Count), property);
                parameters.Add(parameter);
                return $"{_dialect.QuoteIdentifier(property.ColumnName)} = {parameter.Name}";
            });
        var whereSql = BuildWhereByKeyAndConcurrency(descriptor, parameters);
        var sql = $"UPDATE {_dialect.FormatTableName(descriptor.Schema, descriptor.TableName)} SET {string.Join(", ", setSql)} WHERE {whereSql}";

        return new InquiryCommandPlan<TEntity>(sql, parameters, descriptor.Properties);
    }

    public InquiryCommandPlan<TEntity> BuildDelete<TEntity>(IInquiryEntityDescriptor<TEntity> descriptor)
    {
        EnsureHasKeys(descriptor, nameof(BuildDelete));

        var parameters = new List<InquiryCommandParameter<TEntity>>(descriptor.Keys.Count);
        var whereSql = BuildWhereByKeyAndConcurrency(descriptor, parameters);
        var sql = $"DELETE FROM {_dialect.FormatTableName(descriptor.Schema, descriptor.TableName)} WHERE {whereSql}";

        return new InquiryCommandPlan<TEntity>(sql, parameters, descriptor.Properties);
    }

    public InquiryCommandPlan<TEntity> BuildUpsert<TEntity>(IInquiryEntityDescriptor<TEntity> descriptor)
    {
        EnsureHasKeys(descriptor, nameof(BuildUpsert));

        var upsertColumns = descriptor.Properties
            .Where(property => property.IsInsertable || property.IsKey)
            .ToArray();

        if (upsertColumns.Length == 0)
        {
            throw new InquiryMappingException($"Entity '{typeof(TEntity).FullName}' has no upsert columns.");
        }

        var parameters = upsertColumns
            .Select((property, index) => new InquiryCommandParameter<TEntity>(_dialect.CreateParameterName(property.PropertyName, index), property))
            .ToArray();

        return new InquiryCommandPlan<TEntity>(_dialect.BuildUpsert(descriptor), parameters, descriptor.Properties);
    }

    private string BuildSelectPrefix<TEntity>(IInquiryEntityDescriptor<TEntity> descriptor)
    {
        var columns = string.Join(", ", descriptor.Properties.Select(property => _dialect.QuoteIdentifier(property.ColumnName)));
        return $"SELECT {columns} FROM {_dialect.FormatTableName(descriptor.Schema, descriptor.TableName)}";
    }

    private string BuildWhereByKeyAndConcurrency<TEntity>(
        IInquiryEntityDescriptor<TEntity> descriptor,
        List<InquiryCommandParameter<TEntity>> parameters)
    {
        var predicates = new List<string>(descriptor.Keys.Count + 1);
        foreach (var key in descriptor.Keys)
        {
            var parameter = new InquiryCommandParameter<TEntity>(_dialect.CreateParameterName(key.PropertyName, parameters.Count), key);
            parameters.Add(parameter);
            predicates.Add($"{_dialect.QuoteIdentifier(key.ColumnName)} = {parameter.Name}");
        }

        if (descriptor.ConcurrencyToken is not null)
        {
            var parameter = new InquiryCommandParameter<TEntity>(
                _dialect.CreateParameterName(descriptor.ConcurrencyToken.PropertyName, parameters.Count),
                descriptor.ConcurrencyToken);
            parameters.Add(parameter);
            predicates.Add($"{_dialect.QuoteIdentifier(descriptor.ConcurrencyToken.ColumnName)} = {parameter.Name}");
        }

        return string.Join(" AND ", predicates);
    }

    private static void EnsureSingleKey<TEntity>(IInquiryEntityDescriptor<TEntity> descriptor, string operation)
    {
        EnsureHasKeys(descriptor, operation);
        if (descriptor.Keys.Count != 1)
        {
            throw new InquiryMappingException(
                $"Operation '{operation}' requires entity '{typeof(TEntity).FullName}' to have exactly one key.");
        }
    }

    private static void EnsureHasKeys<TEntity>(IInquiryEntityDescriptor<TEntity> descriptor, string operation)
    {
        if (descriptor.Keys.Count == 0)
        {
            throw new InquiryMappingException(
                $"Operation '{operation}' requires entity '{typeof(TEntity).FullName}' to define at least one [InquiryKey].");
        }
    }
}
