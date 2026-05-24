using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Reflection;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registers core Inquiry runtime services.
/// </summary>
public static class InquiryServiceCollectionExtensions
{
    /// <summary>
    /// Registers Inquiry runtime services and generated stores/materializers.
    /// </summary>
    public static IServiceCollection AddInquiry(this IServiceCollection services)
    {
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        services.TryAddScoped<Inquiry.IInquiry, Inquiry.DefaultInquiry>();
        services.TryAddScoped<Inquiry.IInquiryRequestPipeline, Inquiry.InquiryRequestPipeline>();
        AddGeneratedServices(services);
        return services;
    }

    private static void AddGeneratedServices(IServiceCollection services)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.IsDynamic)
            {
                continue;
            }

            foreach (var registrationType in GetRegistrationTypes(assembly))
            {
                var registration = (Inquiry.IInquiryServiceRegistration?)Activator.CreateInstance(registrationType, nonPublic: true);
                registration?.AddServices(services);
            }
        }
    }

    private static IEnumerable<Type> GetRegistrationTypes(Assembly assembly)
    {
        try
        {
            return assembly
                .GetTypes()
                .Where(static type =>
                    !type.IsAbstract &&
                    typeof(Inquiry.IInquiryServiceRegistration).IsAssignableFrom(type));
        }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types
                .Where(static type => type is not null)
                .Cast<Type>()
                .Where(static type =>
                    !type.IsAbstract &&
                    typeof(Inquiry.IInquiryServiceRegistration).IsAssignableFrom(type));
        }
    }
}
