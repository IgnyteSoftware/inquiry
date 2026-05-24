using Inquiry.Commands;

namespace Inquiry;

/// <summary>
/// Provides simple database access for user-defined Inquiry stores and application services.
/// </summary>
public interface IInquiry
{
    /// <summary>
    /// Executes a SQL query and streams mapped entities.
    /// </summary>
    IAsyncEnumerable<TEntity> QueryAsync<TEntity>(
        string commandText,
        CancellationToken cancellationToken = default)
        where TEntity : class;

    /// <summary>
    /// Executes a SQL query with parameters and streams mapped entities.
    /// </summary>
    IAsyncEnumerable<TEntity> QueryAsync<TEntity>(
        string commandText,
        object? parameters,
        CancellationToken cancellationToken = default)
        where TEntity : class;

    /// <summary>
    /// Executes a SQL query and streams mapped entities.
    /// </summary>
    IAsyncEnumerable<TEntity> QueryAsync<TEntity>(
        InquiryCommandDefinition command,
        CancellationToken cancellationToken = default)
        where TEntity : class;

    /// <summary>
    /// Executes a SQL query and returns the first mapped entity, or <see langword="null"/> when no row is returned.
    /// </summary>
    Task<TEntity?> QuerySingleOrDefaultAsync<TEntity>(
        string commandText,
        CancellationToken cancellationToken = default)
        where TEntity : class;

    /// <summary>
    /// Executes a SQL query with parameters and returns the first mapped entity, or <see langword="null"/> when no row is returned.
    /// </summary>
    Task<TEntity?> QuerySingleOrDefaultAsync<TEntity>(
        string commandText,
        object? parameters,
        CancellationToken cancellationToken = default)
        where TEntity : class;

    /// <summary>
    /// Executes a SQL query and returns the first mapped entity, or <see langword="null"/> when no row is returned.
    /// </summary>
    Task<TEntity?> QuerySingleOrDefaultAsync<TEntity>(
        InquiryCommandDefinition command,
        CancellationToken cancellationToken = default)
        where TEntity : class;

    /// <summary>
    /// Executes a SQL command and returns the affected row count.
    /// </summary>
    Task<int> ExecuteAsync(
        string commandText,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a SQL command with parameters and returns the affected row count.
    /// </summary>
    Task<int> ExecuteAsync(
        string commandText,
        object? parameters,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a SQL command and returns the affected row count.
    /// </summary>
    Task<int> ExecuteAsync(
        InquiryCommandDefinition command,
        CancellationToken cancellationToken = default);
}
