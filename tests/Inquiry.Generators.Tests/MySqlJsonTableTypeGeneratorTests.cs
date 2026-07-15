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
                [InquiryColumn] public bool Enabled { get; set; }
                [InquiryColumn] public byte ByteValue { get; set; }
                [InquiryColumn] public short ShortValue { get; set; }
                [InquiryColumn] public long LongValue { get; set; }
                [InquiryColumn] public float FloatValue { get; set; }
                [InquiryColumn] public double DoubleValue { get; set; }
            }
            public partial class ValueStore : InquiryStore<Value>
            {
                [InquirySelectAllByPredicate]
                [InquiryWhere("State", Compare.In)]
                public partial Task<IReadOnlyList<Value>> ByStates(IReadOnlyList<State> states, CancellationToken cancellationToken = default);
                [InquirySelectAllByPredicate]
                [InquiryWhere("Data", Compare.In)]
                public partial Task<IReadOnlyList<Value>> ByData(IReadOnlyList<byte[]> data, CancellationToken cancellationToken = default);
                [InquirySelectAllByPredicate, InquiryWhere("Enabled", Compare.In)]
                public partial Task<IReadOnlyList<Value>> ByEnabled(IReadOnlyList<bool> values, CancellationToken cancellationToken = default);
                [InquirySelectAllByPredicate, InquiryWhere("ByteValue", Compare.In)]
                public partial Task<IReadOnlyList<Value>> ByBytes(IReadOnlyList<byte> values, CancellationToken cancellationToken = default);
                [InquirySelectAllByPredicate, InquiryWhere("ShortValue", Compare.In)]
                public partial Task<IReadOnlyList<Value>> ByShorts(IReadOnlyList<short> values, CancellationToken cancellationToken = default);
                [InquirySelectAllByPredicate, InquiryWhere("LongValue", Compare.In)]
                public partial Task<IReadOnlyList<Value>> ByLongs(IReadOnlyList<long> values, CancellationToken cancellationToken = default);
                [InquirySelectAllByPredicate, InquiryWhere("FloatValue", Compare.In)]
                public partial Task<IReadOnlyList<Value>> ByFloats(IReadOnlyList<float> values, CancellationToken cancellationToken = default);
                [InquirySelectAllByPredicate, InquiryWhere("DoubleValue", Compare.In)]
                public partial Task<IReadOnlyList<Value>> ByDoubles(IReadOnlyList<double> values, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source, dialect: dialect);
        AssertNoErrors(result);
        var text = Assert.Single(result.RunResult.GeneratedTrees,
            static t => t.FilePath.EndsWith("ValueStore.InquiryStore.g.cs", StringComparison.Ordinal)).GetText().ToString();

        Assert.Contains("COLUMNS(val LONGTEXT PATH '$')", text);
        Assert.Contains("SELECT FROM_BASE64(jt.val)", text);
        Assert.Contains("global::System.Linq.Enumerable.Select(states, static _e => _e.ToString())", text);
        Assert.Contains("COLUMNS(val BOOLEAN PATH '$')", text);
        Assert.Contains("COLUMNS(val TINYINT UNSIGNED PATH '$')", text);
        Assert.Contains("COLUMNS(val SMALLINT PATH '$')", text);
        Assert.Contains("COLUMNS(val BIGINT PATH '$')", text);
        Assert.Contains("COLUMNS(val FLOAT PATH '$')", text);
        Assert.Contains("COLUMNS(val DOUBLE PATH '$')", text);
        Assert.DoesNotContain(" SIGNED PATH", text);
        Assert.DoesNotContain("CHAR(255)", text);
    }
}
