using Inquiry.Connections;
using Inquiry.Sqlite.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Inquiry.Tests;

/// <summary>
/// The IConfiguration-based registration overloads resolve the connection string from
/// <c>ConnectionStrings:{name}</c> and delegate to the string-based overloads.
///
/// Only the SQLite provider is exercised here: the other four providers' configuration overloads
/// are identical three-line delegations to their string-based overloads, so compiling them is
/// sufficient coverage.
/// </summary>
public sealed class ConfigurationBindingTests
{
    private static IConfiguration BuildConfiguration(params (string Key, string Value)[] entries)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(entries.Select(e => new KeyValuePair<string, string?>(e.Key, e.Value)))
            .Build();

    [Fact]
    public async Task DefaultConnectionStringNameRegistersWorkingFactory()
    {
        var configuration = BuildConfiguration(("ConnectionStrings:Inquiry", "Data Source=:memory:"));

        var services = new ServiceCollection();
        services.AddInquirySqlite(configuration);

        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IInquiryConnectionFactory>();

        await using var connection = await factory.OpenConnectionAsync();
        Assert.Equal(System.Data.ConnectionState.Open, connection.State);
    }

    [Fact]
    public async Task CustomConnectionStringNameResolves()
    {
        var configuration = BuildConfiguration(("ConnectionStrings:Reporting", "Data Source=:memory:"));

        var services = new ServiceCollection();
        services.AddInquirySqlite(configuration, connectionStringName: "Reporting");

        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IInquiryConnectionFactory>();

        await using var connection = await factory.OpenConnectionAsync();
        Assert.Equal(System.Data.ConnectionState.Open, connection.State);
    }

    [Fact]
    public void MissingConnectionStringThrowsActionableMessage()
    {
        var configuration = BuildConfiguration();

        var services = new ServiceCollection();
        var ex = Assert.Throws<InvalidOperationException>(() => services.AddInquirySqlite(configuration, connectionStringName: "Missing"));

        Assert.Contains("ConnectionStrings:", ex.Message);
        Assert.Contains("Missing", ex.Message);
    }

    [Fact]
    public void NullConfigurationThrows()
    {
        var services = new ServiceCollection();
        Assert.Throws<ArgumentNullException>(() => services.AddInquirySqlite(configuration: null!));
    }
}
