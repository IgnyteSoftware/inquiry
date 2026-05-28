using Inquiry.Commands;
using Inquiry.Materialization;

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
public interface IInquiryRequestPipeline
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

    /// <summary>Executes a non-query command and returns the affected row count.</summary>
    Task<int> ExecuteAsync(
        InquiryCommand command,
        CancellationToken cancellationToken = default);
}
