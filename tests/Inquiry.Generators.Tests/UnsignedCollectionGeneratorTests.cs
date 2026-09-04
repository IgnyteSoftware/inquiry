namespace Inquiry.Generators.Tests;

public sealed partial class InquiryGeneratorTests
{
    private const string UnsignedCollectionSource = """
        using System.Collections.Generic; using System.Threading; using System.Threading.Tasks;
        using Inquiry; using Inquiry.Entities; using Inquiry.Stores;
        namespace Demo;
        public readonly record struct Strong(uint Value);
        public sealed class StrongConverter : IInquiryValueConverter<Strong, uint>
        { public uint ToProvider(Strong value) => value.Value; public Strong FromProvider(uint value) => new(value); }
        public enum UnsignedState : uint { High = 3000000000u, Max = uint.MaxValue }
        [InquiryTable("UnsignedItems")]
        public sealed class Item
        {
            [InquiryKey] public uint Id { get; set; }
            [InquiryColumn] public sbyte S8 { get; set; }
            [InquiryColumn] public ushort U16 { get; set; }
            [InquiryColumn] public uint U32 { get; set; }
            [InquiryColumn] public ulong U64 { get; set; }
            [InquiryColumn] public uint? NullableU32 { get; set; }
            [InquiryColumn(Converter = typeof(StrongConverter))] public Strong Converted { get; set; }
            [InquiryColumn] public UnsignedState State { get; set; }
        }
        public partial class ItemStore : InquiryStore<Item>
        {
            [InquiryExists, InquiryWhere("S8", Compare.In)] public partial Task<bool> S8In(IReadOnlyList<sbyte> values, CancellationToken ct = default);
            [InquiryExists, InquiryWhere("U16", Compare.In)] public partial Task<bool> U16In(IReadOnlyList<ushort> values, CancellationToken ct = default);
            [InquiryExists, InquiryWhere("U32", Compare.In)] public partial Task<bool> U32In(IReadOnlyList<uint> values, CancellationToken ct = default);
            [InquiryExists, InquiryWhere("U64", Compare.In)] public partial Task<bool> U64In(IReadOnlyList<ulong> values, CancellationToken ct = default);
            [InquiryExists, InquiryWhere("Converted", Compare.In)] public partial Task<bool> ConvertedIn(IReadOnlyList<Strong> values, CancellationToken ct = default);
            [InquiryExists, InquiryWhere("Converted", Compare.NotIn)] public partial Task<bool> ConvertedNotIn(IReadOnlyList<Strong> values, CancellationToken ct = default);
            [InquiryUpdate, InquiryWhere("Converted", Compare.In)] public partial Task<int> UpdateConverted(uint u32, IReadOnlyList<Strong> values, CancellationToken ct = default);
            [InquiryExists, InquiryWhere("State", Compare.In)] public partial Task<bool> EnumIn(IReadOnlyList<UnsignedState> values, CancellationToken ct = default);
            [InquiryExists, InquiryWhere("U32", Compare.NotIn)] public partial Task<bool> U32NotIn(IReadOnlyList<uint> values, CancellationToken ct = default);
            [InquiryDelete, InquiryWhere("Id", Compare.In)] public partial Task<int> DeleteAll(IReadOnlyList<uint> values, CancellationToken ct = default);
        }
        """;

    [Theory]
    [InlineData("PostgreSql", "InquiryArrayParameter.Bind")]
    [InlineData("SqlServer", "InquiryTvpParameter.Bind")]
    public void NativeUnsignedCollectionsUseOneStaticSignedPartnerProjection(string dialect, string binder)
    {
        var result = RunGenerator(UnsignedCollectionSource, dialect: dialect);
        AssertNoErrors(result);
        var generated = string.Join("\n", result.RunResult.GeneratedTrees.Select(static tree => tree.GetText().ToString()));
        Assert.Contains(dialect == "PostgreSql"
            ? "static _e => unchecked((global::System.Int16)(global::System.Byte)(_e))"
            : "static _e => unchecked((global::System.Byte)(_e))", generated);
        Assert.Contains("static _e => unchecked((global::System.Int16)(_e))", generated);
        Assert.Contains("static _e => unchecked((global::System.Int32)(_e))", generated);
        Assert.Contains("static _e => unchecked((global::System.Int64)(_e))", generated);
        Assert.Contains("StrongConverter>.Instance.ToProvider(_e)", generated);
        Assert.True(global::System.Text.RegularExpressions.Regex.Matches(generated, "StrongConverter>.Instance.ToProvider\\(_e\\)").Count == 3);
        Assert.Contains(binder, generated);
        Assert.DoesNotContain("BindUnsupported", generated);
        Assert.DoesNotContain("Convert.ChangeType", generated);
        Assert.DoesNotContain("Select(global::System.Linq.Enumerable.Select", generated);
        if (dialect == "SqlServer")
        {
            var schema = Assert.Single(result.RunResult.GeneratedTrees, static tree => tree.FilePath.EndsWith("InquiryGeneratedSchema.g.cs", StringComparison.Ordinal)).GetText().ToString();
            Assert.Contains("AS TABLE ([Value] TINYINT NOT NULL)", schema);
            Assert.Contains("AS TABLE ([Value] SMALLINT NOT NULL)", schema);
            Assert.Contains("AS TABLE ([Value] INT NOT NULL)", schema);
            Assert.Contains("AS TABLE ([Value] BIGINT NOT NULL)", schema);
            Assert.Single(global::System.Text.RegularExpressions.Regex.Matches(schema, "AS TABLE \\(\\[Value\\] INT NOT NULL\\)").Cast<global::System.Text.RegularExpressions.Match>());
            Assert.Contains("Inquiry_Tvp_04c62ef046c2b6360a93af873b3bf9acb9f7a1b100290f0d3f9116f1b78abf7c", schema);
        }
    }

    [Theory]
    [InlineData("Sqlite")]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    [InlineData("Oracle")]
    public void JsonDialectsDoNotProjectDirectUnsignedCollections(string dialect)
    {
        var result = RunGenerator(UnsignedCollectionSource, dialect: dialect);
        AssertNoErrors(result);
        var store = Assert.Single(result.RunResult.GeneratedTrees, static tree => tree.FilePath.EndsWith("ItemStore.InquiryStore.g.cs", StringComparison.Ordinal)).GetText().ToString();
        Assert.Contains("InquiryJsonArrayParameter.Bind(_c,", store);
        Assert.DoesNotContain("unchecked((global::System.Int32)", store);
        Assert.DoesNotContain("unchecked((global::System.Int64)", store);
    }

    [Fact]
    public void NullablePredicateElementsAreAcceptedForNonNullableColumns()
    {
        var source = UnsignedCollectionSource.Replace(
            "[InquiryExists, InquiryWhere(\"U32\", Compare.In)] public partial Task<bool> U32In(IReadOnlyList<uint> values, CancellationToken ct = default);",
            "[InquiryExists, InquiryWhere(\"U32\", Compare.In)] public partial Task<bool> U32In(IReadOnlyList<uint?> values, CancellationToken ct = default);");
        var result = RunGenerator(source, dialect: "PostgreSql");
        AssertNoErrors(result);
    }
}
