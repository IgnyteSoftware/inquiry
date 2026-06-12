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
/// The class-materializer read methods pass <see cref="CommandBehavior.SingleResult"/> (or
/// <c>SingleResult|SingleRow</c> for the single-or-default path) so the provider can release reader
/// state as soon as the single result set drains. The struct-materializer (generated-store) overloads
/// additionally pass <see cref="CommandBehavior.SequentialAccess"/> — generated materializers read each
/// column exactly once in ascending ordinal order, so the row can be streamed forward-only instead of
/// buffered, roughly halving allocation on large/wide result sets.
///
/// Parameter binding and the three interceptor-notification methods are inlined into each
/// query body. The fast path checks <see cref="HasInterceptors"/> directly; when no interceptors
/// are registered the three <c>InquiryCommandContext</c> allocations and three <c>ValueTask</c>
/// awaits are eliminated entirely (matching Dapper's "nothing between reader and materializer"
/// loop).
/// </remarks>
internal sealed class InquiryRequestPipeline : IInquiryRequestPipeline
{
    private const CommandBehavior ReadBehavior = CommandBehavior.SingleResult;

    // Single-row reads deliberately omit CommandBehavior.SingleRow. The QuerySingleOrDefaultAsync
    // contract throws if the query returns more than one row, and that detection requires a second
    // ReadAsync call to observe the extra row. SingleRow gives providers permission to stop after the
    // first row, silently suppressing the detection on providers that honour the hint (audit P2 #5).
    private const CommandBehavior SingleRowBehavior = CommandBehavior.SingleResult;

    // The struct-materializer (generated-store) overloads add SequentialAccess: generated materializers
    // read every column exactly once in ascending ordinal order, so the provider can stream the row
    // forward-only instead of buffering it — roughly halving allocation on large/wide result sets
    // (matching Dapper). The class-materializer overloads above keep the buffered behaviours, because a
    // caller-supplied IInquiryEntityMaterializer<T> may read columns out of order, which SequentialAccess forbids.
    private const CommandBehavior SequentialReadBehavior = CommandBehavior.SingleResult | CommandBehavior.SequentialAccess;
    private const CommandBehavior SequentialSingleRowBehavior = CommandBehavior.SingleResult | CommandBehavior.SequentialAccess;

    private readonly IInquiryConnectionFactory _connectionFactory;
    private readonly IInquiryCommandInterceptor[] _interceptors;

    // True when Auto preparation is configured AND the provider's prepared state survives the
    // connection lifecycle. The per-command StoredProcedure check is applied at the call site.
    private readonly bool _prepareEnabled;

    /// <summary>
    /// Initializes a new instance of the <see cref="InquiryRequestPipeline"/> class.
    /// </summary>
    public InquiryRequestPipeline(
        IInquiryConnectionFactory connectionFactory,
        IEnumerable<IInquiryCommandInterceptor> interceptors)
        : this(connectionFactory, interceptors, options: null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="InquiryRequestPipeline"/> class with options.
    /// </summary>
    public InquiryRequestPipeline(
        IInquiryConnectionFactory connectionFactory,
        IEnumerable<IInquiryCommandInterceptor> interceptors,
        InquiryOptions? options)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _interceptors = interceptors?.ToArray() ?? throw new ArgumentNullException(nameof(interceptors));
        _prepareEnabled = (options?.PrepareStatements ?? PreparedStatementMode.Auto) == PreparedStatementMode.Auto
            && _connectionFactory.SupportsPersistentPreparedStatements;
    }

    private bool HasInterceptors => _interceptors.Length > 0;

    /// <summary>
    /// Creates a command on <paramref name="connection"/> and runs the factory's
    /// <see cref="IInquiryConnectionFactory.InitializeCommand"/> hook.
    /// </summary>
    private DbCommand CreateCommand(DbConnection connection)
    {
        var dbCommand = connection.CreateCommand();
        _connectionFactory.InitializeCommand(dbCommand);
        return dbCommand;
    }

    // Prepares the command when enabled and it is not a stored procedure. Kept as a single guarded
    // statement so unsupported providers and explicit opt-outs stay branch-cheap.
    private ValueTask MaybePrepareAsync(DbCommand dbCommand, CancellationToken cancellationToken)
    {
        if (_prepareEnabled && dbCommand.CommandType != CommandType.StoredProcedure)
        {
            return new ValueTask(dbCommand.PrepareAsync(cancellationToken));
        }

        return default;
    }

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
        await using var dbCommand = CreateCommand(connection);
        DbDataReader? reader = null;
        try
        {
            InitializeCommandSync(dbCommand, command);
            if (HasInterceptors)
            {
                try
                {
                    await InvokeInitializedAsync(dbCommand, command, cancellationToken).ConfigureAwait(false);
                    await InvokeExecutingAsync(command, dbCommand, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    await InvokeFailedAsync(command, dbCommand, exception, cancellationToken).ConfigureAwait(false);
                    throw;
                }
            }

            try
            {
                await MaybePrepareAsync(dbCommand, cancellationToken).ConfigureAwait(false);
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

            if (HasInterceptors)
            {
                try
                {
                    await InvokeExecutedAsync(command, dbCommand, recordsAffected: null, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    await InvokeFailedAsync(command, dbCommand, exception, cancellationToken).ConfigureAwait(false);
                    throw;
                }
            }
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
        await using var dbCommand = CreateCommand(connection);

        try
        {
            InitializeCommandSync(dbCommand, command);
            if (HasInterceptors)
            {
                await InvokeInitializedAsync(dbCommand, command, cancellationToken).ConfigureAwait(false);
                await InvokeExecutingAsync(command, dbCommand, cancellationToken).ConfigureAwait(false);
            }

            await MaybePrepareAsync(dbCommand, cancellationToken).ConfigureAwait(false);
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
        await using var dbCommand = CreateCommand(connection);

        try
        {
            InitializeCommandSync(dbCommand, command);
            if (HasInterceptors)
            {
                await InvokeInitializedAsync(dbCommand, command, cancellationToken).ConfigureAwait(false);
                await InvokeExecutingAsync(command, dbCommand, cancellationToken).ConfigureAwait(false);
            }

            await MaybePrepareAsync(dbCommand, cancellationToken).ConfigureAwait(false);
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
        await using var dbCommand = CreateCommand(connection);
        DbDataReader? reader = null;
        try
        {
            InitializeCommandSync(dbCommand, command);
            if (HasInterceptors)
            {
                try
                {
                    await InvokeInitializedAsync(dbCommand, command, cancellationToken).ConfigureAwait(false);
                    await InvokeExecutingAsync(command, dbCommand, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    await InvokeFailedAsync(command, dbCommand, exception, cancellationToken).ConfigureAwait(false);
                    throw;
                }
            }

            try
            {
                await MaybePrepareAsync(dbCommand, cancellationToken).ConfigureAwait(false);
                reader = await dbCommand.ExecuteReaderAsync(SequentialReadBehavior, cancellationToken).ConfigureAwait(false);
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

            if (HasInterceptors)
            {
                try
                {
                    await InvokeExecutedAsync(command, dbCommand, recordsAffected: null, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    await InvokeFailedAsync(command, dbCommand, exception, cancellationToken).ConfigureAwait(false);
                    throw;
                }
            }
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
        await using var dbCommand = CreateCommand(connection);

        try
        {
            InitializeCommandSync(dbCommand, command);
            if (HasInterceptors)
            {
                await InvokeInitializedAsync(dbCommand, command, cancellationToken).ConfigureAwait(false);
                await InvokeExecutingAsync(command, dbCommand, cancellationToken).ConfigureAwait(false);
            }

            await MaybePrepareAsync(dbCommand, cancellationToken).ConfigureAwait(false);
            await using var reader = await dbCommand.ExecuteReaderAsync(SequentialReadBehavior, cancellationToken).ConfigureAwait(false);
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
        await using var dbCommand = CreateCommand(connection);

        try
        {
            InitializeCommandSync(dbCommand, command);
            if (HasInterceptors)
            {
                await InvokeInitializedAsync(dbCommand, command, cancellationToken).ConfigureAwait(false);
                await InvokeExecutingAsync(command, dbCommand, cancellationToken).ConfigureAwait(false);
            }

            await MaybePrepareAsync(dbCommand, cancellationToken).ConfigureAwait(false);
            await using var reader = await dbCommand.ExecuteReaderAsync(SequentialSingleRowBehavior, cancellationToken).ConfigureAwait(false);
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
    public async IAsyncEnumerable<T> QueryAsync<T, TArgs, TMaterializer>(
        string commandText,
        TArgs args,
        Action<DbCommand, TArgs> bindParameters,
        TMaterializer materializer,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
        where T : class
        where TMaterializer : struct, IInquiryEntityMaterializer<T>
    {
        if (commandText is null) throw new ArgumentNullException(nameof(commandText));
        if (bindParameters is null) throw new ArgumentNullException(nameof(bindParameters));

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var dbCommand = CreateCommand(connection);
        // Lazy: only allocate the InquiryCommand if interceptors need to observe the command.
        InquiryCommand? interceptorCommand = HasInterceptors ? new InquiryCommand(commandText) : null;
        DbDataReader? reader = null;
        try
        {
            dbCommand.CommandText = commandText;
            bindParameters(dbCommand, args);
            _connectionFactory.FinalizeCommand(dbCommand);
            if (interceptorCommand is not null)
            {
                try
                {
                    await InvokeInitializedAsync(dbCommand, interceptorCommand, cancellationToken).ConfigureAwait(false);
                    await InvokeExecutingAsync(interceptorCommand, dbCommand, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    await InvokeFailedAsync(interceptorCommand, dbCommand, exception, cancellationToken).ConfigureAwait(false);
                    throw;
                }
            }

            try
            {
                await MaybePrepareAsync(dbCommand, cancellationToken).ConfigureAwait(false);
                reader = await dbCommand.ExecuteReaderAsync(SequentialReadBehavior, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                if (interceptorCommand is not null) await InvokeFailedAsync(interceptorCommand, dbCommand, exception, cancellationToken).ConfigureAwait(false);
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
                    if (interceptorCommand is not null) await InvokeFailedAsync(interceptorCommand, dbCommand, exception, cancellationToken).ConfigureAwait(false);
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
                    if (interceptorCommand is not null) await InvokeFailedAsync(interceptorCommand, dbCommand, exception, cancellationToken).ConfigureAwait(false);
                    throw;
                }

                yield return item;
            }

            if (interceptorCommand is not null)
            {
                try
                {
                    await InvokeExecutedAsync(interceptorCommand, dbCommand, recordsAffected: null, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    await InvokeFailedAsync(interceptorCommand, dbCommand, exception, cancellationToken).ConfigureAwait(false);
                    throw;
                }
            }
        }
        finally
        {
            if (reader is not null) await reader.DisposeAsync().ConfigureAwait(false);
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
        await using var dbCommand = CreateCommand(connection);
        // Lazy: only allocate the InquiryCommand if interceptors need to observe the command.
        InquiryCommand? interceptorCommand = HasInterceptors ? new InquiryCommand(commandText) : null;

        try
        {
            dbCommand.CommandText = commandText;
            bindParameters(dbCommand, args);
            _connectionFactory.FinalizeCommand(dbCommand);
            if (interceptorCommand is not null)
            {
                await InvokeInitializedAsync(dbCommand, interceptorCommand, cancellationToken).ConfigureAwait(false);
                await InvokeExecutingAsync(interceptorCommand, dbCommand, cancellationToken).ConfigureAwait(false);
            }

            await MaybePrepareAsync(dbCommand, cancellationToken).ConfigureAwait(false);
            await using var reader = await dbCommand.ExecuteReaderAsync(SequentialReadBehavior, cancellationToken).ConfigureAwait(false);
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
        await using var dbCommand = CreateCommand(connection);
        InquiryCommand? interceptorCommand = HasInterceptors ? new InquiryCommand(commandText) : null;

        try
        {
            dbCommand.CommandText = commandText;
            bindParameters(dbCommand, args);
            _connectionFactory.FinalizeCommand(dbCommand);
            if (interceptorCommand is not null)
            {
                await InvokeInitializedAsync(dbCommand, interceptorCommand, cancellationToken).ConfigureAwait(false);
                await InvokeExecutingAsync(interceptorCommand, dbCommand, cancellationToken).ConfigureAwait(false);
            }

            await MaybePrepareAsync(dbCommand, cancellationToken).ConfigureAwait(false);
            await using var reader = await dbCommand.ExecuteReaderAsync(SequentialSingleRowBehavior, cancellationToken).ConfigureAwait(false);
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
        await using var dbCommand = CreateCommand(connection);

        try
        {
            InitializeCommandSync(dbCommand, command);
            if (HasInterceptors)
            {
                await InvokeInitializedAsync(dbCommand, command, cancellationToken).ConfigureAwait(false);
                await InvokeExecutingAsync(command, dbCommand, cancellationToken).ConfigureAwait(false);
            }

            await MaybePrepareAsync(dbCommand, cancellationToken).ConfigureAwait(false);
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
    public async Task<T> ExecuteScalarAsync<T>(InquiryCommand command, CancellationToken cancellationToken = default)
    {
        if (command is null) throw new ArgumentNullException(nameof(command));

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var dbCommand = CreateCommand(connection);

        try
        {
            InitializeCommandSync(dbCommand, command);
            if (HasInterceptors)
            {
                await InvokeInitializedAsync(dbCommand, command, cancellationToken).ConfigureAwait(false);
                await InvokeExecutingAsync(command, dbCommand, cancellationToken).ConfigureAwait(false);
            }

            await MaybePrepareAsync(dbCommand, cancellationToken).ConfigureAwait(false);
            var value = await dbCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

            if (HasInterceptors) await InvokeExecutedAsync(command, dbCommand, recordsAffected: null, cancellationToken).ConfigureAwait(false);
            return ScalarConvert.From<T>(value);
        }
        catch (Exception exception)
        {
            if (HasInterceptors) await InvokeFailedAsync(command, dbCommand, exception, cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<T> ExecuteScalarAsync<T, TArgs>(
        string commandText,
        TArgs args,
        Action<DbCommand, TArgs> bindParameters,
        CancellationToken cancellationToken = default)
    {
        if (commandText is null) throw new ArgumentNullException(nameof(commandText));
        if (bindParameters is null) throw new ArgumentNullException(nameof(bindParameters));

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var dbCommand = CreateCommand(connection);
        InquiryCommand? interceptorCommand = HasInterceptors ? new InquiryCommand(commandText) : null;

        try
        {
            dbCommand.CommandText = commandText;
            bindParameters(dbCommand, args);
            _connectionFactory.FinalizeCommand(dbCommand);
            if (interceptorCommand is not null)
            {
                await InvokeInitializedAsync(dbCommand, interceptorCommand, cancellationToken).ConfigureAwait(false);
                await InvokeExecutingAsync(interceptorCommand, dbCommand, cancellationToken).ConfigureAwait(false);
            }

            await MaybePrepareAsync(dbCommand, cancellationToken).ConfigureAwait(false);
            var value = await dbCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

            if (interceptorCommand is not null) await InvokeExecutedAsync(interceptorCommand, dbCommand, recordsAffected: null, cancellationToken).ConfigureAwait(false);
            return ScalarConvert.From<T>(value);
        }
        catch (Exception exception)
        {
            if (interceptorCommand is not null) await InvokeFailedAsync(interceptorCommand, dbCommand, exception, cancellationToken).ConfigureAwait(false);
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
        await using var dbCommand = CreateCommand(connection);
        // Lazy: only allocate the InquiryCommand if interceptors are present (and only for the
        // failure path if execution throws before the first interceptor call).
        InquiryCommand? interceptorCommand = HasInterceptors ? new InquiryCommand(commandText) : null;

        try
        {
            dbCommand.CommandText = commandText;
            bindParameters(dbCommand, args);
            _connectionFactory.FinalizeCommand(dbCommand);

            if (interceptorCommand is not null)
            {
                await InvokeInitializedAsync(dbCommand, interceptorCommand, cancellationToken).ConfigureAwait(false);
                await InvokeExecutingAsync(interceptorCommand, dbCommand, cancellationToken).ConfigureAwait(false);
            }

            await MaybePrepareAsync(dbCommand, cancellationToken).ConfigureAwait(false);
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

    private void InitializeCommandSync(DbCommand dbCommand, InquiryCommand command)
    {
        dbCommand.CommandText = command.CommandText;
        if (command.CommandType is not null) dbCommand.CommandType = command.CommandType.Value;
        if (command.CommandTimeout is not null) dbCommand.CommandTimeout = command.CommandTimeout.Value;
        InquiryParameterBinder.Bind(dbCommand, command.ParametersArray);
        command.DbCommandBinder?.Invoke(dbCommand);
        _connectionFactory.FinalizeCommand(dbCommand);
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
