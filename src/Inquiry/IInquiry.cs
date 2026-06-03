using Inquiry.Commands;
using Inquiry.Materialization;
using Inquiry.Transactions;
using System.Data;
using System.Data.Common;

namespace Inquiry;

/// <summary>
/// Provides simple database access for user-defined Inquiry stores and application services.
/// </summary>
public interface IInquiry
{
    /// <summary>
    /// Gets whether a 0-row UPDATE/DELETE on an optimistic-concurrency token entity should throw
    /// <see cref="InquiryConcurrencyException"/> rather than report <see langword="false"/>. Generated
    /// stores for token entities read this at the mutation call site; non-token entities never reference
    /// it. Defaults to <see langword="false"/> so existing <see cref="IInquiry"/> implementations stay
    /// source-compatible; <see cref="DefaultInquiry"/> surfaces <see cref="InquiryOptions.ThrowOnConcurrencyConflict"/>.
    /// </summary>
    bool ThrowOnConcurrencyConflict => false;

    // ---- Ad-hoc string overloads (DI-resolved class materializer) ----------------------

    /// <summary>Executes a SQL query and streams mapped entities.</summary>
    IAsyncEnumerable<TEntity> QueryAsync<TEntity>(
        string commandText,
        CancellationToken cancellationToken = default)
        where TEntity : class;

    /// <summary>Executes a SQL query with parameters and streams mapped entities.</summary>
    IAsyncEnumerable<TEntity> QueryAsync<TEntity>(
        string commandText,
        object? parameters,
        CancellationToken cancellationToken = default)
        where TEntity : class;

    /// <summary>Executes a SQL query and streams mapped entities.</summary>
    IAsyncEnumerable<TEntity> QueryAsync<TEntity>(
        InquiryCommand command,
        CancellationToken cancellationToken = default)
        where TEntity : class;

    /// <summary>Executes a SQL query and returns the buffered set of mapped entities.</summary>
    Task<IReadOnlyList<TEntity>> QueryListAsync<TEntity>(
        string commandText,
        CancellationToken cancellationToken = default)
        where TEntity : class;

    /// <summary>Executes a SQL query with parameters and returns the buffered set of mapped entities.</summary>
    Task<IReadOnlyList<TEntity>> QueryListAsync<TEntity>(
        string commandText,
        object? parameters,
        CancellationToken cancellationToken = default)
        where TEntity : class;

    /// <summary>Executes a SQL query and returns the buffered set of mapped entities.</summary>
    Task<IReadOnlyList<TEntity>> QueryListAsync<TEntity>(
        InquiryCommand command,
        CancellationToken cancellationToken = default)
        where TEntity : class;

    /// <summary>Executes a SQL query and returns the first mapped entity, or <see langword="null"/> when no row is returned.</summary>
    Task<TEntity?> QuerySingleOrDefaultAsync<TEntity>(
        string commandText,
        CancellationToken cancellationToken = default)
        where TEntity : class;

    /// <summary>Executes a SQL query with parameters and returns the first mapped entity, or <see langword="null"/> when no row is returned.</summary>
    Task<TEntity?> QuerySingleOrDefaultAsync<TEntity>(
        string commandText,
        object? parameters,
        CancellationToken cancellationToken = default)
        where TEntity : class;

    /// <summary>Executes a SQL query and returns the first mapped entity, or <see langword="null"/> when no row is returned.</summary>
    Task<TEntity?> QuerySingleOrDefaultAsync<TEntity>(
        InquiryCommand command,
        CancellationToken cancellationToken = default)
        where TEntity : class;

    // ---- Struct-materializer overloads (generated-store path) --------------------------
    //
    // These bypass the DI lookup that the class-materializer path needs and let the JIT
    // specialize the pipeline body per concrete TMaterializer struct, so the per-row
    // Materialize call inlines into the read loop. Generated stores pass
    // default(SomeEntityStructMaterializer) — the struct has no fields and no state.

    /// <summary>
    /// Streams materialized rows using a struct materializer. JIT-specialized per
    /// <typeparamref name="TMaterializer"/> so the per-row dispatch inlines.
    /// </summary>
    IAsyncEnumerable<TEntity> QueryAsync<TEntity, TMaterializer>(
        InquiryCommand command,
        TMaterializer materializer,
        CancellationToken cancellationToken = default)
        where TEntity : class
        where TMaterializer : struct, IInquiryEntityMaterializer<TEntity>;

    /// <summary>Buffered list query with a struct materializer.</summary>
    Task<IReadOnlyList<TEntity>> QueryListAsync<TEntity, TMaterializer>(
        InquiryCommand command,
        TMaterializer materializer,
        CancellationToken cancellationToken = default)
        where TEntity : class
        where TMaterializer : struct, IInquiryEntityMaterializer<TEntity>;

    /// <summary>Single-or-default query with a struct materializer.</summary>
    Task<TEntity?> QuerySingleOrDefaultAsync<TEntity, TMaterializer>(
        InquiryCommand command,
        TMaterializer materializer,
        CancellationToken cancellationToken = default)
        where TEntity : class
        where TMaterializer : struct, IInquiryEntityMaterializer<TEntity>;

    // ---- Execute (no materializer) ----------------------------------------------------

    /// <summary>Executes a SQL command and returns the affected row count.</summary>
    Task<int> ExecuteAsync(
        string commandText,
        CancellationToken cancellationToken = default);

    /// <summary>Executes a SQL command with parameters and returns the affected row count.</summary>
    Task<int> ExecuteAsync(
        string commandText,
        object? parameters,
        CancellationToken cancellationToken = default);

    /// <summary>Executes a SQL command and returns the affected row count.</summary>
    Task<int> ExecuteAsync(
        InquiryCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a SQL command, binding parameters via a caller-supplied static delegate. The
    /// generated-store path uses this overload to avoid allocating an <c>InquiryParameter[]</c>
    /// or <c>InquiryCommand</c> per call — the delegate writes directly into the
    /// <see cref="DbCommand"/>'s parameter collection.
    /// </summary>
    /// <remarks>
    /// The default implementation routes the call through <c>ExecuteAsync(InquiryCommand, …)</c>
    /// via <see cref="InquiryCommand.DbCommandBinder"/>, so existing <see cref="IInquiry"/>
    /// implementations (e.g. test mocks) stay source-compatible. <see cref="DefaultInquiry"/>
    /// overrides this and delegates to the pipeline's allocation-free fast path.
    /// </remarks>
    /// <typeparam name="TArgs">
    /// The bound state (typically the entity or key). Pass a static method group / static
    /// lambda for <paramref name="bindParameters"/> to keep this allocation-free.
    /// </typeparam>
    Task<int> ExecuteAsync<TArgs>(
        string commandText,
        TArgs args,
        Action<DbCommand, TArgs> bindParameters,
        CancellationToken cancellationToken = default)
    {
        if (commandText is null) throw new ArgumentNullException(nameof(commandText));
        if (bindParameters is null) throw new ArgumentNullException(nameof(bindParameters));
        return ExecuteAsync(
            new InquiryCommand(commandText, cmd => bindParameters(cmd, args)),
            cancellationToken);
    }

    /// <summary>
    /// Executes a command returning a single scalar value (COUNT/SUM/MIN/MAX/AVG). A null/DBNull
    /// result maps to <c>default(T)</c> (e.g. <see langword="null"/> for a nullable T).
    /// </summary>
    /// <remarks>The default throws; <see cref="DefaultInquiry"/> implements it over the pipeline, so
    /// existing <see cref="IInquiry"/> implementations stay source-compatible.</remarks>
    Task<T> ExecuteScalarAsync<T>(
        InquiryCommand command,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Scalar execution requires the built-in DefaultInquiry.");

    /// <summary>Scalar query binding parameters via a caller-supplied static delegate (fast path).</summary>
    Task<T> ExecuteScalarAsync<T, TArgs>(
        string commandText,
        TArgs args,
        Action<DbCommand, TArgs> bindParameters,
        CancellationToken cancellationToken = default)
    {
        if (commandText is null) throw new ArgumentNullException(nameof(commandText));
        if (bindParameters is null) throw new ArgumentNullException(nameof(bindParameters));
        return ExecuteScalarAsync<T>(
            new InquiryCommand(commandText, cmd => bindParameters(cmd, args)),
            cancellationToken);
    }

    /// <summary>
    /// Buffered query with a struct materializer, binding parameters via a caller-supplied static
    /// delegate. The generated-store path uses this overload to avoid allocating an
    /// <c>InquiryParameter[]</c> or <c>InquiryCommand</c> per call — the delegate writes directly
    /// into the <see cref="DbCommand"/>'s parameter collection.
    /// </summary>
    /// <remarks>
    /// The default implementation routes through <c>QueryListAsync&lt;TEntity, TMaterializer&gt;(InquiryCommand, …)</c>,
    /// so existing <see cref="IInquiry"/> implementations stay source-compatible.
    /// <see cref="DefaultInquiry"/> overrides this and delegates to the pipeline's allocation-free fast path.
    /// </remarks>
    Task<IReadOnlyList<TEntity>> QueryListAsync<TEntity, TArgs, TMaterializer>(
        string commandText,
        TArgs args,
        Action<DbCommand, TArgs> bindParameters,
        TMaterializer materializer,
        CancellationToken cancellationToken = default)
        where TEntity : class
        where TMaterializer : struct, IInquiryEntityMaterializer<TEntity>
    {
        if (commandText is null) throw new ArgumentNullException(nameof(commandText));
        if (bindParameters is null) throw new ArgumentNullException(nameof(bindParameters));
        return QueryListAsync<TEntity, TMaterializer>(
            new InquiryCommand(commandText, cmd => bindParameters(cmd, args)), materializer, cancellationToken);
    }

    /// <summary>
    /// Single-or-default query with a struct materializer, binding parameters via a caller-supplied
    /// static delegate. See
    /// <see cref="QueryListAsync{TEntity, TArgs, TMaterializer}(string, TArgs, Action{DbCommand, TArgs}, TMaterializer, CancellationToken)"/>
    /// for the allocation rationale.
    /// </summary>
    Task<TEntity?> QuerySingleOrDefaultAsync<TEntity, TArgs, TMaterializer>(
        string commandText,
        TArgs args,
        Action<DbCommand, TArgs> bindParameters,
        TMaterializer materializer,
        CancellationToken cancellationToken = default)
        where TEntity : class
        where TMaterializer : struct, IInquiryEntityMaterializer<TEntity>
    {
        if (commandText is null) throw new ArgumentNullException(nameof(commandText));
        if (bindParameters is null) throw new ArgumentNullException(nameof(bindParameters));
        return QuerySingleOrDefaultAsync<TEntity, TMaterializer>(
            new InquiryCommand(commandText, cmd => bindParameters(cmd, args)), materializer, cancellationToken);
    }

    // ---- Transactions -----------------------------------------------------------------

    /// <summary>
    /// Opens a new database connection and begins a transaction. All operations performed via
    /// <see cref="IInquiryTransaction.Inquiry"/> share that connection and transaction. The
    /// transaction rolls back automatically if disposed without committing.
    /// </summary>
    Task<IInquiryTransaction> BeginTransactionAsync(
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
        CancellationToken cancellationToken = default);
}
