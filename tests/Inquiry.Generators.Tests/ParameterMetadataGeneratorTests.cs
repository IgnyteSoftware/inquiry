using Microsoft.CodeAnalysis;

namespace Inquiry.Generators.Tests;

public sealed partial class InquiryGeneratorTests
{
    private const string ParameterMetadataSource = """
        using System;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using Inquiry;
        using Inquiry.Entities;
        using Inquiry.Paging;
        using Inquiry.Stores;
        namespace Demo;
        [InquiryTable("MetadataItem")]
        public sealed class MetadataItem
        {
            [InquiryKey] public int Id { get; set; }
            [InquiryColumn(Length = 64, IsUnicode = false)] public string Code { get; set; } = string.Empty;
            [InquiryColumn] public byte[] Payload { get; set; } = Array.Empty<byte>();
            [InquiryColumn] public DateTimeOffset OccurredAt { get; set; }
        }
        public partial class MetadataStore : InquiryStore<MetadataItem>
        {
            [InquirySelectAllByField("Code", OrderBy = "Id", Paged = true)]
            public partial Task<IReadOnlyList<MetadataItem>> PageByCodeAsync(string code, int offset, int limit, CancellationToken ct = default);
            [InquirySelectAllByPredicate]
            [InquiryWhere("OccurredAt", Compare.Equal)]
            public partial Task<IReadOnlyList<MetadataItem>> SearchAsync(DateTimeOffset occurredAt, CancellationToken ct = default);
            [InquiryExists]
            [InquiryWhere("Code", Compare.Equal)]
            public partial Task<bool> ExistsAsync(string code, CancellationToken ct = default);
            [InquiryUpdate]
            [InquiryWhere("Id", Compare.Equal)]
            public partial Task<int> RenameAsync(string code, int id, CancellationToken ct = default);
            [InquiryDelete]
            [InquiryWhere("Payload", Compare.Equal)]
            public partial Task<int> DeleteAsync(byte[] payload, CancellationToken ct = default);
            [InquiryKeysetPage("Id")]
            public partial Task<InquiryPage<MetadataItem, int>> SeekAsync(int? afterId, int pageSize, CancellationToken ct = default);
        }
        """;

    [Theory]
    [InlineData("SqlServer")]
    [InlineData("PostgreSql")]
    [InlineData("Sqlite")]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    [InlineData("Oracle")]
    public void AllColumnBackedScalarBinderPathsEmitDbType(string dialect)
    {
        var result = RunGenerator(ParameterMetadataSource, dialect: dialect);
        AssertNoErrors(result);
        var tree = Assert.Single(result.RunResult.GeneratedTrees,
            static t => t.FilePath.EndsWith("MetadataStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var text = tree.GetText().ToString();

        var page = Method(text, "PageByCodeAsync");
        if (dialect == "Oracle") Assert.Matches("ParameterName = \"iq1\\$Codexx\\$[0-9a-f]{14}\"", page);
        else Assert.Contains("@Code", page);
        Assert.Contains("DbType.AnsiString", page);

        var search = Method(text, "SearchAsync");
        Assert.Contains(dialect == "Oracle" ? "ParameterName = \"OccurredAt\"" : "@OccurredAt", search);
        Assert.Contains("DbType.DateTimeOffset", search);

        var exists = Method(text, "ExistsAsync");
        Assert.Contains(dialect == "Oracle" ? "ParameterName = \"Code\"" : "@Code", exists);
        Assert.Contains("DbType.AnsiString", exists);

        var rename = Method(text, "RenameAsync");
        if (dialect == "Oracle")
        {
            Assert.Matches("ParameterName = \"iq1\\$Codexx\\$[0-9a-f]{14}\"", rename);
            Assert.Contains("ParameterName = \"Id\"", rename);
        }
        else
        {
            Assert.Contains("@Code", rename);
            Assert.Contains("@Id", rename);
        }
        Assert.Contains("DbType.AnsiString", rename);
        Assert.Contains("DbType.Int32", rename);

        var delete = Method(text, "DeleteAsync");
        Assert.Contains(dialect == "Oracle" ? "ParameterName = \"Payload\"" : "@Payload", delete);
        Assert.Contains("DbType.Binary", delete);

        var seek = Method(text, "SeekAsync");
        if (dialect == "Oracle") Assert.Matches("ParameterName = \"iq1\\$cursor\\$[0-9a-f]{14}\"", seek);
        else Assert.Contains("@__cursor0", seek);
        Assert.Contains("DbType.Int32", seek);
    }

    [Fact]
    public void SqlServerPredicateCarriesSizeButUpdateValueDoesNot()
    {
        var result = RunGenerator(ParameterMetadataSource, dialect: "SqlServer");
        AssertNoErrors(result);
        var tree = Assert.Single(result.RunResult.GeneratedTrees,
            static t => t.FilePath.EndsWith("MetadataStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var text = tree.GetText().ToString();

        var page = Method(text, "PageByCodeAsync");
        Assert.Contains(".Size = 64;", page);

        var exists = Method(text, "ExistsAsync");
        Assert.Contains(".Size = 64;", exists);

        var rename = Method(text, "RenameAsync");
        var setValue = rename.IndexOf(".Value = (object?)_args.Arg0", StringComparison.Ordinal);
        var whereParameter = rename.IndexOf("ParameterName = \"@Id\"", StringComparison.Ordinal);
        Assert.True(setValue >= 0 && whereParameter > setValue);
        var setBlock = rename[rename.IndexOf("ParameterName = \"@Code\"", StringComparison.Ordinal)..whereParameter];
        Assert.Contains("DbType.AnsiString", setBlock);
        Assert.DoesNotContain(".Size =", setBlock);
        Assert.DoesNotContain(".Precision =", setBlock);
        Assert.DoesNotContain(".Scale =", setBlock);
    }

    private static string Method(string generated, string methodName)
    {
        var name = generated.IndexOf(methodName + "(", StringComparison.Ordinal);
        Assert.True(name >= 0, $"Generated method {methodName} was not found.");
        var start = generated.LastIndexOf('\n', name) + 1;
        var end = generated.IndexOf("\n    }", name, StringComparison.Ordinal);
        Assert.True(end > name, $"Generated method {methodName} had no closing brace.");
        return generated[start..(end + 6)];
    }
}
