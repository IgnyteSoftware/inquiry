using System.Collections;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Inquiry;

public sealed class InquiryClient : IInquiryClient
{
    private readonly Func<IServiceProvider?, CancellationToken, ValueTask<DbConnection>> _connectionFactory;
    private readonly bool _ownsConnections;
    private readonly DbTransaction? _transaction;
    private readonly IInquiryProvider _provider;
    private readonly InquiryMetadataRegistry _metadata;
    private readonly IReadOnlyList<IInquiryMiddleware> _middleware;
    private readonly IServiceProvider? _services;

    public InquiryClient(
        Func<IServiceProvider?, CancellationToken, ValueTask<DbConnection>> connectionFactory,
        IInquiryProvider provider,
        InquiryMetadataRegistry? metadata = null,
        IEnumerable<IInquiryMiddleware>? middleware = null,
        IServiceProvider? services = null,
        bool ownsConnections = true,
        DbTransaction? transaction = null)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _metadata = metadata ?? new InquiryMetadataRegistry();
        _middleware = middleware?.ToArray() ?? Array.Empty<IInquiryMiddleware>();
        _services = services;
        _ownsConnections = ownsConnections;
        _transaction = transaction;
    }

    public static InquiryClient Create(DbConnection connection, IInquiryProvider provider)
    {
        ArgumentNullException.ThrowIfNull(connection);
        return new InquiryClient((_, _) => ValueTask.FromResult(connection), provider, ownsConnections: false);
    }

    public async Task<TEntity?> FindAsync<TEntity, TKey>(TKey key, CancellationToken cancellationToken = default)
    {
        var descriptor = _metadata.GetDescriptor<TEntity>();
        var plan = _provider.CommandFactory.BuildFind(descriptor);
        var parameterValues = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            [plan.Parameters[0].Name] = key
        };

        var results = await ExecuteReaderAsync(
            InquiryOperation.Find,
            typeof(TEntity),
            plan.CommandText,
            parameterValues,
            reader => MaterializeByOrdinal(descriptor, reader),
            singleRow: true,
            cancellationToken).ConfigureAwait(false);

        return results.Count == 0 ? default : results[0];
    }

    public Task<IReadOnlyList<TEntity>> SelectAsync<TEntity>(
        InquiryQuery<TEntity>? query = null,
        CancellationToken cancellationToken = default)
    {
        var descriptor = _metadata.GetDescriptor<TEntity>();
        var plan = _provider.CommandFactory.BuildSelect(descriptor, query);
        return ExecuteReaderAsync(
            InquiryOperation.Select,
            typeof(TEntity),
            plan.CommandText,
            query?.Parameters,
            reader => MaterializeByOrdinal(descriptor, reader),
            singleRow: false,
            cancellationToken);
    }

    public Task<IReadOnlyList<TEntity>> SelectAsync<TEntity>(
        Func<InquiryQuery<TEntity>, InquiryQuery<TEntity>> configure,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var query = configure(new InquiryQuery<TEntity>());
        return SelectAsync(query, cancellationToken);
    }

    public async Task<TEntity?> FirstOrDefaultAsync<TEntity>(
        InquiryQuery<TEntity> query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        query.Limit(1);
        var list = await SelectAsync(query, cancellationToken).ConfigureAwait(false);
        return list.Count == 0 ? default : list[0];
    }

    public async Task<TEntity> SingleAsync<TEntity>(InquiryQuery<TEntity> query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        query.Limit(2);
        var list = await SelectAsync(query, cancellationToken).ConfigureAwait(false);
        return list.Count switch
        {
            1 => list[0],
            0 => throw new InvalidOperationException("Sequence contains no elements."),
            _ => throw new InvalidOperationException("Sequence contains more than one element.")
        };
    }

    public async IAsyncEnumerable<TEntity> StreamAsync<TEntity>(
        InquiryQuery<TEntity> query,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var list = await SelectAsync(query, cancellationToken).ConfigureAwait(false);
        foreach (var item in list)
        {
            yield return item;
        }
    }

    public Task<int> InsertAsync<TEntity>(TEntity entity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        var descriptor = _metadata.GetDescriptor<TEntity>();
        var plan = _provider.CommandFactory.BuildInsert(descriptor);
        return ExecuteNonQueryAsync(InquiryOperation.Insert, typeof(TEntity), plan, entity, cancellationToken);
    }

    public async Task<int> InsertManyAsync<TEntity>(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entities);
        var affected = 0;
        foreach (var entity in entities)
        {
            affected += await InsertAsync(entity, cancellationToken).ConfigureAwait(false);
        }

        return affected;
    }

    public Task<int> UpdateAsync<TEntity>(TEntity entity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        var descriptor = _metadata.GetDescriptor<TEntity>();
        var plan = _provider.CommandFactory.BuildUpdate(descriptor);
        return ExecuteNonQueryAsync(InquiryOperation.Update, typeof(TEntity), plan, entity, cancellationToken);
    }

    public Task<int> UpdateOnlyAsync<TEntity>(
        TEntity entity,
        IReadOnlyList<string> properties,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(properties);
        var descriptor = _metadata.GetDescriptor<TEntity>();
        var plan = _provider.CommandFactory.BuildUpdate(descriptor, properties);
        return ExecuteNonQueryAsync(InquiryOperation.Update, typeof(TEntity), plan, entity, cancellationToken);
    }

    public Task<int> DeleteAsync<TEntity>(TEntity entity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        var descriptor = _metadata.GetDescriptor<TEntity>();
        var plan = _provider.CommandFactory.BuildDelete(descriptor);
        return ExecuteNonQueryAsync(InquiryOperation.Delete, typeof(TEntity), plan, entity, cancellationToken);
    }

    public async Task<int> DeleteByKeyAsync<TEntity, TKey>(TKey key, CancellationToken cancellationToken = default)
    {
        var descriptor = _metadata.GetDescriptor<TEntity>();
        if (descriptor.Keys.Count != 1)
        {
            throw new InquiryMappingException($"DeleteByKeyAsync requires entity '{typeof(TEntity).FullName}' to have exactly one key.");
        }

        var entity = Activator.CreateInstance<TEntity>();
        descriptor.Keys[0].SetValue(entity, key);
        return await DeleteAsync(entity, cancellationToken).ConfigureAwait(false);
    }

    public Task<int> UpsertAsync<TEntity>(TEntity entity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        var descriptor = _metadata.GetDescriptor<TEntity>();
        var plan = _provider.CommandFactory.BuildUpsert(descriptor);
        return ExecuteNonQueryAsync(InquiryOperation.Upsert, typeof(TEntity), plan, entity, cancellationToken);
    }

    public Task<IReadOnlyList<TEntity>> QueryAsync<TEntity>(
        string sql,
        object? parameters = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            throw new ArgumentException("SQL cannot be empty.", nameof(sql));
        }

        var descriptor = _metadata.GetDescriptor<TEntity>();
        return ExecuteReaderAsync(
            InquiryOperation.RawQuery,
            typeof(TEntity),
            sql,
            parameters,
            reader => MaterializeByName(descriptor, reader),
            singleRow: false,
            cancellationToken);
    }

    public async Task<TEntity?> QuerySingleOrDefaultAsync<TEntity>(
        string sql,
        object? parameters = null,
        CancellationToken cancellationToken = default)
    {
        var list = await ExecuteReaderAsync(
            InquiryOperation.RawQuery,
            typeof(TEntity),
            sql,
            parameters,
            reader => MaterializeByName(_metadata.GetDescriptor<TEntity>(), reader),
            singleRow: true,
            cancellationToken).ConfigureAwait(false);

        return list.Count == 0 ? default : list[0];
    }

    public Task<int> ExecuteAsync(string sql, object? parameters = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            throw new ArgumentException("SQL cannot be empty.", nameof(sql));
        }

        return ExecuteNonQueryAsync(
            InquiryOperation.RawExecute,
            null,
            sql,
            parameters,
            cancellationToken);
    }

    public Task<IReadOnlyList<TEntity>> QueryStoredProcedureAsync<TEntity>(
        string procedureName,
        object? parameters = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(procedureName))
        {
            throw new ArgumentException("Stored procedure name cannot be empty.", nameof(procedureName));
        }

        var descriptor = _metadata.GetDescriptor<TEntity>();
        return ExecuteReaderAsync(
            InquiryOperation.StoredProcedureQuery,
            typeof(TEntity),
            procedureName,
            parameters,
            reader => MaterializeByName(descriptor, reader),
            singleRow: false,
            cancellationToken,
            CommandType.StoredProcedure);
    }

    public async Task<TEntity?> QuerySingleOrDefaultStoredProcedureAsync<TEntity>(
        string procedureName,
        object? parameters = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(procedureName))
        {
            throw new ArgumentException("Stored procedure name cannot be empty.", nameof(procedureName));
        }

        var list = await ExecuteReaderAsync(
            InquiryOperation.StoredProcedureQuery,
            typeof(TEntity),
            procedureName,
            parameters,
            reader => MaterializeByName(_metadata.GetDescriptor<TEntity>(), reader),
            singleRow: true,
            cancellationToken,
            CommandType.StoredProcedure).ConfigureAwait(false);

        return list.Count == 0 ? default : list[0];
    }

    public Task<int> ExecuteStoredProcedureAsync(
        string procedureName,
        object? parameters = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(procedureName))
        {
            throw new ArgumentException("Stored procedure name cannot be empty.", nameof(procedureName));
        }

        return ExecuteNonQueryAsync(
            InquiryOperation.StoredProcedureExecute,
            null,
            procedureName,
            parameters,
            cancellationToken,
            CommandType.StoredProcedure);
    }

    public async Task<IInquiryTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction is not null)
        {
            throw new InquiryValidationException("This InquiryClient is already bound to a transaction.");
        }

        var connection = await _connectionFactory(_services, cancellationToken).ConfigureAwait(false);
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var transactionClient = new InquiryClient(
            (_, _) => ValueTask.FromResult(connection),
            _provider,
            _metadata,
            _middleware,
            _services,
            ownsConnections: false,
            transaction);

        return new InquiryTransaction(transactionClient, transaction, connection, _ownsConnections);
    }

    public async Task ExecuteInTransactionAsync(
        Func<IInquiryClient, CancellationToken, Task> callback,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(callback);
        await using var transaction = await BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await callback(transaction.Client, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    private async Task<int> ExecuteNonQueryAsync<TEntity>(
        InquiryOperation operation,
        Type entityType,
        InquiryCommandPlan<TEntity> plan,
        TEntity entity,
        CancellationToken cancellationToken)
    {
        var parameters = plan.Parameters.ToDictionary(
            parameter => parameter.Name,
            parameter => parameter.Property.GetValue(entity),
            StringComparer.OrdinalIgnoreCase);

        return await ExecuteNonQueryAsync(operation, entityType, plan.CommandText, parameters, cancellationToken).ConfigureAwait(false);
    }

    private async Task<int> ExecuteNonQueryAsync(
        InquiryOperation operation,
        Type? entityType,
        string commandText,
        object? parameters,
        CancellationToken cancellationToken,
        CommandType commandType = CommandType.Text)
    {
        var commandParameters = InquiryParameterReader.ReadCommandParameters(parameters);
        var response = await ExecuteWithConnectionAsync(
            operation,
            entityType,
            commandText,
            commandParameters,
            commandType,
            async (command, token) =>
            {
                var rows = await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                return new InquiryResponse { Result = rows, RowsAffected = rows };
            },
            cancellationToken).ConfigureAwait(false);

        return response.Result is int rows ? rows : response.RowsAffected ?? 0;
    }

    private async Task<IReadOnlyList<TEntity>> ExecuteReaderAsync<TEntity>(
        InquiryOperation operation,
        Type? entityType,
        string commandText,
        object? parameters,
        Func<DbDataReader, TEntity> materialize,
        bool singleRow,
        CancellationToken cancellationToken,
        CommandType commandType = CommandType.Text)
    {
        var commandParameters = InquiryParameterReader.ReadCommandParameters(parameters);
        var response = await ExecuteWithConnectionAsync(
            operation,
            entityType,
            commandText,
            commandParameters,
            commandType,
            async (command, token) =>
            {
                var list = new List<TEntity>();
                await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, token).ConfigureAwait(false);
                while (await reader.ReadAsync(token).ConfigureAwait(false))
                {
                    list.Add(materialize(reader));
                    if (singleRow)
                    {
                        break;
                    }
                }

                return new InquiryResponse { Result = list, RowsAffected = list.Count };
            },
            cancellationToken).ConfigureAwait(false);

        return response.Result is IReadOnlyList<TEntity> rows
            ? rows
            : Array.Empty<TEntity>();
    }

    private async Task<InquiryResponse> ExecuteWithConnectionAsync(
        InquiryOperation operation,
        Type? entityType,
        string commandText,
        IReadOnlyList<InquiryParameter> parameters,
        CommandType commandType,
        Func<DbCommand, CancellationToken, Task<InquiryResponse>> execute,
        CancellationToken cancellationToken)
    {
        var connection = _transaction?.Connection ?? await _connectionFactory(_services, cancellationToken).ConfigureAwait(false);
        var shouldClose = false;
        var shouldDispose = _transaction is null && _ownsConnections;

        try
        {
            if (connection.State != ConnectionState.Open)
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                shouldClose = true;
            }

            var context = new InquiryRequestContext(
                operation,
                entityType,
                connection,
                _transaction,
                commandText,
                commandType,
                _provider.Name,
                _services,
                cancellationToken);

            foreach (var parameter in parameters)
            {
                context.CommandParameters.Add(parameter);
                context.Parameters[NormalizeParameterName(parameter.Name)] = parameter.Value;
            }

            var terminal = new InquiryRequestDelegate(async ctx =>
            {
                if (string.IsNullOrWhiteSpace(ctx.CommandText))
                {
                    throw new InquiryCommandException("Inquiry command text cannot be empty.");
                }

                await using var command = ctx.Connection.CreateCommand();
                command.CommandText = ctx.CommandText;
                command.CommandType = ctx.CommandType;
                command.Transaction = ctx.Transaction;
                var commandParameters = BuildCommandParameters(ctx);
                foreach (var parameter in commandParameters)
                {
                    AddParameter(command, parameter);
                }

                foreach (var commandEnricher in ctx.CommandEnrichers)
                {
                    commandEnricher(ctx, command);
                }

                var stopwatch = Stopwatch.StartNew();
                var response = await execute(command, ctx.CancellationToken).ConfigureAwait(false);
                stopwatch.Stop();
                CopyOutputParameterValues(command, commandParameters, ctx);

                return new InquiryResponse
                {
                    Result = response.Result,
                    RowsAffected = response.RowsAffected,
                    Elapsed = stopwatch.Elapsed
                };
            });

            var pipeline = InquiryPipeline.Build(_middleware, terminal);
            return await pipeline(context).ConfigureAwait(false);
        }
        catch (InquiryException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InquiryCommandException($"Inquiry {operation} failed for entity '{entityType?.FullName ?? "<raw>"}'.", ex);
        }
        finally
        {
            if (_transaction is null)
            {
                if (shouldClose && connection.State == ConnectionState.Open)
                {
                    await connection.CloseAsync().ConfigureAwait(false);
                }

                if (shouldDispose)
                {
                    await connection.DisposeAsync().ConfigureAwait(false);
                }
            }
        }
    }

    private static IReadOnlyList<InquiryParameter> BuildCommandParameters(InquiryRequestContext context)
    {
        var parameters = new List<InquiryParameter>(context.CommandParameters);
        var knownNames = new HashSet<string>(
            parameters.Select(parameter => NormalizeParameterName(parameter.Name)),
            StringComparer.OrdinalIgnoreCase);

        foreach (var parameter in parameters)
        {
            if (parameter.Direction is ParameterDirection.Input or ParameterDirection.InputOutput &&
                context.Parameters.TryGetValue(NormalizeParameterName(parameter.Name), out var value))
            {
                parameter.Value = value;
            }
        }

        foreach (var parameter in context.Parameters)
        {
            var normalizedName = NormalizeParameterName(parameter.Key);
            if (knownNames.Add(normalizedName))
            {
                parameters.Add(InquiryParameter.Input(normalizedName, parameter.Value));
            }
        }

        return parameters;
    }

    private static void AddParameter(DbCommand command, InquiryParameter inquiryParameter)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = NormalizeParameterName(inquiryParameter.Name);
        parameter.Direction = inquiryParameter.Direction;
        if (inquiryParameter.DbType is not null)
        {
            parameter.DbType = inquiryParameter.DbType.Value;
        }

        if (inquiryParameter.Size is not null)
        {
            parameter.Size = inquiryParameter.Size.Value;
        }

        if (inquiryParameter.IsNullable is not null)
        {
            parameter.IsNullable = inquiryParameter.IsNullable.Value;
        }

        parameter.Value = inquiryParameter.Value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private static void CopyOutputParameterValues(
        DbCommand command,
        IReadOnlyList<InquiryParameter> inquiryParameters,
        InquiryRequestContext context)
    {
        foreach (var inquiryParameter in inquiryParameters)
        {
            if (!ReceivesValueFromDatabase(inquiryParameter.Direction))
            {
                continue;
            }

            var parameterName = NormalizeParameterName(inquiryParameter.Name);
            if (!command.Parameters.Contains(parameterName))
            {
                continue;
            }

            var dbParameter = command.Parameters[parameterName];
            inquiryParameter.Value = dbParameter.Value is DBNull ? null : dbParameter.Value;
            context.Parameters[parameterName] = inquiryParameter.Value;
        }
    }

    private static bool ReceivesValueFromDatabase(ParameterDirection direction)
    {
        return direction is ParameterDirection.Output or ParameterDirection.InputOutput or ParameterDirection.ReturnValue;
    }

    private static string NormalizeParameterName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InquiryValidationException("Parameter names cannot be empty.");
        }

        return name[0] is '@' or ':' or '$' ? name : "@" + name;
    }

    private static TEntity MaterializeByOrdinal<TEntity>(
        IInquiryEntityDescriptor<TEntity> descriptor,
        DbDataReader reader)
    {
        if (descriptor is IInquiryMaterializer<TEntity> materializer)
        {
            return materializer.Materialize(reader);
        }

        var entity = Activator.CreateInstance<TEntity>();
        for (var index = 0; index < descriptor.Properties.Count; index++)
        {
            var property = descriptor.Properties[index];
            property.SetValue(entity, InquiryValueConverter.FromDatabaseValue(reader.IsDBNull(index) ? null : reader.GetValue(index), property.PropertyType));
        }

        return entity;
    }

    private static TEntity MaterializeByName<TEntity>(
        IInquiryEntityDescriptor<TEntity> descriptor,
        DbDataReader reader)
    {
        var entity = Activator.CreateInstance<TEntity>();
        var lookup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var ordinal = 0; ordinal < reader.FieldCount; ordinal++)
        {
            lookup[reader.GetName(ordinal)] = ordinal;
        }

        foreach (var property in descriptor.Properties)
        {
            if (!lookup.TryGetValue(property.ColumnName, out var ordinal))
            {
                continue;
            }

            property.SetValue(entity, InquiryValueConverter.FromDatabaseValue(reader.IsDBNull(ordinal) ? null : reader.GetValue(ordinal), property.PropertyType));
        }

        return entity;
    }
}
