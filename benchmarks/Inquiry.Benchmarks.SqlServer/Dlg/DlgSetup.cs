using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Data.SqlClient;

namespace Inquiry.Benchmarks.SqlServer.Dlg;

/// <summary>
/// Bridges the generated DLG datalayer into the benchmark's Testcontainer: applies DLG's stored
/// procedures, then writes the <c>.config</c> DLG self-loads via <c>new DatabaseHelper(null)</c>.
/// </summary>
public static class DlgSetup
{
    // Resource name = {RootNamespace}.{file}. DLG's root namespace is Inquiry.Benchmarks.DLG.
    private const string ScriptResourceName = "Inquiry.Benchmarks.DLG.SQLScript.sql";

    /// <summary>Reads DLG's embedded SQLScript.sql and runs it batch-by-batch (split on GO).</summary>
    public static async Task ApplyStoredProceduresAsync(string connectionString)
    {
        var script = ReadEmbeddedScript();
        var batches = Regex.Split(script, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase);

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        foreach (var batch in batches)
        {
            var trimmed = batch.Trim();
            if (trimmed.Length == 0) continue;

            await using var command = connection.CreateCommand();
            command.CommandText = trimmed;
            try
            {
                await command.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
            catch (SqlException ex)
            {
                var preview = trimmed.Length > 200 ? trimmed[..200] : trimmed;
                throw new InvalidOperationException(
                    $"DLG SQLScript.sql batch failed: {ex.Message}\n--- batch (first 200 chars) ---\n{preview}", ex);
            }
        }
    }

    /// <summary>
    /// Writes <c>Inquiry.Benchmarks.DLG.config</c> next to the running assembly so DLG's
    /// ConfigurationHelper self-loads this connection string. MUST run before the first DLG call —
    /// ConfigurationHelper caches statically. Uses providerName="Microsoft.Data.SqlClient"; DLG's
    /// provider switch throws on "System.Data.SqlClient".
    /// </summary>
    public static void PrimeConfig(string connectionString)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Inquiry.Benchmarks.DLG.config");

        var config = new XElement("configuration",
            new XElement("connectionStrings",
                ConnEntry("Development", connectionString),
                ConnEntry("BackupDevelopment", connectionString)),
            new XElement("appSettings",
                AppSetting("ConnectionStringToUse", "Development"),
                AppSetting("BackupConnectionStringToUse", "BackupDevelopment"),
                AppSetting("ShouldUseBackupServer", "false")));

        new XDocument(new XDeclaration("1.0", "utf-8", null), config).Save(path);
    }

    private static XElement ConnEntry(string name, string connectionString) =>
        new("add",
            new XAttribute("name", name),
            new XAttribute("connectionString", connectionString),
            new XAttribute("providerName", "Microsoft.Data.SqlClient"));

    private static XElement AppSetting(string key, string value) =>
        new("add", new XAttribute("key", key), new XAttribute("value", value));

    private static string ReadEmbeddedScript()
    {
        var assembly = typeof(Inquiry.Benchmarks.DLG.DatabaseHelper).Assembly;
        using var stream = assembly.GetManifestResourceStream(ScriptResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{ScriptResourceName}' not found in {assembly.GetName().Name}. " +
                "Ensure SQLScript.sql is <EmbeddedResource> in Inquiry.Benchmarks.DLG.csproj.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
