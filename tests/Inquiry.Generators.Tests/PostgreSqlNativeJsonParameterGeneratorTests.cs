namespace Inquiry.Generators.Tests;

public sealed partial class InquiryGeneratorTests
{
    private const string PostgreSqlNativeJsonParameterSource = """
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using Inquiry;
        using Inquiry.Entities;
        using Inquiry.Stores;
        namespace Demo;

        public sealed class Settings { public string Theme { get; set; } = ""; }

        [InquiryTable("JsonItem")]
        public sealed class JsonItem
        {
            [InquiryKey] public int Id { get; set; }
            [InquiryColumn(SqlType = "JSONB"), InquiryJson] public Settings? Binary { get; set; }
            [InquiryColumn(SqlType = " json ")] public string? Text { get; set; }
            [InquiryColumn] public string? Plain { get; set; }
        }

        public partial class JsonItemStore : InquiryStore<JsonItem>
        {
            [InquiryInsert] public partial Task<int> InsertAsync(JsonItem item, CancellationToken ct = default);
            [InquiryBulkInsert] public partial Task<long> BulkAsync(IEnumerable<JsonItem> items, CancellationToken ct = default);
            [InquirySelectAllByPredicate][InquiryWhere("Text", JsonPath = "$.theme")] public partial Task<IReadOnlyList<JsonItem>> ByThemeAsync(string theme, CancellationToken ct = default);
        }
        """;

    [Fact]
    public void PostgreSqlNativeJsonColumnsBindAsNpgsqlJsonTypes()
    {
        var result = RunGenerator(PostgreSqlNativeJsonParameterSource, dialect: "PostgreSql");
        AssertNoErrors(result);
        var text = Assert.Single(result.RunResult.GeneratedTrees,
            static t => t.FilePath.EndsWith("JsonItemStore.InquiryStore.g.cs", StringComparison.Ordinal)).GetText().ToString();

        var insert = Method(text, "InsertAsync");
        Assert.Contains("((global::Npgsql.NpgsqlParameter)_p1).NpgsqlDbType = global::NpgsqlTypes.NpgsqlDbType.Jsonb;", insert);
        Assert.Contains("((global::Npgsql.NpgsqlParameter)_p2).NpgsqlDbType = global::NpgsqlTypes.NpgsqlDbType.Json;", insert);
        Assert.DoesNotContain("(global::Npgsql.NpgsqlParameter)_p3", insert);
        Assert.DoesNotContain("NpgsqlDbType", Method(text, "ByThemeAsync"));
        Assert.Contains("new string?[] { null, \"JSONB\", \" json \", null }", text);
    }

    [Theory]
    [InlineData("Sqlite")]
    [InlineData("SqlServer")]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    [InlineData("Oracle")]
    public void NonPostgreSqlNativeJsonColumnsKeepPortableParameters(string dialect)
    {
        var result = RunGenerator(PostgreSqlNativeJsonParameterSource, dialect: dialect);
        AssertNoErrors(result);
        var text = Assert.Single(result.RunResult.GeneratedTrees,
            static t => t.FilePath.EndsWith("JsonItemStore.InquiryStore.g.cs", StringComparison.Ordinal)).GetText().ToString();
        Assert.DoesNotContain("Npgsql", text);
    }
}
