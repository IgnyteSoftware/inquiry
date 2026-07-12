namespace Inquiry.Generators.Tests;

public sealed partial class InquiryGeneratorTests
{
    private const string ConverterDeleteAllSource = """
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using Inquiry.Entities;
        using Inquiry.Stores;
        namespace Demo;
        public readonly record struct StrongId(long Value);
        public sealed class StrongIdConverter : IInquiryValueConverter<StrongId, long>
        {
            public long ToProvider(StrongId value) => value.Value;
            public StrongId FromProvider(long value) => new(value);
        }
        [InquiryTable("ConvertedKeys")]
        public sealed class ConvertedKey
        {
            [InquiryKey(Converter = typeof(StrongIdConverter))] public StrongId Id { get; set; }
            [InquiryColumn] public string Name { get; set; } = string.Empty;
        }
        public partial class ConvertedKeyStore : InquiryStore<ConvertedKey>
        {
            [InquiryDeleteAll]
            public partial Task<int> DeleteAllAsync(IEnumerable<StrongId> ids, CancellationToken ct = default);
        }
        """;

    [Theory]
    [InlineData("Sqlite", "global::Inquiry.Parameters.InquiryJsonArrayParameter", "\"Id\" INTEGER")]
    [InlineData("SqlServer", "global::Inquiry.SqlServer.Parameters.InquiryTvpParameter", "[Id] BIGINT")]
    [InlineData("PostgreSql", "global::Inquiry.Parameters.InquiryArrayParameter", "\"Id\" BIGINT")]
    [InlineData("MySql", "global::Inquiry.Parameters.InquiryJsonArrayParameter", "`Id` BIGINT")]
    [InlineData("MariaDb", "global::Inquiry.Parameters.InquiryJsonArrayParameter", "`Id` BIGINT")]
    [InlineData("Oracle", "global::Inquiry.Parameters.InquiryJsonArrayParameter", "Id NUMBER(19)")]
    public void ConverterDeleteAllProjectsProviderValuesIntoEveryDialectTransport(
        string dialect,
        string binder,
        string providerDdl)
    {
        var result = RunGenerator(ConverterDeleteAllSource, dialect: dialect);
        AssertNoErrors(result);
        var store = ConverterDeleteAllStore(result, "ConvertedKeyStore");
        const string projected = "ids is null ? null : global::System.Linq.Enumerable.Select(ids, static _e => global::Inquiry.Entities.InquiryConverterCache<global::Demo.StrongIdConverter>.Instance.ToProvider(_e))";

        Assert.Contains(providerDdl, ExtractSchemaDdl(result));
        Assert.Contains(ExpectedDeleteAllSql(dialect, "ConvertedKeys", providerIsString: false), store);
        var typeName = dialect == "SqlServer"
            ? ", \"[dbo].[Inquiry_Tvp_e36b3e7cf003f2911419d555807aef152b7c6667f4b9b9fb3984b20ecedd995a]\""
            : string.Empty;
        Assert.Contains($"{binder}.Bind(_c, \"{(dialect == "Oracle" ? ":keys" : "@keys")}\", {projected}{typeName});", store);
        Assert.DoesNotContain($"{binder}.Bind(_c, \"{(dialect == "Oracle" ? ":keys" : "@keys")}\", ids);", store);
        Assert.Contains("static _e =>", store);
        Assert.Single(global::System.Text.RegularExpressions.Regex.Matches(
            store,
            "StrongIdConverter>.Instance.ToProvider\\(").Cast<global::System.Text.RegularExpressions.Match>());
        Assert.DoesNotContain(".ToArray()", store);
        Assert.DoesNotContain(".ToList()", store);
    }

    [Theory]
    [InlineData("Sqlite", "global::Inquiry.Parameters.InquiryJsonArrayParameter")]
    [InlineData("SqlServer", "global::Inquiry.SqlServer.Parameters.InquiryTvpParameter")]
    [InlineData("PostgreSql", "global::Inquiry.Parameters.InquiryArrayParameter")]
    [InlineData("MySql", "global::Inquiry.Parameters.InquiryJsonArrayParameter")]
    [InlineData("MariaDb", "global::Inquiry.Parameters.InquiryJsonArrayParameter")]
    [InlineData("Oracle", "global::Inquiry.Parameters.InquiryJsonArrayParameter")]
    public void ConverterDeleteAllGuardsNullableValueAndReferenceElements(string dialect, string binder)
    {
        const string source = """
            #nullable enable
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry.Entities;
            using Inquiry.Stores;
            namespace Demo;
            public readonly record struct StrongId(long Value);
            public sealed class RefId { public string Value { get; set; } = string.Empty; }
            public sealed class StrongIdConverter : IInquiryValueConverter<StrongId, long>
            { public long ToProvider(StrongId value) => value.Value; public StrongId FromProvider(long value) => new(value); }
            public sealed class RefIdConverter : IInquiryValueConverter<RefId, string>
            { public string ToProvider(RefId value) => value.Value; public RefId FromProvider(string value) => new() { Value = value }; }

            [InquiryTable("NullableKeys")]
            public sealed class NullableKey
            { [InquiryKey(Converter = typeof(StrongIdConverter))] public StrongId? Id { get; set; } }
            public partial class NullableKeyStore : InquiryStore<NullableKey>
            {
                [InquiryDeleteAll]
                public partial Task<int> DeleteAllAsync(IEnumerable<StrongId?> ids, CancellationToken ct = default);
            }

            [InquiryTable("ReferenceKeys")]
            public sealed class ReferenceKey
            { [InquiryKey(Length = 64, Converter = typeof(RefIdConverter))] public RefId Id { get; set; } = new(); }
            public partial class ReferenceKeyStore : InquiryStore<ReferenceKey>
            {
                [InquiryDeleteAll]
                public partial Task<int> DeleteAllAsync(IEnumerable<RefId> ids, CancellationToken ct = default);
            }
            """;

        var result = RunGenerator(source, dialect: dialect);
        AssertNoErrors(result);
        var nullableValue = ConverterDeleteAllStore(result, "NullableKeyStore");
        var reference = ConverterDeleteAllStore(result, "ReferenceKeyStore");
        var parameterName = dialect == "Oracle" ? ":keys" : "@keys";
        const string nullableProjection = "ids is null ? null : global::System.Linq.Enumerable.Select(ids, static _e => _e.HasValue ? (long?)global::Inquiry.Entities.InquiryConverterCache<global::Demo.StrongIdConverter>.Instance.ToProvider(_e.Value) : null)";
        const string referenceProjection = "ids is null ? null : global::System.Linq.Enumerable.Select(ids, static _e => _e is null ? (string?)null : global::Inquiry.Entities.InquiryConverterCache<global::Demo.RefIdConverter>.Instance.ToProvider(_e))";

        Assert.Contains(ExpectedDeleteAllSql(dialect, "NullableKeys", providerIsString: false), nullableValue);
        Assert.Contains(ExpectedDeleteAllSql(dialect, "ReferenceKeys", providerIsString: true), reference);
        var nullableTypeName = dialect == "SqlServer"
            ? ", \"[dbo].[Inquiry_Tvp_e36b3e7cf003f2911419d555807aef152b7c6667f4b9b9fb3984b20ecedd995a]\""
            : string.Empty;
        var referenceTypeName = dialect == "SqlServer"
            ? ", \"[dbo].[Inquiry_Tvp_474f2ebbdd781f2c0331853ca09837a0aa4613f2bf445089eafda2b033abe95c]\""
            : string.Empty;
        Assert.Contains($"{binder}.Bind(_c, \"{parameterName}\", {nullableProjection}{nullableTypeName});", nullableValue);
        Assert.Contains($"{binder}.Bind(_c, \"{parameterName}\", {referenceProjection}{referenceTypeName});", reference);
        Assert.DoesNotContain($"{binder}.Bind(_c, \"{parameterName}\", ids);", nullableValue);
        Assert.DoesNotContain($"{binder}.Bind(_c, \"{parameterName}\", ids);", reference);
        Assert.Single(global::System.Text.RegularExpressions.Regex.Matches(nullableValue, "StrongIdConverter>.Instance.ToProvider\\(").Cast<global::System.Text.RegularExpressions.Match>());
        Assert.Single(global::System.Text.RegularExpressions.Regex.Matches(reference, "RefIdConverter>.Instance.ToProvider\\(").Cast<global::System.Text.RegularExpressions.Match>());
    }

    [Fact]
    public void ConverterDeleteAllUsesSharedProjectionForSoftDeleteAndEnumAsString()
    {
        const string source = """
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry.Entities;
            using Inquiry.Stores;
            namespace Demo;
            public readonly record struct StrongId(long Value);
            public sealed class StrongIdConverter : IInquiryValueConverter<StrongId, long>
            { public long ToProvider(StrongId value) => value.Value; public StrongId FromProvider(long value) => new(value); }
            [InquiryTable("SoftKeys")]
            public sealed class SoftKey
            {
                [InquiryKey(Converter = typeof(StrongIdConverter))] public StrongId Id { get; set; }
                [InquiryColumn, InquirySoftDelete] public bool IsDeleted { get; set; }
            }
            public partial class SoftKeyStore : InquiryStore<SoftKey>
            {
                [InquiryDeleteAll] public partial Task<int> DeleteAllAsync(IEnumerable<StrongId> ids, CancellationToken ct = default);
            }
            public enum Code { A, B }
            [InquiryTable("EnumKeys")]
            public sealed class EnumKey
            { [InquiryKey, InquiryEnumAsString] public Code Id { get; set; } }
            public partial class EnumKeyStore : InquiryStore<EnumKey>
            {
                [InquiryDeleteAll] public partial Task<int> DeleteAllAsync(IEnumerable<Code> ids, CancellationToken ct = default);
            }
            """;

        var result = RunGenerator(source);
        AssertNoErrors(result);
        var softDelete = ConverterDeleteAllStore(result, "SoftKeyStore");
        var enumStore = ConverterDeleteAllStore(result, "EnumKeyStore");

        Assert.Contains("UPDATE \\\"SoftKeys\\\" SET \\\"IsDeleted\\\" = 1", softDelete);
        Assert.Contains("Enumerable.Select(ids, static _e => global::Inquiry.Entities.InquiryConverterCache<global::Demo.StrongIdConverter>.Instance.ToProvider(_e))", softDelete);
        Assert.Contains("Enumerable.Select(ids, static _e => _e.ToString())", enumStore);
    }

    private static string ConverterDeleteAllStore(GeneratorTestResult result, string name)
    {
        var tree = Assert.Single(result.RunResult.GeneratedTrees,
            tree => tree.FilePath.EndsWith(name + ".InquiryStore.g.cs", StringComparison.Ordinal));
        return tree.GetText().ToString();
    }

    private static string ExpectedDeleteAllSql(string dialect, string table, bool providerIsString)
        => dialect switch
        {
            "Sqlite" => $"private const string _sqlDeleteAll = \"DELETE FROM \\\"{table}\\\" WHERE \\\"Id\\\" IN (SELECT value FROM json_each(@keys))\";",
            "SqlServer" => $"private const string _sqlDeleteAll = \"DELETE FROM [{table}] WHERE [Id] IN (SELECT [Value] FROM @keys)\";",
            "PostgreSql" => $"private const string _sqlDeleteAll = \"DELETE FROM \\\"{table}\\\" WHERE \\\"Id\\\" = ANY(@keys)\";",
            "MySql" or "MariaDb" => $"private const string _sqlDeleteAll = \"DELETE FROM `{table}` WHERE `Id` IN (SELECT jt.val FROM JSON_TABLE(@keys, '$[*]' COLUMNS(val {(providerIsString ? "LONGTEXT" : "BIGINT")} PATH '$')) jt)\";",
            "Oracle" => $"private const string _sqlDeleteAll = \"DELETE FROM {table} WHERE Id IN (SELECT jt.val FROM JSON_TABLE(:keys, '$[*]' COLUMNS(val {(providerIsString ? "VARCHAR2(4000)" : "NUMBER(19)")} PATH '$')) jt)\";",
            _ => throw new global::System.ArgumentOutOfRangeException(nameof(dialect)),
        };
}
