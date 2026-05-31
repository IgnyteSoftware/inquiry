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
}
