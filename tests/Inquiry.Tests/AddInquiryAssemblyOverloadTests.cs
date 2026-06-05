using Inquiry.DependencyInjection;
using Inquiry.Pipeline;
using Microsoft.Extensions.DependencyInjection;
using System.Linq;

namespace Inquiry.Tests;

/// <summary>
/// AddInquiry's default discovery scans <see cref="AppDomain.CurrentDomain"/>'s loaded
/// assemblies for <see cref="IInquiryServiceRegistration"/> implementations. Stores in
/// a referenced-but-not-yet-loaded assembly are missed (audit P2 #8).
///
/// The explicit-assembly overload lets a host hand the assembly to AddInquiry directly,
/// guaranteeing the scan picks it up regardless of load timing. These tests pin the new
/// overload's contract: an explicit assembly is scanned, its registrations run, and
/// nothing in the core wiring (pipeline / IInquiry) regresses.
/// </summary>
/// <remarks>
/// Assertions count <see cref="SentinelMarker"/> descriptors on each test's OWN
/// <see cref="ServiceCollection"/> — never a process-global counter. <see cref="SentinelRegistration"/>
/// is an <see cref="IInquiryServiceRegistration"/> in this assembly, so the AppDomain scan that runs on
/// every AddInquiry call (from any test class) invokes it; a shared static counter would be mutated by
/// tests running in parallel and flake. The per-test ServiceCollection is local, so the marker count is
/// race-free.
/// </remarks>
public sealed class AddInquiryAssemblyOverloadTests
{
    private static int SentinelMarkerCount(IServiceCollection services)
        => services.Count(sd => sd.ServiceType == typeof(SentinelMarker));

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
    public void AddInquiryWithExplicitAssemblyDoesNotDoubleRegisterWhenAssemblyIsAlsoInAppDomain()
    {
        // The test assembly is loaded into AppDomain, so the implicit AppDomain scan already
        // discovers SentinelRegistration. Passing it explicitly too must not run AddServices twice
        // (dedupe by Assembly identity) — so exactly one marker lands in this collection.
        var services = new ServiceCollection();

        services.AddInquiry(typeof(SentinelRegistration).Assembly);

        Assert.Equal(1, SentinelMarkerCount(services));
    }

    // Marker service the sentinel registration adds. Counting its descriptors on a per-test
    // ServiceCollection measures how many times the registration ran against THAT collection.
    private sealed class SentinelMarker
    {
    }

    // Sentinel registration discovered via AppDomain scan and via explicit-Assembly overload alike.
    // Lives in this test assembly so we don't need a separate fixture project to exercise the API.
    private sealed class SentinelRegistration : IInquiryServiceRegistration
    {
        public void AddServices(IServiceCollection services)
        {
            services.AddSingleton<SentinelMarker>();
        }
    }
}
