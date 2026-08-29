using Inquiry.DependencyInjection;
using Inquiry.Diagnostics;
using Inquiry.MariaDb.DependencyInjection;
using Inquiry.MySql.DependencyInjection;
using Inquiry.Oracle.DependencyInjection;
using Inquiry.PostgreSql.DependencyInjection;
using Inquiry.Sqlite.DependencyInjection;
using Inquiry.SqlServer.DependencyInjection;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;
using Npgsql;
using Oracle.ManagedDataAccess.Client;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Registers Inquiry providers from Aspire resource connection names.
/// </summary>
public static class InquiryAspireExtensions
{
    /// <summary>Registers the MariaDB provider, telemetry, and health check.</summary>
    public static void AddInquiryMariaDb(this IHostApplicationBuilder builder, string resourceName)
    {
        Validate(builder, resourceName);
        var dataSource = new MySqlDataSourceBuilder(GetRequiredConnectionString(builder, resourceName)).Build();
        builder.Services.AddSingleton(_ => dataSource);
        builder.Services.AddInquiryMariaDb(dataSource);
        AddAspireDefaults(builder);
    }

    /// <summary>Registers the MySQL provider, telemetry, and health check.</summary>
    public static void AddInquiryMySql(this IHostApplicationBuilder builder, string resourceName)
    {
        Validate(builder, resourceName);
        var connectionString = new MySqlConnectionStringBuilder(GetRequiredConnectionString(builder, resourceName))
        {
            AllowUserVariables = true,
        }.ConnectionString;
        var dataSource = new MySqlDataSourceBuilder(connectionString).Build();
        builder.Services.AddSingleton(_ => dataSource);
        builder.Services.AddInquiryMySql(dataSource);
        AddAspireDefaults(builder);
    }

    /// <summary>Registers the Oracle provider, telemetry, and health check.</summary>
    public static void AddInquiryOracle(this IHostApplicationBuilder builder, string resourceName)
    {
        Validate(builder, resourceName);
        var dataSource = OracleClientFactory.Instance.CreateDataSource(GetRequiredConnectionString(builder, resourceName));
        builder.Services.AddSingleton(_ => dataSource);
        builder.Services.AddInquiryOracle(dataSource);
        AddAspireDefaults(builder);
    }

    /// <summary>Registers the PostgreSQL provider, telemetry, and health check.</summary>
    public static void AddInquiryPostgreSql(this IHostApplicationBuilder builder, string resourceName)
    {
        Validate(builder, resourceName);
        var dataSource = new NpgsqlDataSourceBuilder(GetRequiredConnectionString(builder, resourceName)).Build();
        builder.Services.AddSingleton(_ => dataSource);
        builder.Services.AddInquiryPostgreSql(dataSource);
        AddAspireDefaults(builder);
    }

    /// <summary>Registers the SQLite provider, telemetry, and health check.</summary>
    public static void AddInquirySqlite(this IHostApplicationBuilder builder, string resourceName)
    {
        Validate(builder, resourceName);
        var dataSource = SqliteFactory.Instance.CreateDataSource(GetRequiredConnectionString(builder, resourceName));
        builder.Services.AddSingleton(_ => dataSource);
        builder.Services.AddInquirySqlite(dataSource);
        AddAspireDefaults(builder);
    }

    /// <summary>Registers the SQL Server provider, telemetry, and health check.</summary>
    public static void AddInquirySqlServer(this IHostApplicationBuilder builder, string resourceName)
    {
        Validate(builder, resourceName);
        var dataSource = SqlClientFactory.Instance.CreateDataSource(GetRequiredConnectionString(builder, resourceName));
        builder.Services.AddSingleton(_ => dataSource);
        builder.Services.AddInquirySqlServer(dataSource);
        AddAspireDefaults(builder);
    }

    private static void AddAspireDefaults(IHostApplicationBuilder builder)
    {
        builder.Services.AddInquiryTelemetry();
        builder.Services.AddHealthChecks().AddInquiry();
        builder.Services.AddOpenTelemetry()
            .WithTracing(tracing => tracing.AddSource(InquiryTelemetry.ActivitySourceName))
            .WithMetrics(metrics => metrics.AddMeter(InquiryTelemetry.MeterName));
    }

    private static void Validate(IHostApplicationBuilder builder, string resourceName)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceName);
    }

    private static string GetRequiredConnectionString(IHostApplicationBuilder builder, string resourceName)
    {
        var connectionString = builder.Configuration.GetConnectionString(resourceName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Connection string '{resourceName}' was not found in configuration. " +
                $"Add a 'ConnectionStrings:{resourceName}' entry, or pass the name of a configured connection string.");
        }

        return connectionString;
    }
}
