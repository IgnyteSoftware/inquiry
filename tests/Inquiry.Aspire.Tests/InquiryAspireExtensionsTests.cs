using System.Diagnostics;
using System.Data.Common;
using Inquiry.Connections;
using Inquiry.Interceptors;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using MySqlConnector;
using Npgsql;

namespace Inquiry.Aspire.Tests;

public sealed class InquiryAspireExtensionsTests
{
    public static TheoryData<Action<IHostApplicationBuilder, string>, string> Providers => new()
    {
        { static (builder, name) => builder.AddInquiryMariaDb(name), "MariaDbInquiryConnectionFactory" },
        { static (builder, name) => builder.AddInquiryMySql(name), "MySqlInquiryConnectionFactory" },
        { static (builder, name) => builder.AddInquiryOracle(name), "OracleInquiryConnectionFactory" },
        { static (builder, name) => builder.AddInquiryPostgreSql(name), "PostgreSqlInquiryConnectionFactory" },
        { static (builder, name) => builder.AddInquirySqlite(name), "SqliteInquiryConnectionFactory" },
        { static (builder, name) => builder.AddInquirySqlServer(name), "SqlServerInquiryConnectionFactory" },
    };

    public static TheoryData<Action<IHostApplicationBuilder, string>, Type, string> DataSourceProviders => new()
    {
        { static (builder, name) => builder.AddInquiryMariaDb(name), typeof(MySqlDataSource), "Server=localhost;Database=orders;User ID=root;Password=root" },
        { static (builder, name) => builder.AddInquiryMySql(name), typeof(MySqlDataSource), "Server=localhost;Database=orders;User ID=root;Password=root" },
        { static (builder, name) => builder.AddInquiryOracle(name), typeof(DbDataSource), "User Id=inquiry;Password=inquiry;Data Source=localhost:1521/FREEPDB1" },
        { static (builder, name) => builder.AddInquiryPostgreSql(name), typeof(NpgsqlDataSource), "Host=localhost;Database=orders;Username=postgres;Password=postgres" },
        { static (builder, name) => builder.AddInquirySqlite(name), typeof(DbDataSource), "Data Source=:memory:" },
        { static (builder, name) => builder.AddInquirySqlServer(name), typeof(DbDataSource), "Server=.;Database=orders;Integrated Security=true;TrustServerCertificate=true" },
    };

    [Theory]
    [MemberData(nameof(Providers))]
    public void ResourceNameRegistersProviderTelemetryAndHealthCheck(
        Action<IHostApplicationBuilder, string> register,
        string expectedFactoryType)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:orders"] = ConnectionStringFor(expectedFactoryType),
        });

        register(builder, "orders");
        using var host = builder.Build();

        Assert.Equal(expectedFactoryType, host.Services.GetRequiredService<IInquiryConnectionFactory>().GetType().Name);
        Assert.Contains(
            host.Services.GetServices<IInquiryCommandInterceptor>(),
            interceptor => interceptor.GetType().Name == "InquiryTelemetryInterceptor");
        Assert.Contains(
            host.Services.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations,
            registration => registration.Name == "inquiry");
    }

    [Fact]
    public void MissingResourceConnectionStringIsRejected()
    {
        var builder = Host.CreateApplicationBuilder();

        var exception = Assert.Throws<InvalidOperationException>(() => builder.AddInquiryPostgreSql("orders"));

        Assert.Contains("Connection string 'orders' was not found", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(DataSourceProviders))]
    public void ResourceNameRegistersProviderDataSource(
        Action<IHostApplicationBuilder, string> register,
        Type dataSourceType,
        string connectionString)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:orders"] = connectionString,
        });

        register(builder, "orders");
        using var host = builder.Build();

        Assert.NotNull(host.Services.GetService(dataSourceType));
    }

    [Fact]
    public async Task InquiryActivitySourceIsConnectedToHostTelemetry()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:orders"] = "Data Source=:memory:",
        });
        builder.AddInquirySqlite("orders");
        using var host = builder.Build();

        await host.StartAsync();
        using var source = new ActivitySource("Inquiry");
        using var activity = source.StartActivity("test");
        await host.StopAsync();

        Assert.NotNull(activity);
    }

    private static string ConnectionStringFor(string factoryType) => factoryType switch
    {
        "PostgreSqlInquiryConnectionFactory" => "Host=localhost;Database=orders;Username=postgres;Password=postgres",
        "OracleInquiryConnectionFactory" => "User Id=inquiry;Password=inquiry;Data Source=localhost:1521/FREEPDB1",
        "SqliteInquiryConnectionFactory" => "Data Source=:memory:",
        "SqlServerInquiryConnectionFactory" => "Server=.;Database=orders;Integrated Security=true;TrustServerCertificate=true",
        _ => "Server=localhost;Database=orders;User ID=root;Password=root",
    };
}
