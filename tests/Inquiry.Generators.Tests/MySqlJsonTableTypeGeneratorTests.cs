using System;

namespace Inquiry.Generators.Tests;

public sealed partial class InquiryGeneratorTests
{
    [Theory]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    public void MySqlFamilyJsonTableUsesEffectiveTypesAndBinaryDecode(string dialect)
    {
        const string source = """
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry.Entities;
            using Inquiry.Stores;
            namespace Demo;
            public enum State { New, Done }
            [InquiryTable("TValue")]
            public sealed class Value
            {
                [InquiryKey] public int Id { get; set; }
                [InquiryColumn, InquiryEnumAsString] public State State { get; set; }
                [InquiryColumn] public byte[] Data { get; set; } = System.Array.Empty<byte>();
            }
            public partial class ValueStore : InquiryStore<Value>
            {
                [InquirySelectAllByPredicate]
                [InquiryWhere("State", Compare.In)]
                public partial Task<IReadOnlyList<Value>> ByStates(IReadOnlyList<State> states, CancellationToken cancellationToken = default);
                [InquirySelectAllByPredicate]
                [InquiryWhere("Data", Compare.In)]
                public partial Task<IReadOnlyList<Value>> ByData(IReadOnlyList<byte[]> data, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source, dialect: dialect);
        AssertNoErrors(result);
        var text = Assert.Single(result.RunResult.GeneratedTrees,
            static t => t.FilePath.EndsWith("ValueStore.InquiryStore.g.cs", StringComparison.Ordinal)).GetText().ToString();

        Assert.Contains("COLUMNS(val LONGTEXT PATH '$')", text);
        Assert.Contains("SELECT FROM_BASE64(jt.val)", text);
        Assert.Contains("global::System.Linq.Enumerable.Select(states, static _e => _e.ToString())", text);
        Assert.DoesNotContain(" SIGNED PATH", text);
        Assert.DoesNotContain("CHAR(255)", text);
    }
}
