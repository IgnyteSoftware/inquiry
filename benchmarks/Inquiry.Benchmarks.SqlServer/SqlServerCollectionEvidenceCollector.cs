using System.Data;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Inquiry.Benchmarks.Contracts;
using Inquiry.Benchmarks.Contracts.Evidence;
using Inquiry.Benchmarks.Contracts.Fixtures;
using Microsoft.Data.SqlClient;

namespace Inquiry.Benchmarks.SqlServer;

public static class SqlServerCollectionEvidenceCollector
{
    public static async Task<SqlServerCollectionEvidence> CollectAsync(SqlServerCollectionBenchmarkDatabase database)
    {
        var logicalReads = new List<SqlServerCollectionLogicalReadEvidence>();
        foreach (var scenario in SqlServerCollectionScenarioCatalog.Scenarios)
        {
            await ClearDatabasePlanCacheAsync(database.ConnectionString).ConfigureAwait(false);
            var command = CommandShape(scenario.Transport, scenario.Cardinality);
            await ExecuteAsync(database, scenario.Transport, scenario.Cardinality).ConfigureAwait(false);
            await ExecuteAsync(database, scenario.Transport, scenario.Cardinality).ConfigureAwait(false);
            var stats = await ReadStatsAsync(database.ConnectionString, command, expectedCachedPlanCount: 1,
                expectedExecutionCount: 2).ConfigureAwait(false);
            logicalReads.Add(new(scenario.Transport, scenario.Cardinality, stats.LastLogicalReads,
                stats.ExecutionCount, HashBinary(stats.QueryHash), HashBinary(stats.PlanHash)));
        }

        var plans = new List<SqlServerCollectionPlanEvidence>();
        foreach (var transport in Enum.GetValues<SqlServerCollectionTransport>())
        {
            await ClearDatabasePlanCacheAsync(database.ConnectionString).ConfigureAwait(false);
            for (var index = 0; index < SqlServerCollectionScenarioCatalog.Cardinalities.Count; index++)
            {
                var cardinality = SqlServerCollectionScenarioCatalog.Cardinalities[index];
                var command = CommandShape(transport, cardinality);
                await ExecuteAsync(database, transport, cardinality).ConfigureAwait(false);
                var expectedCachedPlanCount = transport == SqlServerCollectionTransport.ScalarExpansion ? index + 1 : 1;
                var stats = await ReadStatsAsync(database.ConnectionString, command, expectedCachedPlanCount).ConfigureAwait(false);
                plans.Add(new(transport, cardinality, HashText(stats.CachedStatementSql),
                    HashText(CachedParameterMetadata(stats.CachedBatchSql, command.Sql, stats.QueryPlanXml)), HashBinary(stats.QueryHash),
                    HashBinary(stats.PlanHash), stats.CachedPlanCount));
            }
        }

        return new SqlServerCollectionEvidence(
            SqlServerCollectionEvidenceSchema.Version,
            DateTimeOffset.UtcNow,
            DatabaseImageCatalog.GetRequired("sqlserver").Digest,
            NorthwindFixtureCatalog.For(FixtureTier.Standard).IdentityHash,
            logicalReads,
            plans);
    }

    public static async Task CollectAndWriteAsync(string outputPath)
    {
        await using var database = await SqlServerCollectionBenchmarkDatabase.CreateAsync().ConfigureAwait(false);
        await SqlServerCollectionCorrectness.VerifyAsync(database).ConfigureAwait(false);
        var evidence = await CollectAsync(database).ConfigureAwait(false);
        var json = JsonSerializer.SerializeToUtf8Bytes(evidence, EvidenceJson.Options);
        var errors = SqlServerCollectionEvidenceValidator.Validate(json);
        if (errors.Count != 0)
            throw new InvalidOperationException("Server evidence failed validation: " +
                string.Join("; ", errors.Select(static error => $"{error.Code}: {error.Message}")));
        var fullPath = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await File.WriteAllBytesAsync(fullPath, json).ConfigureAwait(false);
    }

    private static async Task ExecuteAsync(
        SqlServerCollectionBenchmarkDatabase database,
        SqlServerCollectionTransport transport,
        int cardinality)
    {
        var ids = SqlServerCollectionBenchmarks.CreateIds(cardinality);
        _ = transport switch
        {
            SqlServerCollectionTransport.Tvp => await SqlServerCollectionBenchmarks.ExecuteTvpAsync(
                database.ConnectionString, database.TvpTypeName, ids).ConfigureAwait(false),
            SqlServerCollectionTransport.OpenJson => await SqlServerCollectionBenchmarks.ExecuteOpenJsonAsync(
                database.ConnectionString, ids).ConfigureAwait(false),
            SqlServerCollectionTransport.ScalarExpansion => await SqlServerCollectionBenchmarks.ExecuteScalarAsync(
                database.ConnectionString, ids).ConfigureAwait(false),
            _ => throw new ArgumentOutOfRangeException(nameof(transport), transport, null),
        };
    }

    private static CommandEvidenceShape CommandShape(
        SqlServerCollectionTransport transport,
        int cardinality)
    {
        if (transport == SqlServerCollectionTransport.Tvp)
            return new("inquiry-collection:tvp */", "inquiry-collection:tvp", SqlServerCollectionBenchmarks.TvpSql);
        if (transport == SqlServerCollectionTransport.OpenJson)
            return new("inquiry-collection:openjson */", "inquiry-collection:openjson", SqlServerCollectionBenchmarks.OpenJsonSql);
        return new($"inquiry-collection:scalar:n{cardinality} */", "inquiry-collection:scalar:n",
            SqlServerCollectionBenchmarks.ScalarSql(cardinality));
    }

    private static async Task ClearDatabasePlanCacheAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "ALTER DATABASE SCOPED CONFIGURATION CLEAR PROCEDURE_CACHE;";
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static async Task<QueryStats> ReadStatsAsync(
        string connectionString,
        CommandEvidenceShape shape,
        int expectedCachedPlanCount,
        int? expectedExecutionCount = null)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            WITH cached_statements AS
            (
                SELECT qs.last_logical_reads,
                       qs.execution_count,
                       qs.query_hash,
                       qs.query_plan_hash,
                       qs.plan_handle,
                       text.text AS batch_sql,
                       SUBSTRING(text.text, (qs.statement_start_offset / 2) + 1,
                           ((CASE qs.statement_end_offset WHEN -1 THEN DATALENGTH(text.text)
                               ELSE qs.statement_end_offset END - qs.statement_start_offset) / 2) + 1) AS cached_sql
                FROM sys.dm_exec_query_stats AS qs
                CROSS APPLY sys.dm_exec_sql_text(qs.sql_handle) AS text
            ),
            exact_statement AS
            (
                SELECT *, COUNT_BIG(*) OVER () AS exact_count
                FROM cached_statements
                WHERE batch_sql LIKE N'%' + @exact_tag + N'%'
            ),
            family AS
            (
                SELECT COUNT_BIG(*) AS cached_plan_count
                FROM cached_statements
                WHERE batch_sql LIKE N'%' + @family_tag + N'%'
            )
            SELECT exact.last_logical_reads,
                   exact.execution_count,
                   exact.query_hash,
                   exact.query_plan_hash,
                   family.cached_plan_count,
                   exact.batch_sql,
                   exact.cached_sql,
                   CONVERT(nvarchar(max), plan_xml.query_plan),
                   exact.exact_count
            FROM exact_statement AS exact
            CROSS JOIN family
            CROSS APPLY sys.dm_exec_query_plan(exact.plan_handle) AS plan_xml;
            """;
        command.Parameters.Add("@exact_tag", SqlDbType.NVarChar, 128).Value = shape.ExactTag;
        command.Parameters.Add("@family_tag", SqlDbType.NVarChar, 128).Value = shape.FamilyTag;
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleResult | CommandBehavior.SingleRow)
            .ConfigureAwait(false);
        if (!await reader.ReadAsync().ConfigureAwait(false))
            throw new InvalidOperationException($"No exact DMV query statistics were captured for '{shape.ExactTag}'.");
        var cachedPlanCount = checked((int)reader.GetInt64(4));
        var cachedBatchSql = reader.GetString(5);
        var exactCount = reader.GetInt64(8);
        if (exactCount != 1 || cachedPlanCount != expectedCachedPlanCount)
            throw new InvalidOperationException(
                $"Unexpected cache entry counts for '{shape.ExactTag}': exact={exactCount}, family={cachedPlanCount}, expected={expectedCachedPlanCount}.");
        if (expectedExecutionCount.HasValue && reader.GetInt64(1) != expectedExecutionCount.GetValueOrDefault())
            throw new InvalidOperationException(
                $"The cached statement for '{shape.ExactTag}' was not warmed before measurement.");
        if (!cachedBatchSql.EndsWith(shape.Sql, StringComparison.Ordinal))
            throw new InvalidOperationException($"The cached statement for '{shape.ExactTag}' did not contain the exact executed SQL suffix.");
        return new(reader.GetInt64(0), reader.GetInt64(1), (byte[])reader.GetValue(2),
            (byte[])reader.GetValue(3), cachedPlanCount, cachedBatchSql, reader.GetString(6), reader.GetString(7));
    }

    private static string CachedParameterMetadata(string cachedBatchSql, string executedSql, string queryPlanXml)
    {
        var prefixLength = cachedBatchSql.Length - executedSql.Length;
        if (prefixLength > 0)
        {
            var cachedPrefix = cachedBatchSql[..prefixLength].Trim();
            if (cachedPrefix.Length != 0) return cachedPrefix;
        }

        var parameters = XDocument.Parse(queryPlanXml).Descendants()
            .Where(static element => element.Name.LocalName == "ParameterList")
            .SelectMany(static element => element.Descendants()
                .Where(static child => child.Name.LocalName == "ColumnReference"))
            .Select(static element => new
            {
                Name = (string?)element.Attribute("Column"),
                Type = (string?)element.Attribute("ParameterDataType"),
            })
            .Where(static parameter => parameter.Name is not null && parameter.Type is not null)
            .Select(static parameter => parameter.Name + ":" + parameter.Type)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (parameters.Length == 0)
            throw new InvalidOperationException("The exact cached plan did not expose parameter metadata.");
        return string.Join(";", parameters);
    }

    private static string HashBinary(byte[] value) => HashText(Convert.ToHexString(value).ToLowerInvariant());
    private static string HashText(string value) => Convert.ToHexString(
        System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private sealed record CommandEvidenceShape(string ExactTag, string FamilyTag, string Sql);
    private sealed record QueryStats(
        long LastLogicalReads,
        long ExecutionCount,
        byte[] QueryHash,
        byte[] PlanHash,
        int CachedPlanCount,
        string CachedBatchSql,
        string CachedStatementSql,
        string QueryPlanXml);
}
