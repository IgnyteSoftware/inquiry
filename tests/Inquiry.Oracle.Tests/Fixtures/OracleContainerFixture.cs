using System;
using System.Threading.Tasks;
using DotNet.Testcontainers.Configurations;
using Inquiry.IntegrationTesting;
using Oracle.ManagedDataAccess.Client;
using Testcontainers.Oracle;
using Xunit;

namespace Inquiry.Oracle.Tests.Fixtures;

/// <summary>Starts one Oracle container for the whole assembly. If Docker is unreachable,
/// <see cref="IsAvailable"/> stays false and tests skip rather than fail.</summary>
/// <remarks>
/// The container's default connection string authenticates as the limited <c>oracle</c> application
/// user (CONNECT + RESOURCE only), which cannot <c>CREATE USER</c>. The throwaway-schema harness needs
/// admin rights, so <see cref="AdminConnectionString"/> rewrites the user to <c>SYSTEM</c>; in the
/// gvenzl image the SYSTEM password equals the configured <c>ORACLE_PASSWORD</c> (the same password
/// Testcontainers sets for the app user), so only the user id needs swapping.
/// </remarks>
public sealed class OracleContainerFixture : IAsyncLifetime
{
    public const string ImageEnvironmentVariable = "INQUIRY_ORACLE_IMAGE";
    public const string DefaultImage =
        "gvenzl/oracle-xe@sha256:f82bccdf6020d27373fdf0e93046b63eb3f777a0289e329d9839feebaf4555de";

    private OracleContainer? _container;

    public bool IsAvailable { get; private set; }
    public string? SkipReason { get; private set; }
    public string AdminConnectionString { get; private set; } = string.Empty;
    public string SelectedImage { get; private set; } = string.Empty;
    public string ImageDigest { get; private set; } = string.Empty;
    public string ServerVersion { get; private set; } = string.Empty;
    public string ReadinessEvidence { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        var configuredImage = Environment.GetEnvironmentVariable(ImageEnvironmentVariable);
        var hasImageOverride = !string.IsNullOrWhiteSpace(configuredImage);
        var capabilityIsRequired = IsCapabilityRequired(DockerRequirement.IsRequired(), configuredImage);
        SelectedImage = hasImageOverride ? configuredImage!.Trim() : DefaultImage;

        try
        {
            // Use the XE image whose service name (XEPDB1) matches what Testcontainers.Oracle bakes into
            // its connection string. The oracle-free image serves FREEPDB1 instead, so its generated
            // connection string fails with ORA-12514 (service not known).
            _container = new OracleBuilder(SelectedImage).Build();
            await _container.StartAsync();
            ImageDigest = await ResolveImageDigestAsync(_container);
            AdminConnectionString = new OracleConnectionStringBuilder(_container.GetConnectionString())
            {
                UserID = "SYSTEM",
            }.ToString();

            // The faststart image's listener accepts connections a moment before the database service is
            // registered, so an immediate SYSTEM connect can hit ORA-12514 ("listener does not currently
            // know of service"). Poll until the service is actually serving before declaring availability.
            ServerVersion = await WaitForServiceAsync(AdminConnectionString);
            ReadinessEvidence = "SYSTEM connection and SELECT 1 FROM dual succeeded";
            IsAvailable = true;
            Console.WriteLine(
                $"Oracle fixture ready. image={SelectedImage}; digest={ImageDigest}; server={ServerVersion}; evidence={ReadinessEvidence}");
        }
        catch (Exception ex)
        {
            IsAvailable = false;
            SkipReason = "Oracle container unavailable (is Docker running?): " + ex.Message;

            if (capabilityIsRequired)
            {
                throw new InvalidOperationException(
                    $"The selected Oracle integration image '{SelectedImage}' failed required startup validation.",
                    ex);
            }
        }

        DockerRequirement.ThrowIfRequiredButUnavailable(IsAvailable, SkipReason);
    }

    internal static bool IsCapabilityRequired(bool dockerIsRequired, string? configuredImage)
        => dockerIsRequired || !string.IsNullOrWhiteSpace(configuredImage);

    private static async Task<string> ResolveImageDigestAsync(OracleContainer container)
    {
        using var client = TestcontainersSettings.OS.DockerEndpointAuthConfig
            .GetDockerClientBuilder(Guid.NewGuid())
            .Build();
        var containerInspect = await client.Containers.InspectContainerAsync(container.Id);
        var imageInspect = await client.Images.InspectImageAsync(containerInspect.Image);
        var repositoryDigest = imageInspect.RepoDigests?.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(repositoryDigest))
        {
            return repositoryDigest;
        }

        if (!string.IsNullOrWhiteSpace(imageInspect.ID))
        {
            return imageInspect.ID;
        }

        throw new InvalidOperationException($"Docker did not report a resolved digest for image '{container.Image.FullName}'.");
    }

    private static async Task<string> WaitForServiceAsync(string connectionString)
    {
        const int maxAttempts = 30;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var connection = new OracleConnection(connectionString);
                await connection.OpenAsync();
                await using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT 1 FROM dual";
                await cmd.ExecuteScalarAsync();

                cmd.CommandText = "SELECT BANNER FROM V$VERSION WHERE BANNER LIKE 'Oracle Database%' FETCH FIRST 1 ROW ONLY";
                return Convert.ToString(await cmd.ExecuteScalarAsync()) ?? "unknown";
            }
            catch (OracleException) when (attempt < maxAttempts)
            {
                await Task.Delay(2000);
            }
        }
    }

    public async Task DisposeAsync()
    {
        if (_container is not null) await _container.DisposeAsync();
    }
}
