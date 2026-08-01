using Inquiry.Pipeline;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Inquiry.DependencyInjection;

/// <summary>
/// Registers core Inquiry runtime services.
/// </summary>
public static class InquiryServiceCollectionExtensions
{
    private const string AssemblyScanRequiresUnreferencedCode =
        "Assembly scanning discovers generated IInquiryServiceRegistration types via reflection, which trimming may remove. " +
        "For trimmed/NativeAOT applications, call the generated AddInquiryGeneratedStores() extension instead.";

    /// <summary>
    /// Registers core Inquiry runtime services.
    /// </summary>
    public static IServiceCollection AddInquiry(this IServiceCollection services)
        => AddInquiryCore(services, configureOptions: null);

    /// <summary>
    /// Registers core Inquiry runtime services, applying the supplied
    /// <see cref="InquiryOptions"/> configuration (e.g. <c>o.PrepareStatements = PreparedStatementMode.None</c>).
    /// </summary>
    public static IServiceCollection AddInquiry(this IServiceCollection services, Action<InquiryOptions> configureOptions)
    {
        if (configureOptions is null)
        {
            throw new ArgumentNullException(nameof(configureOptions));
        }

        return AddInquiryCore(services, configureOptions);
    }

    /// <summary>
    /// Registers core Inquiry runtime services and scans the supplied assemblies for generated
    /// <see cref="IInquiryServiceRegistration"/> implementations. Prefer the generated
    /// <c>AddInquiryGeneratedStores()</c> extension when the stores live in the current assembly.
    /// Assemblies passed explicitly are deduped by identity, so passing one twice is a no-op.
    /// </summary>
    [RequiresUnreferencedCode(AssemblyScanRequiresUnreferencedCode)]
    public static IServiceCollection AddInquiry(this IServiceCollection services, params Assembly[] additionalAssemblies)
    {
        if (additionalAssemblies is null)
        {
            throw new ArgumentNullException(nameof(additionalAssemblies));
        }

        AddInquiryCore(services, configureOptions: null);
        AddGeneratedServices(services, additionalAssemblies);
        return services;
    }

    /// <summary>
    /// Registers core Inquiry runtime services with the supplied <see cref="InquiryOptions"/>
    /// configuration and scans the supplied assemblies for generated
    /// <see cref="IInquiryServiceRegistration"/> implementations. See the <see cref="Assembly"/>-only
    /// overload for the dedupe semantics.
    /// </summary>
    [RequiresUnreferencedCode(AssemblyScanRequiresUnreferencedCode)]
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

        AddInquiryCore(services, configureOptions);
        AddGeneratedServices(services, additionalAssemblies);
        return services;
    }

    private static IServiceCollection AddInquiryCore(
        IServiceCollection services,
        Action<InquiryOptions>? configureOptions)
    {
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        AddOrConfigureOptions(services, configureOptions);

        services.TryAddScoped<IInquiry, DefaultInquiry>();
        services.TryAddScoped<IInquiryRequestPipeline, InquiryRequestPipeline>();

        return services;
    }

    private static void AddOrConfigureOptions(IServiceCollection services, Action<InquiryOptions>? configureOptions)
    {
        for (var i = services.Count - 1; i >= 0; i--)
        {
            if (services[i].ServiceType == typeof(InquiryOptions)
                && services[i].ImplementationInstance is InquiryOptions existingOptions)
            {
                configureOptions?.Invoke(existingOptions);
                return;
            }
        }

        var options = new InquiryOptions();
        configureOptions?.Invoke(options);
        services.Replace(ServiceDescriptor.Singleton(options));
    }

    [RequiresUnreferencedCode(AssemblyScanRequiresUnreferencedCode)]
    private static void AddGeneratedServices(IServiceCollection services, Assembly[] additionalAssemblies)
    {
        // Dedupe by Assembly identity: a caller may pass an Assembly twice, and we must not invoke
        // its registration twice (downstream service registrations typically use TryAdd, which would
        // silently swallow the duplicate, but generated stores include collection-aware Add calls in
        // some shapes - better not to rely on that).
        var seen = new HashSet<Assembly>();
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

    [RequiresUnreferencedCode(AssemblyScanRequiresUnreferencedCode)]
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
