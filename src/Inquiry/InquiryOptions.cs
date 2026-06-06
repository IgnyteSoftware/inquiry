namespace Inquiry;

/// <summary>
/// Runtime configuration for Inquiry, supplied via the
/// <see cref="DependencyInjection.InquiryServiceCollectionExtensions.AddInquiry(Microsoft.Extensions.DependencyInjection.IServiceCollection, System.Action{InquiryOptions})"/>
/// overload.
/// </summary>
public sealed class InquiryOptions
{
    /// <summary>
    /// Default maximum number of parameters Inquiry will bind into a single generated command.
    /// </summary>
    public const int DefaultMaxParametersPerCommand = 2000;

    /// <summary>
    /// Gets or sets whether generated commands are prepared before execution. Defaults to
    /// <see cref="PreparedStatementMode.Auto"/>.
    /// </summary>
    public PreparedStatementMode PrepareStatements { get; set; } = PreparedStatementMode.Auto;

    /// <summary>
    /// Gets or sets whether a 0-row UPDATE/DELETE on an optimistic-concurrency token entity
    /// throws <see cref="InquiryConcurrencyException"/> instead of returning <see langword="false"/>
    /// (or a <see langword="null"/> <c>ReturnEntity</c> result). Defaults to <see langword="false"/>,
    /// preserving the backward-compatible "not found" contract.
    /// </summary>
    public bool ThrowOnConcurrencyConflict { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of parameters Inquiry will bind into one generated command.
    /// This bounds <c>Compare.In</c>, batch delete, batch insert, and batch update expansion before a
    /// provider-specific parameter cap is hit. Defaults to <see cref="DefaultMaxParametersPerCommand"/>.
    /// </summary>
    public int MaxParametersPerCommand { get; set; } = DefaultMaxParametersPerCommand;
}
