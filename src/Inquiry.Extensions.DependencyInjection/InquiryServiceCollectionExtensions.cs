using Microsoft.Extensions.DependencyInjection;

namespace Inquiry;

public static class InquiryServiceCollectionExtensions
{
    public static IServiceCollection AddInquiry(
        this IServiceCollection services,
        Action<InquiryOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new InquiryOptions();
        configure(options);

        if (options.Provider is null)
        {
            throw new InquiryValidationException(
                "Inquiry requires a provider. Reference an Inquiry provider package and configure one with UseProvider or a provider-specific extension method.");
        }

        services.AddSingleton(options);
        services.AddSingleton(options.Logging);
        services.AddSingleton(options.Conventions);
        services.AddSingleton(options.Provider);
        services.AddSingleton(provider => new InquiryMetadataRegistry(provider.GetRequiredService<InquiryConventionOptions>()));
        services.AddScoped<IInquiryClient>(provider =>
        {
            var configured = provider.GetRequiredService<InquiryOptions>();
            var middleware = configured.Pipeline.Middleware
                .Select(registration => registration.Create(
                    provider,
                    static (services, type) => (IInquiryMiddleware)ActivatorUtilities.CreateInstance(services, type)))
                .ToArray();

            return new InquiryClient(
                configured.GetConnectionFactory(),
                configured.Provider!,
                provider.GetRequiredService<InquiryMetadataRegistry>(),
                middleware,
                provider,
                configured.OwnsConnections);
        });

        return services;
    }
}
