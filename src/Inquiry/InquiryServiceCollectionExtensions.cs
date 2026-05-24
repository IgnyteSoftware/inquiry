using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registers core Inquiry runtime services.
/// </summary>
public static class InquiryServiceCollectionExtensions
{
    /// <summary>
    /// Registers the core Inquiry request pipeline.
    /// </summary>
    public static IServiceCollection AddInquiryCore(this IServiceCollection services)
    {
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        services.TryAddScoped<Inquiry.IInquiry, Inquiry.DefaultInquiry>();
        services.TryAddScoped<Inquiry.IInquiryRequestPipeline, Inquiry.InquiryRequestPipeline>();
        return services;
    }
}
