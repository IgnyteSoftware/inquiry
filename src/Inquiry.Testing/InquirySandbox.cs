using Inquiry.Transactions;
using Microsoft.Extensions.DependencyInjection;
using System.Data;
using System.Runtime.ExceptionServices;

namespace Inquiry.Testing;

/// <summary>
/// Runs test code in a fresh dependency-injection scope and an Inquiry transaction that is
/// always rolled back. Generated stores resolved from the sandbox services automatically join
/// the transaction through Inquiry's ambient transaction context.
/// </summary>
public sealed class InquirySandbox
{
    private static readonly AsyncLocal<bool> Active = new();
    private readonly IServiceProvider _services;
    private readonly IsolationLevel _isolationLevel;

    /// <summary>
    /// Creates a sandbox backed by an application's root service provider.
    /// </summary>
    public InquirySandbox(
        IServiceProvider services,
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _isolationLevel = isolationLevel;
    }

    /// <summary>
    /// Runs an operation in a fresh service scope and rolls its transaction back afterward.
    /// </summary>
    public Task RunAsync(
        Func<InquirySandboxContext, CancellationToken, Task> operation,
        CancellationToken cancellationToken = default)
    {
        if (operation is null) throw new ArgumentNullException(nameof(operation));

        return RunAsync<object?>(async (context, token) =>
        {
            await operation(context, token).ConfigureAwait(false);
            return null;
        }, cancellationToken);
    }

    /// <summary>
    /// Runs an operation in a fresh service scope, returns its result, and rolls its transaction
    /// back afterward.
    /// </summary>
    public async Task<TResult> RunAsync<TResult>(
        Func<InquirySandboxContext, CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default)
    {
        if (operation is null) throw new ArgumentNullException(nameof(operation));
        if (Active.Value)
        {
            throw new InvalidOperationException(
                "An Inquiry sandbox cannot be nested in the same async context. " +
                "Use IInquiry.BeginTransactionAsync inside the sandbox when a savepoint is required.");
        }

        var scope = _services.CreateAsyncScope();
        Active.Value = true;
        IInquiryTransaction? transaction = null;
        ExceptionDispatchInfo? primaryFailure = null;
        TResult result = default!;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var inquiry = scope.ServiceProvider.GetRequiredService<IInquiry>();
            transaction = await inquiry.BeginTransactionAsync(_isolationLevel, cancellationToken).ConfigureAwait(false);
            var context = new InquirySandboxContext(scope.ServiceProvider, inquiry);
            result = await operation(context, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            primaryFailure = ExceptionDispatchInfo.Capture(exception);
        }

        var cleanupFailures = new List<Exception>();
        if (transaction is not null)
        {
            try
            {
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                cleanupFailures.Add(exception);
            }

            try
            {
                await transaction.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                cleanupFailures.Add(exception);
            }
        }

        try
        {
            await scope.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            cleanupFailures.Add(exception);
        }
        finally
        {
            Active.Value = false;
        }

        if (primaryFailure is not null)
        {
            primaryFailure.Throw();
        }

        if (cleanupFailures.Count == 1)
        {
            ExceptionDispatchInfo.Capture(cleanupFailures[0]).Throw();
        }

        if (cleanupFailures.Count > 1)
        {
            throw new AggregateException("Inquiry sandbox rollback or disposal failed.", cleanupFailures);
        }

        return result;
    }
}

/// <summary>
/// Services and the ambient transactional Inquiry facade available during a sandbox run.
/// </summary>
public sealed class InquirySandboxContext
{
    internal InquirySandboxContext(IServiceProvider services, IInquiry transaction)
    {
        Services = services;
        Transaction = transaction;
    }

    /// <summary>Gets the fresh scoped service provider for this run.</summary>
    public IServiceProvider Services { get; }

    /// <summary>
    /// Gets the scoped Inquiry facade whose operations use the sandbox transaction. The root
    /// transaction handle is intentionally not exposed, so the sandbox cannot be committed.
    /// </summary>
    public IInquiry Transaction { get; }
}
