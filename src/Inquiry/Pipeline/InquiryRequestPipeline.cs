using Inquiry.Commands;
using Inquiry.Connections;
using Inquiry.Interceptors;
using Inquiry.Materialization;
using Inquiry.Parameters;
using System.Data;
using System.Data.Common;
using System.Runtime.CompilerServices;

namespace Inquiry.Pipeline;

/// <summary>
/// Default implementation of the Inquiry request pipeline.
/// </summary>
/// <remarks>
/// Each read method exists in two flavours — one taking <see cref="IInquiryEntityMaterializer{T}"/>
/// (class), one taking a struct-constrained generic materializer. The struct variant is what
/// generated stores call; the JIT emits a separate body per concrete struct so the per-row
/// <c>materializer.Materialize(reader)</c> call inlines instead of going through an interface
/// dispatch.
///
/// All read methods pass <see cref="CommandBehavior.SingleResult"/> (or <c>SingleResult|SingleRow</c>
/// for the single-or-default path) to <c>ExecuteReaderAsync</c> so the provider can release
/// reader state as soon as the single result set drains.
///
/// Parameter binding and the three interceptor-notification methods are inlined into each
/// query body. The fast path checks <see cref="HasInterceptors"/> directly; when no interceptors
/// are registered the three <c>InquiryCommandContext</c> allocations and three <c>ValueTask</c>
/// awaits are eliminated entirely (matching Dapper's "nothing between reader and materializer"
/// loop).
/// </remarks>
public sealed class InquiryRequestPipeline : IInquiryRequestPipeline
{
    private const CommandBehavior ReadBehavior = CommandBehavior.SingleResult;
    private const CommandBehavior SingleRowBehavior = CommandBehavior.SingleResult | CommandBehavior.SingleRow;

    private readonly IInquiryConnectionFactory _connectionFactory;
    private readonly IInquiryCommandInterceptor[] _interceptors;

    /// <summary>
    /// Initializes a new instance of the <see cref="InquiryRequestPipeline"/> class.
    /// </summary>
    public InquiryRequestPipeline(
        IInquiryConnectionFactory connectionFactory,
        IEnumerable<IInquiryCommandInterceptor> interceptors)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _interceptors = interceptors?.ToArray() ?? throw new ArgumentNullException(nameof(interceptors));
    }

    private bool HasInterceptors => _interceptors.Length > 0;

    // ---- Class-materializer overloads (ad-hoc IInquiry path) -----------------------------

    /// <inheritdoc />
    public async IAsyncEnumerable<T> QueryAsync<T>(
        InquiryCommand command,
        IInquiryEntityMaterializer<T> materializer,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
        where T : class
    {
        if (command is null) throw new ArgumentNullException(nameof(command));
        if (materializer is null) throw new ArgumentNullException(nameof(materializer));

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var dbCommand = connection.CreateCommand();
        DbDataReader? reader = null;
        try
        {
            InitializeCommandSync(dbCommand, command);
            if (HasInterceptors)
            {
                await InvokeInitializedAsync(dbCommand, command, cancellationToken).ConfigureAwait(false);
                await InvokeExecutingAsync(command, dbCommand, cancellationToken).ConfigureAwait(false);
            }

            try
            {
                reader = await dbCommand.ExecuteReaderAsync(ReadBehavior, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                if (HasInterceptors) await InvokeFailedAsync(command, dbCommand, exception, cancellationToken).ConfigureAwait(false);
                throw;
            }

            while (true)
            {
                bool hasRow;
                try
                {
                    hasRow = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    if (HasInterceptors) await InvokeFailedAsync(command, dbCommand, exception, cancellationToken).ConfigureAwait(false);
                    throw;
                }

                if (!hasRow) break;

                T item;
                try
                {
                    item = materializer.Materialize(reader);
                }
                catch (Exception exception)
                {
                    if (HasInterceptors) await InvokeFailedAsync(command, dbCommand, exception, cancellationToken).ConfigureAwait(false);
                    throw;
                }

                yield return item;
            }

            if (HasInterceptors) await InvokeExecutedAsync(command, dbCommand, recordsAffected: null, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (reader is not null) await reader.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<T>> QueryListAsync<T>(
        InquiryCommand command,
        IInquiryEntityMaterializer<T> materializer,
        CancellationToken cancellationToken = default)
        where T : class
    {
        if (command is null) throw new ArgumentNullException(nameof(command));
        if (materializer is null) throw new ArgumentNullException(nameof(materializer));

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var dbCommand = connection.CreateCommand();

        try
        {
            InitializeCommandSync(dbCommand, command);
            if (HasInterceptors)
            {
                await InvokeInitializedAsync(dbCommand, command, cancellationToken).ConfigureAwait(false);
                await InvokeExecutingAsync(command, dbCommand, cancellationToken).ConfigureAwait(false);
            }

            await using var reader = await dbCommand.ExecuteReaderAsync(ReadBehavior, cancellationToken).ConfigureAwait(false);
            var list = new List<T>();
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                list.Add(materializer.Materialize(reader));
            }

            if (HasInterceptors) await InvokeExecutedAsync(command, dbCommand, recordsAffected: null, cancellationToken).ConfigureAwait(false);
            return list;
        }
        catch (Exception exception)
        {
            if (HasInterceptors) await InvokeFailedAsync(command, dbCommand, exception, cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<T?> QuerySingleOrDefaultAsync<T>(
        InquiryCommand command,
        IInquiryEntityMaterializer<T> materializer,
        CancellationToken cancellationToken = default)
        where T : class
    {
        if (command is null) throw new ArgumentNullException(nameof(command));
        if (materializer is null) throw new ArgumentNullException(nameof(materializer));

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var dbCommand = connection.CreateCommand();

        try
        {
            InitializeCommandSync(dbCommand, command);
            if (HasInterceptors)
            {
                await InvokeInitializedAsync(dbCommand, command, cancellationToken).ConfigureAwait(false);
                await InvokeExecutingAsync(command, dbCommand, cancellationToken).ConfigureAwait(false);
            }

            await using var reader = await dbCommand.ExecuteReaderAsync(SingleRowBehavior, cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                if (HasInterceptors) await InvokeExecutedAsync(command, dbCommand, recordsAffected: null, cancellationToken).ConfigureAwait(false);
                return default;
            }

            var result = materializer.Materialize(reader);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidOperationException("QuerySingleOrDefaultAsync expected zero or one row, but the query returned multiple rows.");
            }

            if (HasInterceptors) await InvokeExecutedAsync(command, dbCommand, recordsAffected: null, cancellationToken).ConfigureAwait(false);
            return result;
        }
        catch (Exception exception)
        {
            if (HasInterceptors) await InvokeFailedAsync(command, dbCommand, exception, cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    // ---- Struct-materializer overloads (generated-store path) -----------------------------
    //
    // Bodies mirror the class overloads but `materializer.Materialize(reader)` inlines because
    // the JIT specializes per TMaterializer. Keeping the bodies separate (rather than calling
    // a shared inner method) preserves that inlining.

    /// <inheritdoc />
    public async IAsyncEnumerable<T> QueryAsync<T, TMaterializer>(
        InquiryCommand command,
        TMaterializer materializer,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
        where T : class
        where TMaterializer : struct, IInquiryEntityMaterializer<T>
    {
        if (command is null) throw new ArgumentNullException(nameof(command));

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var dbCommand = connection.CreateCommand();
        DbDataReader? reader = null;
        try
        {
            InitializeCommandSync(dbCommand, command);
            if (HasInterceptors)
            {
                await InvokeInitializedAsync(dbCommand, command, cancellationToken).ConfigureAwait(false);
                await InvokeExecutingAsync(command, dbCommand, cancellationToken).ConfigureAwait(false);
            }

            try
            {
                reader = await dbCommand.ExecuteReaderAsync(ReadBehavior, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                if (HasInterceptors) await InvokeFailedAsync(command, dbCommand, exception, cancellationToken).ConfigureAwait(false);
                throw;
            }

            while (true)
            {
                bool hasRow;
                try
                {
                    hasRow = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    if (HasInterceptors) await InvokeFailedAsync(command, dbCommand, exception, cancellationToken).ConfigureAwait(false);
                    throw;
                }

                if (!hasRow) break;

                T item;
                try
                {
                    item = materializer.Materialize(reader);
                }
                catch (Exception exception)
                {
                    if (HasInterceptors) await InvokeFailedAsync(command, dbCommand, exception, cancellationToken).ConfigureAwait(false);
                    throw;
                }

                yield return item;
            }

            if (HasInterceptors) await InvokeExecutedAsync(command, dbCommand, recordsAffected: null, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (reader is not null) await reader.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<T>> QueryListAsync<T, TMaterializer>(
        InquiryCommand command,
        TMaterializer materializer,
        CancellationToken cancellationToken = default)
        where T : class
        where TMaterializer : struct, IInquiryEntityMaterializer<T>
    {
        if (command is null) throw new ArgumentNullException(nameof(command));

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var dbCommand = connection.CreateCommand();

        try
        {
            InitializeCommandSync(dbCommand, command);
            if (HasInterceptors)
            {
                await InvokeInitializedAsync(dbCommand, command, cancellationToken).ConfigureAwait(false);
                await InvokeExecutingAsync(command, dbCommand, cancellationToken).ConfigureAwait(false);
            }

            await using var reader = await dbCommand.ExecuteReaderAsync(ReadBehavior, cancellationToken).ConfigureAwait(false);
            var list = new List<T>();
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                list.Add(materializer.Materialize(reader));
            }

            if (HasInterceptors) await InvokeExecutedAsync(command, dbCommand, recordsAffected: null, cancellationToken).ConfigureAwait(false);
            return list;
        }
        catch (Exception exception)
        {
            if (HasInterceptors) await InvokeFailedAsync(command, dbCommand, exception, cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<T?> QuerySingleOrDefaultAsync<T, TMaterializer>(
        InquiryCommand command,
        TMaterializer materializer,
        CancellationToken cancellationToken = default)
        where T : class
        where TMaterializer : struct, IInquiryEntityMaterializer<T>
    {
        if (command is null) throw new ArgumentNullException(nameof(command));

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var dbCommand = connection.CreateCommand();

        try
        {
            InitializeCommandSync(dbCommand, command);
            if (HasInterceptors)
            {
                await InvokeInitializedAsync(dbCommand, command, cancellationToken).ConfigureAwait(false);
                await InvokeExecutingAsync(command, dbCommand, cancellationToken).ConfigureAwait(false);
            }

            await using var reader = await dbCommand.ExecuteReaderAsync(SingleRowBehavior, cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                if (HasInterceptors) await InvokeExecutedAsync(command, dbCommand, recordsAffected: null, cancellationToken).ConfigureAwait(false);
                return default;
            }

            var result = materializer.Materialize(reader);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidOperationException("QuerySingleOrDefaultAsync expected zero or one row, but the query returned multiple rows.");
            }

            if (HasInterceptors) await InvokeExecutedAsync(command, dbCommand, recordsAffected: null, cancellationToken).ConfigureAwait(false);
            return result;
        }
        catch (Exception exception)
        {
            if (HasInterceptors) await InvokeFailedAsync(command, dbCommand, exception, cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<T>> QueryListAsync<T, TArgs, TMaterializer>(
        string commandText,
        TArgs args,
        Action<DbCommand, TArgs> bindParameters,
        TMaterializer materializer,
        CancellationToken cancellationToken = default)
        where T : class
        where TMaterializer : struct, IInquiryEntityMaterializer<T>
    {
        if (commandText is null) throw new ArgumentNullException(nameof(commandText));
        if (bindParameters is null) throw new ArgumentNullException(nameof(bindParameters));

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var dbCommand = connection.CreateCommand();
        // Lazy: only allocate the InquiryCommand if interceptors need to observe the command.
        InquiryCommand? interceptorCommand = HasInterceptors ? new InquiryCommand(commandText) : null;

        try
        {
            dbCommand.CommandText = commandText;
            bindParameters(dbCommand, args);
            if (interceptorCommand is not null)
            {
                await InvokeInitializedAsync(dbCommand, interceptorCommand, cancellationToken).ConfigureAwait(false);
                await InvokeExecutingAsync(interceptorCommand, dbCommand, cancellationToken).ConfigureAwait(false);
            }

            await using var reader = await dbCommand.ExecuteReaderAsync(ReadBehavior, cancellationToken).ConfigureAwait(false);
            var list = new List<T>();
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                list.Add(materializer.Materialize(reader));
            }

            if (interceptorCommand is not null) await InvokeExecutedAsync(interceptorCommand, dbCommand, recordsAffected: null, cancellationToken).ConfigureAwait(false);
            return list;
        }
        catch (Exception exception)
        {
            if (interceptorCommand is not null) await InvokeFailedAsync(interceptorCommand, dbCommand, exception, cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<T?> QuerySingleOrDefaultAsync<T, TArgs, TMaterializer>(
        string commandText,
        TArgs args,
        Action<DbCommand, TArgs> bindParameters,
        TMaterializer materializer,
        CancellationToken cancellationToken = default)
        where T : class
        where TMaterializer : struct, IInquiryEntityMaterializer<T>
    {
        if (commandText is null) throw new ArgumentNullException(nameof(commandText));
        if (bindParameters is null) throw new ArgumentNullException(nameof(bindParameters));

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var dbCommand = connection.CreateCommand();
        InquiryCommand? interceptorCommand = HasInterceptors ? new InquiryCommand(commandText) : null;

        try
        {
            dbCommand.CommandText = commandText;
            bindParameters(dbCommand, args);
            if (interceptorCommand is not null)
            {
                await InvokeInitializedAsync(dbCommand, interceptorCommand, cancellationToken).ConfigureAwait(false);
                await InvokeExecutingAsync(interceptorCommand, dbCommand, cancellationToken).ConfigureAwait(false);
            }

            await using var reader = await dbCommand.ExecuteReaderAsync(SingleRowBehavior, cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                if (interceptorCommand is not null) await InvokeExecutedAsync(interceptorCommand, dbCommand, recordsAffected: null, cancellationToken).ConfigureAwait(false);
                return default;
            }

            var result = materializer.Materialize(reader);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidOperationException("QuerySingleOrDefaultAsync expected zero or one row, but the query returned multiple rows.");
            }

            if (interceptorCommand is not null) await InvokeExecutedAsync(interceptorCommand, dbCommand, recordsAffected: null, cancellationToken).ConfigureAwait(false);
            return result;
        }
        catch (Exception exception)
        {
            if (interceptorCommand is not null) await InvokeFailedAsync(interceptorCommand, dbCommand, exception, cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<int> ExecuteAsync(InquiryCommand command, CancellationToken cancellationToken = default)
    {
        if (command is null) throw new ArgumentNullException(nameof(command));

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var dbCommand = connection.CreateCommand();

        try
        {
            InitializeCommandSync(dbCommand, command);
            if (HasInterceptors)
            {
                await InvokeInitializedAsync(dbCommand, command, cancellationToken).ConfigureAwait(false);
                await InvokeExecutingAsync(command, dbCommand, cancellationToken).ConfigureAwait(false);
            }

            var recordsAffected = await dbCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            if (HasInterceptors) await InvokeExecutedAsync(command, dbCommand, recordsAffected, cancellationToken).ConfigureAwait(false);
            return recordsAffected;
        }
        catch (Exception exception)
        {
            if (HasInterceptors) await InvokeFailedAsync(command, dbCommand, exception, cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<int> ExecuteAsync<TArgs>(
        string commandText,
        TArgs args,
        Action<DbCommand, TArgs> bindParameters,
        CancellationToken cancellationToken = default)
    {
        if (commandText is null) throw new ArgumentNullException(nameof(commandText));
        if (bindParameters is null) throw new ArgumentNullException(nameof(bindParameters));

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var dbCommand = connection.CreateCommand();
        // Lazy: only allocate the InquiryCommand if interceptors are present (and only for the
        // failure path if execution throws before the first interceptor call).
        InquiryCommand? interceptorCommand = HasInterceptors ? new InquiryCommand(commandText) : null;

        try
        {
            dbCommand.CommandText = commandText;
            bindParameters(dbCommand, args);

            if (interceptorCommand is not null)
            {
                await InvokeInitializedAsync(dbCommand, interceptorCommand, cancellationToken).ConfigureAwait(false);
                await InvokeExecutingAsync(interceptorCommand, dbCommand, cancellationToken).ConfigureAwait(false);
            }

            var recordsAffected = await dbCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            if (interceptorCommand is not null) await InvokeExecutedAsync(interceptorCommand, dbCommand, recordsAffected, cancellationToken).ConfigureAwait(false);
            return recordsAffected;
        }
        catch (Exception exception)
        {
            if (interceptorCommand is not null) await InvokeFailedAsync(interceptorCommand, dbCommand, exception, cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    // ---- Synchronous setup + interceptor slow paths --------------------------------------

    private static void InitializeCommandSync(DbCommand dbCommand, InquiryCommand command)
    {
        dbCommand.CommandText = command.CommandText;
        if (command.CommandType is not null) dbCommand.CommandType = command.CommandType.Value;
        if (command.CommandTimeout is not null) dbCommand.CommandTimeout = command.CommandTimeout.Value;
        InquiryParameterBinder.Bind(dbCommand, command.ParametersArray);
        command.DbCommandBinder?.Invoke(dbCommand);
    }

    private async ValueTask InvokeInitializedAsync(DbCommand dbCommand, InquiryCommand command, CancellationToken cancellationToken)
    {
        var context = new InquiryCommandContext(command, dbCommand);
        foreach (var interceptor in _interceptors)
        {
            await interceptor.CommandInitializedAsync(context, cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask InvokeExecutingAsync(InquiryCommand command, DbCommand dbCommand, CancellationToken cancellationToken)
    {
        var context = new InquiryCommandContext(command, dbCommand);
        foreach (var interceptor in _interceptors)
        {
            await interceptor.CommandExecutingAsync(context, cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask InvokeExecutedAsync(InquiryCommand command, DbCommand dbCommand, int? recordsAffected, CancellationToken cancellationToken)
    {
        var context = new InquiryCommandExecutedContext(command, dbCommand, recordsAffected);
        foreach (var interceptor in _interceptors)
        {
            await interceptor.CommandExecutedAsync(context, cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask InvokeFailedAsync(InquiryCommand command, DbCommand dbCommand, Exception exception, CancellationToken cancellationToken)
    {
        var context = new InquiryCommandFailedContext(command, dbCommand, exception);
        foreach (var interceptor in _interceptors)
        {
            await interceptor.CommandFailedAsync(context, cancellationToken).ConfigureAwait(false);
        }
    }
}
