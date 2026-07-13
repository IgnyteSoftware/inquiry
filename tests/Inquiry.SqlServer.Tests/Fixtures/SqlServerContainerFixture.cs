using System;
using System.Threading.Tasks;
using Inquiry.IntegrationTesting;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;
using Xunit;

namespace Inquiry.SqlServer.Tests.Fixtures;

/// <summary>Starts one SQL Server container for the whole assembly. If Docker is unreachable,
/// <see cref="IsAvailable"/> stays false and tests skip rather than fail.</summary>
public sealed class SqlServerContainerFixture : IAsyncLifetime
{
    public const string ImageEnvVarName = "INQUIRY_SQLSERVER_IMAGE";
    private const string DefaultImage =
        "mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04@sha256:c1aa8afe9b06eab64c9774a4802dcd032205d1be785b1fd51e1c0151e7586b74";

    private MsSqlContainer? _container;

    public bool IsAvailable { get; private set; }
    public string? SkipReason { get; private set; }
    public string AdminConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        var configuredImage = Environment.GetEnvironmentVariable(ImageEnvVarName);
        var hasImageOverride = !string.IsNullOrWhiteSpace(configuredImage);
        var selectedImage = hasImageOverride ? configuredImage!.Trim() : DefaultImage;
        var capabilityIsRequired = DockerRequirement.IsRequired() || hasImageOverride;

        Console.WriteLine($"SQL Server integration image: {selectedImage}");

        try
        {
            _container = new MsSqlBuilder(selectedImage).Build();
            await _container.StartAsync();

            if (hasImageOverride)
            {
                var user = await _container.ExecAsync(new[] { "/usr/bin/id", "-u" });
                if (user.ExitCode != 0 || user.Stdout.Trim() == "0")
                {
                    throw new InvalidOperationException(
                        $"The image selected by {ImageEnvVarName} must run as a non-root user. " +
                        $"id -u exited {user.ExitCode} with output '{user.Stdout.Trim()}'.");
                }
            }

            AdminConnectionString = _container.GetConnectionString();

            if (capabilityIsRequired)
            {
                await using var connection = new SqlConnection(AdminConnectionString);
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT FULLTEXTSERVICEPROPERTY('IsFullTextInstalled')";
                var value = await command.ExecuteScalarAsync();
                var isInstalled = value is not null && value != DBNull.Value && Convert.ToInt32(value) == 1;
                SqlServerFullTextPolicy.ShouldSkip(isRequired: true, isInstalled);
            }

            IsAvailable = true;
        }
        catch (Exception ex)
        {
            IsAvailable = false;
            SkipReason = "SQL Server container unavailable (is Docker running?): " + ex.Message;

            if (capabilityIsRequired)
            {
                throw new InvalidOperationException(
                    $"The selected SQL Server integration image '{selectedImage}' failed required startup validation.",
                    ex);
            }
        }

        DockerRequirement.ThrowIfRequiredButUnavailable(IsAvailable, SkipReason);
    }

    public async Task DisposeAsync()
    {
        if (_container is not null) await _container.DisposeAsync();
    }
}
