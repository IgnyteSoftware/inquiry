using System.Reflection;
using BenchmarkDotNet.Attributes;
using Microsoft.Data.Sqlite;

namespace Inquiry.Benchmarks.SelectedStrategy.Tests;

// The competitor entities map every column for the read legs, but each write leg must still set only the
// columns the handwritten ADO.NET SQL sets. A leg that writes its whole entity clears the other columns,
// and then it does different work from the baseline (#504).
[Collection(nameof(CrudUpdateLegTests))]
public sealed class CrudWriteColumnLegTests
{
    [Theory]
    [InlineData(typeof(global::Inquiry.Benchmarks.CustomerCrudBenchmarks), "Customers", "ContactTitle", "CustomerID = '00000'")]
    [InlineData(typeof(global::Inquiry.Benchmarks.ProductCrudBenchmarks), "Products", "QuantityPerUnit", "ProductID = 1")]
    public async Task CompetitorWriteLegsLeaveTheOtherColumnsAlone(Type benchmarkType, string table, string column, string targetRow)
    {
        var benchmark = Activator.CreateInstance(benchmarkType)!;
        benchmarkType.GetProperty("Rows")!.SetValue(benchmark, 1000);
        benchmarkType.GetMethod("GlobalSetup")!.Invoke(benchmark, null);
        try
        {
            var connectionString = (string)benchmarkType
                .GetField("_connectionString", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(benchmark)!;

            // Inquiry writes the whole entity by design, so its legs are not part of this check.
            var legs = benchmarkType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(static method => method.GetCustomAttribute<BenchmarkAttribute>() is not null
                    && !method.Name.EndsWith("_Inquiry", StringComparison.Ordinal)
                    && method.GetCustomAttribute<BenchmarkCategoryAttribute>()?.Categories
                        .Any(static category => category is "Update" or "Upsert") == true)
                .OrderBy(static method => method.Name, StringComparer.Ordinal)
                .ToArray();
            Assert.NotEmpty(legs);

            foreach (var leg in legs)
            {
                await ExecuteAsync(connectionString, $"UPDATE {table} SET {column} = 'retained' WHERE {targetRow};");
                await CrudUpdateLegTests.InvokeAsync(benchmark, leg);

                Assert.True(
                    "retained".Equals(await ExecuteAsync(connectionString, $"SELECT {column} FROM {table} WHERE {targetRow};")),
                    $"{benchmarkType.Name}.{leg.Name} changed {column}, which the baseline leg does not write.");
            }
        }
        finally
        {
            benchmarkType.GetMethod("GlobalCleanup")!.Invoke(benchmark, null);
        }
    }

    private static async Task<object?> ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync();
    }
}
