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
        => AddInquiryCore(services, configureOptions: null, additionalAssemblies: null);

    /// <summary>
    /// Registers Inquiry runtime services and generated stores/materializers, applying the supplied
    /// <see cref="InquiryOptions"/> configuration (e.g. <c>o.PrepareStatements = PreparedStatementMode.Auto</c>).
    /// </summary>
    public static IServiceCollection AddInquiry(this IServiceCollection services, Action<InquiryOptions> configureOptions)
    {
        if (configureOptions is null)
        {
            throw new ArgumentNullException(nameof(configureOptions));
        }

        return AddInquiryCore(services, configureOptions, additionalAssemblies: null);
    }

    /// <summary>
    /// Registers Inquiry runtime services and scans the supplied assemblies (in addition to
    /// <see cref="AppDomain.CurrentDomain"/>) for generated <see cref="IInquiryServiceRegistration"/>
    /// implementations. Use this overload when stores live in a referenced assembly that is not
    /// guaranteed to be loaded by the time AddInquiry runs — passing it explicitly forces the scan.
    /// Assemblies passed explicitly are deduped against the AppDomain scan, so passing one that is
    /// already loaded is a no-op (its registration still runs exactly once).
    /// </summary>
    public static IServiceCollection AddInquiry(this IServiceCollection services, params Assembly[] additionalAssemblies)
    {
        if (additionalAssemblies is null)
        {
            throw new ArgumentNullException(nameof(additionalAssemblies));
        }

        return AddInquiryCore(services, configureOptions: null, additionalAssemblies);
    }

    /// <summary>
    /// Registers Inquiry runtime services with the supplied <see cref="InquiryOptions"/> configuration
    /// and scans the supplied assemblies (in addition to <see cref="AppDomain.CurrentDomain"/>) for
    /// generated <see cref="IInquiryServiceRegistration"/> implementations. See the <see cref="Assembly"/>-
    /// only overload for the dedupe semantics.
    /// </summary>
    public static IServiceCollection AddInquiry(this IServiceCollection services, Action<InquiryOptions> configureOptions, params Assembly[] additionalAssemblies)
    {
        if (configureOptions is null)
        {
            throw new ArgumentNullException(nameof(configureOptions));
        }
        if (additionalAssemblies is null)
        {
            throw new ArgumentNullException(nameof(additionalAssemblies));
        }

        return AddInquiryCore(services, configureOptions, additionalAssemblies);
    }

    private static IServiceCollection AddInquiryCore(
        IServiceCollection services,
        Action<InquiryOptions>? configureOptions,
        Assembly[]? additionalAssemblies)
    {
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        var options = new InquiryOptions();
        configureOptions?.Invoke(options);
        services.TryAddSingleton(options);

        services.TryAddScoped<IInquiry, DefaultInquiry>();
        services.TryAddScoped<IInquiryRequestPipeline, InquiryRequestPipeline>();
        AddGeneratedServices(services, additionalAssemblies);
        return services;
    }

    private static void AddGeneratedServices(IServiceCollection services, Assembly[]? additionalAssemblies)
    {
        // Dedupe by Assembly identity: a caller may pass an Assembly the AppDomain scan already
        // visits, and we must not invoke its registration twice (downstream service registrations
        // typically use TryAdd, which would silently swallow the duplicate, but generated stores
        // include collection-aware Add calls in some shapes — better not to rely on that).
        var seen = new HashSet<Assembly>();
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.IsDynamic || !seen.Add(assembly))
            {
                continue;
            }

            foreach (var registrationType in GetRegistrationTypes(assembly))
            {
                var registration = (IInquiryServiceRegistration?)Activator.CreateInstance(registrationType, nonPublic: true);
                registration?.AddServices(services);
            }
        }

        if (additionalAssemblies is null)
        {
            return;
        }

        foreach (var assembly in additionalAssemblies)
        {
            if (assembly is null || assembly.IsDynamic || !seen.Add(assembly))
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
