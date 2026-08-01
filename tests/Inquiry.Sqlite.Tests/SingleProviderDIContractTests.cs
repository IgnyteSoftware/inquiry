using Inquiry.Connections;
using Inquiry.DependencyInjection;
using Inquiry.Sqlite.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using System.Data.Common;

namespace Inquiry.Sqlite.Tests;

/// <summary>
/// Pins the single-provider DI contract (audit P2 #9). Inquiry binds
/// <see cref="IInquiryConnectionFactory"/> globally on the service collection, so registering two
/// providers on one container would silently overwrite (last call wins). The provider extensions
/// now throw <see cref="InvalidOperationException"/> with a clear message when this is attempted.
///
/// Uses SQLite (the only provider available without Testcontainers) to exercise the helper, but
/// the helper is shared by every <c>AddInquiry&lt;Provider&gt;</c> extension.
/// </summary>
public sealed class SingleProviderDIContractTests
{
    [Fact]
    public void AddInquirySqliteRejectsAlreadyRegisteredConnectionFactory()
    {
        var services = new ServiceCollection();
        services.AddInquiry();
        services.AddInquirySqlite("DataSource=:memory:");

        var ex = Assert.Throws<InvalidOperationException>(() =>
            services.AddInquirySqlite("DataSource=:memory:"));

        Assert.Contains("IInquiryConnectionFactory", ex.Message);
        Assert.Contains("multiple databases", ex.Message);
    }

    [Fact]
    public void AddInquirySqliteRejectsForeignConnectionFactory()
    {
        var services = new ServiceCollection();
        services.AddInquiry();
        services.AddSingleton<IInquiryConnectionFactory>(new FakeFactory());

        var ex = Assert.Throws<InvalidOperationException>(() =>
            services.AddInquirySqlite("DataSource=:memory:"));

        Assert.Contains("IInquiryConnectionFactory", ex.Message);
    }

    [Fact]
    public void EnsureNoExistingConnectionFactoryPassesOnCollectionWithoutConnectionFactory()
    {
        var services = new ServiceCollection();
        services.AddInquiry();

        InquiryProviderRegistration.EnsureNoExistingConnectionFactory(services, "Sqlite");
    }

    private sealed class FakeFactory : IInquiryConnectionFactory
    {
        public ValueTask<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
    }
}
