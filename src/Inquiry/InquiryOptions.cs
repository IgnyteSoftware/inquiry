namespace Inquiry;

/// <summary>
/// Runtime configuration for Inquiry, supplied via the
/// <see cref="DependencyInjection.InquiryServiceCollectionExtensions.AddInquiry(Microsoft.Extensions.DependencyInjection.IServiceCollection, System.Action{InquiryOptions})"/>
/// overload.
/// </summary>
public sealed class InquiryOptions
{
    /// <summary>
    /// Gets or sets whether generated commands are prepared before execution. Defaults to
    /// <see cref="PreparedStatementMode.None"/>.
    /// </summary>
    public PreparedStatementMode PrepareStatements { get; set; } = PreparedStatementMode.None;

    /// <summary>
    /// Gets or sets whether a 0-row UPDATE/DELETE on an optimistic-concurrency token entity (W6)
    /// throws <see cref="InquiryConcurrencyException"/> instead of returning <see langword="false"/>
    /// (or a <see langword="null"/> <c>ReturnEntity</c> result). Defaults to <see langword="false"/>,
    /// preserving the backward-compatible "not found" contract.
    /// </summary>
    public bool ThrowOnConcurrencyConflict { get; set; }
}
