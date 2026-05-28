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
    /// <typeparam name="TArgs">
    /// The bound state (typically the entity or key). Pass a static method group / static
    /// lambda for <paramref name="bindParameters"/> to keep this allocation-free.
    /// </typeparam>
    Task<int> ExecuteAsync<TArgs>(
        string commandText,
        TArgs args,
        Action<DbCommand, TArgs> bindParameters,
        CancellationToken cancellationToken = default);

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
