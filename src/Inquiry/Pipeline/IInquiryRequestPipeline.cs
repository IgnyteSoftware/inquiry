using Inquiry.Commands;
using Inquiry.Materialization;
using System.Data.Common;

namespace Inquiry.Pipeline;

/// <summary>
/// Executes Inquiry database requests.
/// </summary>
/// <remarks>
/// Each read method has two overloads:
/// <list type="bullet">
///   <item>A <em>class-materializer</em> overload taking <see cref="IInquiryEntityMaterializer{T}"/> — used by ad-hoc <see cref="IInquiry"/> calls where the materializer is resolved from DI.</item>
///   <item>A <em>struct-materializer</em> overload constrained as <c>where TMaterializer : struct, IInquiryEntityMaterializer&lt;T&gt;</c> — used by generated stores. The struct constraint lets the JIT emit a specialization per concrete materializer type so the per-row <c>Materialize</c> call is inlined into the read loop, matching Dapper's monomorphic dispatch.</item>
/// </list>
/// </remarks>
internal interface IInquiryRequestPipeline
{
    /// <summary>Executes a query and streams materialized rows.</summary>
    IAsyncEnumerable<T> QueryAsync<T>(
        InquiryCommand command,
        IInquiryEntityMaterializer<T> materializer,
        CancellationToken cancellationToken = default)
        where T : class;

    /// <summary>
    /// Executes a query and streams materialized rows using a struct materializer; the JIT
    /// specializes this method per <typeparamref name="TMaterializer"/> so the inner
    /// <c>materializer.Materialize(reader)</c> call inlines.
    /// </summary>
    IAsyncEnumerable<T> QueryAsync<T, TMaterializer>(
        InquiryCommand command,
        TMaterializer materializer,
        CancellationToken cancellationToken = default)
        where T : class
        where TMaterializer : struct, IInquiryEntityMaterializer<T>;

    /// <summary>
    /// Executes a query and returns a buffered list of materialized rows.
    /// </summary>
    Task<IReadOnlyList<T>> QueryListAsync<T>(
        InquiryCommand command,
        IInquiryEntityMaterializer<T> materializer,
        CancellationToken cancellationToken = default)
        where T : class;

    /// <summary>Buffered query with a struct materializer (JIT-specialized).</summary>
    Task<IReadOnlyList<T>> QueryListAsync<T, TMaterializer>(
        InquiryCommand command,
        TMaterializer materializer,
        CancellationToken cancellationToken = default)
        where T : class
        where TMaterializer : struct, IInquiryEntityMaterializer<T>;

    /// <summary>Executes a query and returns the first materialized row, or <see langword="null"/>.</summary>
    Task<T?> QuerySingleOrDefaultAsync<T>(
        InquiryCommand command,
        IInquiryEntityMaterializer<T> materializer,
        CancellationToken cancellationToken = default)
        where T : class;

    /// <summary>Single-or-default query with a struct materializer (JIT-specialized).</summary>
    Task<T?> QuerySingleOrDefaultAsync<T, TMaterializer>(
        InquiryCommand command,
        TMaterializer materializer,
        CancellationToken cancellationToken = default)
        where T : class
        where TMaterializer : struct, IInquiryEntityMaterializer<T>;

    /// <summary>
    /// Executes a command returning multiple result sets and returns a grid reader to materialize them in
    /// order (one round trip). Generated eager-load stores use this for parent + key-filterable children.
    /// </summary>
    /// <remarks>A default-interface-method (throwing) so custom pipelines stay source-compatible; the
    /// built-in pipelines provide the real implementation.</remarks>
    Task<InquiryGridReader> QueryMultipleAsync(
        InquiryCommand command,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Multi-result-set queries are implemented by the built-in Inquiry pipelines.");

    /// <summary>
    /// Streaming query whose parameters are bound by a caller-supplied static delegate, avoiding
    /// the <c>InquiryCommand</c> / <c>InquiryParameter[]</c> allocations of the boxed path —
    /// generated stores pass a method group / static lambda (no closure capture).
    /// </summary>
    /// <remarks>
    /// The default implementation routes through <c>QueryAsync&lt;T, TMaterializer&gt;(InquiryCommand, …)</c>
    /// via <see cref="InquiryCommand.DbCommandBinder"/>, so custom <c>IInquiryRequestPipeline</c>
    /// implementations stay source-compatible. The built-in pipelines override this with an
    /// allocation-free fast path.
    /// </remarks>
    IAsyncEnumerable<T> QueryAsync<T, TArgs, TMaterializer>(
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
        return QueryAsync<T, TMaterializer>(
            new InquiryCommand(commandText, cmd => bindParameters(cmd, args)), materializer, cancellationToken);
    }

    /// <summary>
    /// Buffered query whose parameters are bound by a caller-supplied static delegate, avoiding
    /// the <c>InquiryCommand</c> / <c>InquiryParameter[]</c> allocations of the boxed path —
    /// generated stores pass a method group / static lambda (no closure capture).
    /// </summary>
    /// <remarks>
    /// The default implementation routes through <c>QueryListAsync&lt;T, TMaterializer&gt;(InquiryCommand, …)</c>
    /// via <see cref="InquiryCommand.DbCommandBinder"/>, so custom <c>IInquiryRequestPipeline</c>
    /// implementations stay source-compatible. The built-in <see cref="InquiryRequestPipeline"/>
    /// overrides this with an allocation-free fast path.
    /// </remarks>
    Task<IReadOnlyList<T>> QueryListAsync<T, TArgs, TMaterializer>(
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
        return QueryListAsync<T, TMaterializer>(
            new InquiryCommand(commandText, cmd => bindParameters(cmd, args)), materializer, cancellationToken);
    }

    /// <summary>
    /// Single-or-default query whose parameters are bound by a caller-supplied static delegate.
    /// See <see cref="QueryListAsync{T, TArgs, TMaterializer}(string, TArgs, Action{DbCommand, TArgs}, TMaterializer, CancellationToken)"/>
    /// for the allocation rationale and source-compatibility note.
    /// </summary>
    Task<T?> QuerySingleOrDefaultAsync<T, TArgs, TMaterializer>(
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
        return QuerySingleOrDefaultAsync<T, TMaterializer>(
            new InquiryCommand(commandText, cmd => bindParameters(cmd, args)), materializer, cancellationToken);
    }

    /// <summary>Executes a non-query command and returns the affected row count.</summary>
    Task<int> ExecuteAsync(
        InquiryCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a non-query command with parameters bound by a caller-supplied static delegate.
    /// Avoids the <c>InquiryCommand</c> / <c>InquiryParameter[]</c> allocations of the boxed
    /// path — generated stores pass a method group (no closure capture), and the binder uses
    /// <c>DbCommand.CreateParameter</c> / <c>Parameters.Add</c> directly.
    /// </summary>
    /// <remarks>
    /// The default implementation routes the call through the existing <c>ExecuteAsync(InquiryCommand, …)</c>
    /// path via <see cref="InquiryCommand.DbCommandBinder"/>, so custom <c>IInquiryRequestPipeline</c>
    /// implementations stay source-compatible. The built-in <see cref="InquiryRequestPipeline"/>
    /// overrides this with an allocation-free fast path.
    /// </remarks>
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
    /// Executes <paramref name="commandText"/> once per item in <paramref name="items"/>, binding
    /// each item's parameters via a caller-supplied static delegate, and returns the total affected
    /// row count. An empty list returns 0 without touching the database.
    /// </summary>
    /// <remarks>
    /// The default implementation loops over the existing
    /// <c>ExecuteAsync&lt;TArgs&gt;(string, TArgs, Action&lt;DbCommand, TArgs&gt;, …)</c> per item, so
    /// custom <c>IInquiryRequestPipeline</c> implementations stay source-compatible. The built-in
    /// pipelines override this with a fast path that executes all items in a single
    /// <see cref="DbBatch"/> round trip when the provider and connection factory support it.
    /// </remarks>
    async Task<int> ExecuteBatchAsync<TItem>(
        string commandText,
        IReadOnlyList<TItem> items,
        Action<InquiryParameterTarget, TItem> bindParameters,
        CancellationToken cancellationToken = default)
    {
        if (commandText is null) throw new ArgumentNullException(nameof(commandText));
        if (items is null) throw new ArgumentNullException(nameof(items));
        if (bindParameters is null) throw new ArgumentNullException(nameof(bindParameters));

        var total = 0;
        for (var i = 0; i < items.Count; i++)
        {
            total += await ExecuteAsync(
                commandText,
                items[i],
                (cmd, item) => bindParameters(new InquiryParameterTarget(cmd), item),
                cancellationToken).ConfigureAwait(false);
        }

        return total;
    }

    /// <summary>Executes a command returning a single scalar value (e.g. COUNT/SUM/MIN/MAX).</summary>
    /// <remarks>A default-interface-method (throwing) so custom pipelines stay source-compatible; the
    /// built-in pipelines provide the real implementation.</remarks>
    Task<T> ExecuteScalarAsync<T>(
        InquiryCommand command,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Scalar execution is implemented by the built-in Inquiry pipeline.");

    /// <summary>
    /// Executes a command (typically a stored procedure) and returns the post-execution value of a
    /// bound output / return-value parameter named <paramref name="readBackParameterName"/>,
    /// converted to <typeparamref name="T"/>. The command must register that parameter with the
    /// appropriate <see cref="System.Data.ParameterDirection"/>.
    /// </summary>
    /// <remarks>A default-interface-method (throwing) so custom pipelines stay source-compatible; the
    /// built-in pipelines provide the real implementation.</remarks>
    Task<T> ExecuteProcedureScalarAsync<T>(
        InquiryCommand command,
        string readBackParameterName,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Stored-procedure output execution is implemented by the built-in Inquiry pipeline.");

    /// <summary>
    /// Scalar query with parameters bound by a caller-supplied static delegate (allocation-free
    /// fast path). The default routes through <c>ExecuteScalarAsync&lt;T&gt;(InquiryCommand, …)</c> so
    /// custom pipelines stay source-compatible; the built-in pipeline overrides it.
    /// </summary>
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
}
