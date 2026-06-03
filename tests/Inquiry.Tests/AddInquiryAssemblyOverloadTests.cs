using Inquiry.DependencyInjection;
using Inquiry.Pipeline;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

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
public sealed class AddInquiryAssemblyOverloadTests
{
    [Fact]
    public void AddInquiryWithExplicitAssemblyRunsThatAssemblysRegistration()
    {
        SentinelRegistration.AddServicesInvocations = 0;
        var services = new ServiceCollection();

        services.AddInquiry(typeof(SentinelRegistration).Assembly);

        // The registration ran (its AddServices side-effect counter incremented).
        Assert.True(SentinelRegistration.AddServicesInvocations > 0);

        // And core service descriptors landed (IInquiry resolves to a concrete factory; we don't
        // resolve it here because that needs an IInquiryConnectionFactory from a provider package).
        var provider = services.BuildServiceProvider();
        Assert.Contains(services, sd => sd.ServiceType == typeof(IInquiry));
        Assert.Contains(services, sd => sd.ServiceType == typeof(IInquiryRequestPipeline));
    }

    [Fact]
    public void AddInquiryWithExplicitAssemblyAndOptionsRunsThatAssemblysRegistration()
    {
        SentinelRegistration.AddServicesInvocations = 0;
        var services = new ServiceCollection();

        var prepareCalled = false;
        services.AddInquiry(o => { o.PrepareStatements = PreparedStatementMode.Auto; prepareCalled = true; }, typeof(SentinelRegistration).Assembly);

        Assert.True(prepareCalled);
        Assert.True(SentinelRegistration.AddServicesInvocations > 0);
        var provider = services.BuildServiceProvider();
        Assert.Equal(PreparedStatementMode.Auto, provider.GetRequiredService<InquiryOptions>().PrepareStatements);
    }

    [Fact]
    public void AddInquiryWithExplicitAssemblyDoesNotDoubleRegisterWhenAssemblyIsAlsoInAppDomain()
    {
        // The test assembly is loaded into AppDomain, so the implicit AppDomain scan
        // already discovers SentinelRegistration. Passing it explicitly too must not
        // run AddServices twice (dedupe by Assembly identity).
        SentinelRegistration.AddServicesInvocations = 0;
        var services = new ServiceCollection();

        services.AddInquiry(typeof(SentinelRegistration).Assembly);

        Assert.Equal(1, SentinelRegistration.AddServicesInvocations);
    }

    // Sentinel registration discovered via AppDomain scan and via explicit-Assembly overload alike.
    // Lives in this test assembly so we don't need a separate fixture project to exercise the API.
    private sealed class SentinelRegistration : IInquiryServiceRegistration
    {
        // Per-test reset; the harness is single-threaded inside a fact.
        internal static int AddServicesInvocations;

        public void AddServices(IServiceCollection services)
        {
            AddServicesInvocations++;
        }
    }
}
