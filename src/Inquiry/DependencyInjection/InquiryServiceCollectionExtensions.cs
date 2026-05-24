using Inquiry.Pipeline;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Reflection;

namespace Inquiry.DependencyInjection;

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

        services.TryAddScoped<IInquiry, DefaultInquiry>();
        services.TryAddScoped<IInquiryRequestPipeline, InquiryRequestPipeline>();
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
                var registration = (IInquiryServiceRegistration?)Activator.CreateInstance(registrationType, nonPublic: true);
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
                    typeof(IInquiryServiceRegistration).IsAssignableFrom(type));
        }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types
                .Where(static type => type is not null)
                .Cast<Type>()
                .Where(static type =>
                    !type.IsAbstract &&
                    typeof(IInquiryServiceRegistration).IsAssignableFrom(type));
        }
    }
}
