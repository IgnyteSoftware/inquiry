using Inquiry.DependencyInjection;
using Inquiry.Pipeline;
using Microsoft.Extensions.DependencyInjection;
using System.Linq;

namespace Inquiry.Tests;

/// <summary>
/// AddInquiry's default registration is core-only. Generated stores should be registered through
/// the generated AddInquiryGeneratedStores extension; the explicit assembly overload remains as an
/// opt-in fallback for hosts that intentionally want reflective registration.
///
/// These tests pin the fallback contract: plain AddInquiry does not scan the AppDomain, an
/// explicit assembly is scanned, its registrations run, and nothing in the core wiring
/// (pipeline / IInquiry) regresses.
/// </summary>
/// <remarks>
/// Assertions count <see cref="SentinelMarker"/> descriptors on each test's own
/// <see cref="ServiceCollection"/> - never a process-global counter. The per-test collection is
/// local, so the marker count is race-free.
/// </remarks>
public sealed class AddInquiryAssemblyOverloadTests
{
    private static int SentinelMarkerCount(IServiceCollection services)
        => services.Count(sd => sd.ServiceType == typeof(SentinelMarker));

    [Fact]
    public void AddInquiryDoesNotScanLoadedAssemblies()
    {
        var services = new ServiceCollection();

        services.AddInquiry();

        Assert.Equal(0, SentinelMarkerCount(services));
        Assert.Contains(services, sd => sd.ServiceType == typeof(IInquiry));
        Assert.Contains(services, sd => sd.ServiceType == typeof(IInquiryRequestPipeline));

        var provider = services.BuildServiceProvider();
        Assert.Equal(PreparedStatementMode.Auto, provider.GetRequiredService<InquiryOptions>().PrepareStatements);
    }

    [Fact]
    public void AddInquiryWithExplicitAssemblyRunsThatAssemblysRegistration()
    {
        var services = new ServiceCollection();

        services.AddInquiry(typeof(SentinelRegistration).Assembly);

        // The registration ran (it added its marker to this collection).
        Assert.True(SentinelMarkerCount(services) >= 1);

        // And core service descriptors landed (IInquiry isn't resolved here because that needs an
        // IInquiryConnectionFactory from a provider package).
        Assert.Contains(services, sd => sd.ServiceType == typeof(IInquiry));
        Assert.Contains(services, sd => sd.ServiceType == typeof(IInquiryRequestPipeline));
    }

    [Fact]
    public void AddInquiryWithExplicitAssemblyAndOptionsRunsThatAssemblysRegistration()
    {
        var services = new ServiceCollection();

        var prepareCalled = false;
        services.AddInquiry(o => { o.PrepareStatements = PreparedStatementMode.Auto; prepareCalled = true; }, typeof(SentinelRegistration).Assembly);

        Assert.True(prepareCalled);
        Assert.True(SentinelMarkerCount(services) >= 1);
        var provider = services.BuildServiceProvider();
        Assert.Equal(PreparedStatementMode.Auto, provider.GetRequiredService<InquiryOptions>().PrepareStatements);
    }

    [Fact]
    public void RepeatedAddInquiryCallsComposeOptions()
    {
        var services = new ServiceCollection();

        services.AddInquiry(o => o.PrepareStatements = PreparedStatementMode.Auto);
        services.AddInquiry(o => o.ThrowOnConcurrencyConflict = true);

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<InquiryOptions>();
        Assert.Equal(PreparedStatementMode.Auto, options.PrepareStatements);
        Assert.True(options.ThrowOnConcurrencyConflict);
    }

    [Fact]
    public void AddInquiryWithExplicitAssemblyDoesNotDoubleRegisterDuplicateAssemblyArguments()
    {
        var services = new ServiceCollection();

        services.AddInquiry(typeof(SentinelRegistration).Assembly, typeof(SentinelRegistration).Assembly);

        Assert.Equal(1, SentinelMarkerCount(services));
    }

    // Marker service the sentinel registration adds. Counting its descriptors on a per-test
    // ServiceCollection measures how many times the registration ran against that collection.
    private sealed class SentinelMarker
    {
    }

    // Sentinel registration discovered only when the test explicitly passes this assembly.
    private sealed class SentinelRegistration : IInquiryServiceRegistration
    {
        public void AddServices(IServiceCollection services)
        {
            services.AddSingleton<SentinelMarker>();
        }
    }
}
