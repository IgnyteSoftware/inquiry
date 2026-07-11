using System;

namespace Inquiry.Generators.Tests;

/// <summary>
/// Value converters: <c>[InquiryColumn(Converter = typeof(X))]</c> and <c>[InquiryJson]</c> map a
/// non-primitive property to/from a provider primitive — read via <c>FromProvider</c>, written via
/// <c>ToProvider</c>, with the provider's DbType on the bound parameter.
/// </summary>
public sealed partial class InquiryGeneratorTests
{
    private const string ConverterSource = """
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using Inquiry;
        using Inquiry.Entities;
        using Inquiry.Stores;

        namespace Demo;

        public struct Money { public decimal Amount { get; set; } }

        public sealed class MoneyConverter : IInquiryValueConverter<Money, decimal>
        {
            public decimal ToProvider(Money model) => model.Amount;
            public Money FromProvider(decimal provider) => new Money { Amount = provider };
        }

        [InquiryTable("Account")]
        public sealed class Account
        {
            [InquiryKey(IsGenerated = true)]
            public long Id { get; set; }

            [InquiryColumn("Balance", Converter = typeof(MoneyConverter))]
            public Money Balance { get; set; }

            [InquiryColumn("Meta"), InquiryJson]
            public Dictionary<string, string>? Meta { get; set; }
        }

        public partial class AccountStore : Inquiry.Stores.InquiryStore<Demo.Account>
        {
            [InquiryInsert]
            public partial Task<int> InsertAsync(Account account, CancellationToken cancellationToken = default);
        }
        """;

    [Fact]
    public void ConverterColumnsReadViaFromProvider()
    {
        var result = RunGenerator(ConverterSource);
        AssertNoErrors(result);

        var tree = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("Account.InquiryEntity.g.cs", StringComparison.Ordinal));
        var text = tree.GetText().ToString();

        // Custom converter at ordinal 1; JSON converter (nullable) guarded at ordinal 2.
        Assert.Contains("Balance = global::Inquiry.Entities.InquiryConverterCache<global::Demo.MoneyConverter>.Instance.FromProvider(reader.GetDecimal(1))", text);
        Assert.Contains("Meta = reader.IsDBNull(2) ? null : global::Inquiry.Entities.InquiryConverterCache<global::Inquiry.Converters.InquiryJsonConverter<global::System.Collections.Generic.Dictionary<string, string>>>.Instance.FromProvider(reader.GetString(2))", text);
    }

    [Fact]
    public void ConverterColumnsWriteViaToProviderWithProviderDbType()
    {
        var result = RunGenerator(ConverterSource);
        AssertNoErrors(result);

        var tree = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("AccountStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var text = tree.GetText().ToString();

        Assert.Contains("global::Inquiry.Entities.InquiryConverterCache<global::Demo.MoneyConverter>.Instance.ToProvider(", text);
        Assert.Contains("global::Inquiry.Entities.InquiryConverterCache<global::Inquiry.Converters.InquiryJsonConverter<global::System.Collections.Generic.Dictionary<string, string>>>.Instance.ToProvider(", text);
        // Provider DbType, not the model type: decimal for Money, string for the JSON column.
        Assert.Contains("global::System.Data.DbType.Decimal", text);
        Assert.Contains("global::System.Data.DbType.String", text);
        // Nullable JSON model → null guard maps to DBNull.
        Assert.Contains("is null ? global::System.DBNull.Value", text);
    }

    [Theory]
    [InlineData("SqlServer", "global::System.Data.DbType.DateTime2")]
    [InlineData("Oracle", "global::System.Data.DbType.DateTime")]
    public void ConverterMetadataUsesFullProviderType(string dialect, string dateTimeDbType)
    {
        const string source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using System.Collections.Generic;
            using Inquiry.Entities;
            using Inquiry.Stores;
            namespace Demo;
            public readonly struct Token { }
            public sealed class GuidConverter : IInquiryValueConverter<Token, Guid> { public Guid ToProvider(Token value) => default; public Token FromProvider(Guid value) => default; }
            public sealed class BinaryConverter : IInquiryValueConverter<Token, byte[]> { public byte[] ToProvider(Token value) => Array.Empty<byte>(); public Token FromProvider(byte[] value) => default; }
            public sealed class OffsetConverter : IInquiryValueConverter<Token, DateTimeOffset> { public DateTimeOffset ToProvider(Token value) => default; public Token FromProvider(DateTimeOffset value) => default; }
            public sealed class DateConverter : IInquiryValueConverter<Token, DateTime> { public DateTime ToProvider(Token value) => default; public Token FromProvider(DateTime value) => default; }
            [InquiryTable("Converted")]
            public sealed class Converted
            {
                [InquiryKey] public int Id { get; set; }
                [InquiryColumn(Converter = typeof(GuidConverter))] public Token GuidValue { get; set; }
                [InquiryColumn(Converter = typeof(BinaryConverter))] public Token BinaryValue { get; set; }
                [InquiryColumn(Converter = typeof(OffsetConverter))] public Token OffsetValue { get; set; }
                [InquiryColumn(Converter = typeof(DateConverter))] public Token DateValue { get; set; }
            }
            public partial class ConvertedStore : InquiryStore<Converted>
            {
                [InquiryInsert] public partial Task<int> InsertAsync(Converted value, CancellationToken ct = default);
            }
            """;

        var result = RunGenerator(source, dialect: dialect);
        AssertNoErrors(result);
        var tree = Assert.Single(result.RunResult.GeneratedTrees,
            static t => t.FilePath.EndsWith("ConvertedStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var text = tree.GetText().ToString();

        Assert.Contains("global::System.Data.DbType.Guid", text);
        Assert.Contains("global::System.Data.DbType.Binary", text);
        Assert.Contains("global::System.Data.DbType.DateTimeOffset", text);
        Assert.Contains(dateTimeDbType, text);
    }

    [Fact]
    public void NonUnicodeConverterStringProviderBindsAnsiString()
    {
        const string source = """
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry.Entities;
            using Inquiry.Stores;
            namespace Demo;
            public readonly struct Code { }
            public sealed class CodeConverter : IInquiryValueConverter<Code, string>
            {
                public string ToProvider(Code value) => string.Empty;
                public Code FromProvider(string value) => default;
            }
            [InquiryTable("ConvertedCode")]
            public sealed class ConvertedCode
            {
                [InquiryKey] public int Id { get; set; }
                [InquiryColumn(IsUnicode = false, Converter = typeof(CodeConverter))] public Code Value { get; set; }
            }
            public partial class ConvertedCodeStore : InquiryStore<ConvertedCode>
            {
                [InquirySelectAllByField("Value")] public partial Task<IReadOnlyList<ConvertedCode>> SelectAsync(Code value, CancellationToken ct = default);
            }
            """;

        var result = RunGenerator(source, dialect: "SqlServer");
        AssertNoErrors(result);
        var tree = Assert.Single(result.RunResult.GeneratedTrees,
            static t => t.FilePath.EndsWith("ConvertedCodeStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var text = tree.GetText().ToString();
        Assert.Contains("DbType.AnsiString", text);
        Assert.DoesNotContain("DbType.String;", text);
    }

    [Fact]
    public void ConverterColumnDdlUsesProviderType()
    {
        var result = RunGenerator(ConverterSource);
        AssertNoErrors(result);
        var ddl = ExtractSchemaDdl(result);

        // The Money converter's provider is decimal → NUMERIC (SQLite), not TEXT; JSON provider is string → TEXT.
        Assert.Contains("\"Balance\" NUMERIC", ddl);
        Assert.Contains("\"Meta\" TEXT", ddl);
    }

    [Fact]
    public void NullableValueTypeConverterGuardsBothSides()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry;
            using Inquiry.Entities;
            using Inquiry.Stores;

            namespace Demo;

            public struct Money { public decimal Amount { get; set; } }

            public sealed class MoneyConverter : IInquiryValueConverter<Money, decimal>
            {
                public decimal ToProvider(Money model) => model.Amount;
                public Money FromProvider(decimal provider) => new Money { Amount = provider };
            }

            [InquiryTable("Account")]
            public sealed class Account
            {
                [InquiryKey(IsGenerated = true)]
                public long Id { get; set; }

                [InquiryColumn("Balance", Converter = typeof(MoneyConverter))]
                public Money? Balance { get; set; }
            }

            public partial class AccountStore : Inquiry.Stores.InquiryStore<Demo.Account>
            {
                [InquiryInsert]
                public partial Task<int> InsertAsync(Account account, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        AssertNoErrors(result);

        var entity = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("Account.InquiryEntity.g.cs", StringComparison.Ordinal));
        var entityText = entity.GetText().ToString();
        // Read: nullable value-type guard wraps the FromProvider call.
        Assert.Contains("reader.IsDBNull(1) ? (global::Demo.Money?)null : global::Inquiry.Entities.InquiryConverterCache<global::Demo.MoneyConverter>.Instance.FromProvider(reader.GetDecimal(1))", entityText);

        var store = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("AccountStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var storeText = store.GetText().ToString();
        // Write: null guard, then ToProvider on the unwrapped .Value.
        Assert.Contains(".ToProvider(", storeText);
        Assert.Contains(".Value", storeText);
    }

    [Fact]
    public void ConverterWithUnsignedProviderTypeReinterpretsOnBindAndRead()
    {
        // #92: a converter whose PROVIDER type is unsigned/sbyte must reinterpret to the same-width
        // storage partner on both sides (uint<->int here), exactly like a plain/enum unsigned column —
        // providers reject DbType.UInt32 and GetFieldValue<uint> throws InvalidCastException.
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry;
            using Inquiry.Entities;
            using Inquiry.Stores;

            namespace Demo;

            public struct Counter { public uint Value { get; set; } }

            public sealed class CounterConverter : IInquiryValueConverter<Counter, uint>
            {
                public uint ToProvider(Counter model) => model.Value;
                public Counter FromProvider(uint provider) => new Counter { Value = provider };
            }

            [InquiryTable("Gauge")]
            public sealed class Gauge
            {
                [InquiryKey(IsGenerated = true)]
                public long Id { get; set; }

                [InquiryColumn("Ticks", Converter = typeof(CounterConverter))]
                public Counter Ticks { get; set; }
            }

            public partial class GaugeStore : Inquiry.Stores.InquiryStore<Demo.Gauge>
            {
                [InquiryInsert]
                public partial Task<int> InsertAsync(Gauge gauge, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        AssertNoErrors(result);

        var entity = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("Gauge.InquiryEntity.g.cs", StringComparison.Ordinal));
        var entityText = entity.GetText().ToString();
        // Read: signed storage read, reinterpreted back to uint before FromProvider (not GetFieldValue<uint>).
        Assert.Contains("FromProvider(unchecked((uint)reader.GetInt32(1)))", entityText);
        Assert.DoesNotContain("GetFieldValue<uint>", entityText);

        var store = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("GaugeStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var storeText = store.GetText().ToString();
        // Write: the ToProvider result is reinterpreted to int before boxing, bound with the signed DbType.
        Assert.Contains("(object)unchecked((int)(global::Inquiry.Entities.InquiryConverterCache<global::Demo.CounterConverter>.Instance.ToProvider(", storeText);
        Assert.Contains("global::System.Data.DbType.Int32", storeText);
        Assert.DoesNotContain("global::System.Data.DbType.UInt32", storeText);
    }

    [Fact]
    public void ConverterNotImplementingInterfaceReportsDiagnostic()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry;
            using Inquiry.Entities;
            using Inquiry.Stores;

            namespace Demo;

            public sealed class NotAConverter { }

            [InquiryTable("Account")]
            public sealed class Account
            {
                [InquiryKey(IsGenerated = true)]
                public long Id { get; set; }

                [InquiryColumn("Balance", Converter = typeof(NotAConverter))]
                public decimal Balance { get; set; }
            }

            public partial class AccountStore : Inquiry.Stores.InquiryStore<Demo.Account>
            {
            }
            """;

        var result = RunGenerator(source);
        Assert.Contains(result.RunResult.Diagnostics, d => d.Id == "INQ037");
    }

    [Fact]
    public void ConverterWithUnsupportedProviderTypeReportsINQ038()
    {
        const string source = """
            using Inquiry.Entities;
            namespace Demo;
            public sealed class Model { }
            public sealed class Provider { }
            public sealed class BadConverter : IInquiryValueConverter<Model, Provider>
            {
                public Provider ToProvider(Model model) => new();
                public Model FromProvider(Provider provider) => new();
            }
            [InquiryTable("Bad")]
            public sealed class Bad
            {
                [InquiryKey] public int Id { get; set; }
                [InquiryColumn(Converter = typeof(BadConverter))] public Model Value { get; set; } = new();
            }
            """;

        var result = RunGenerator(source);
        Assert.Contains(result.RunResult.Diagnostics, d => d.Id == "INQ038");
    }

    [Fact]
    public void GuidProviderConverterInUsesGuidJsonTableType()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry.Entities;
            using Inquiry.Stores;
            namespace Demo;
            public readonly struct ExternalId { public ExternalId(Guid value) => Value = value; public Guid Value { get; } }
            public sealed class ExternalIdConverter : IInquiryValueConverter<ExternalId, Guid>
            {
                public Guid ToProvider(ExternalId model) => model.Value;
                public ExternalId FromProvider(Guid provider) => new ExternalId(provider);
            }
            [InquiryTable("Thing")]
            public sealed class Thing
            {
                [InquiryKey] public int Id { get; set; }
                [InquiryColumn(Converter = typeof(ExternalIdConverter))] public ExternalId ExternalId { get; set; }
            }
            public partial class ThingStore : InquiryStore<Thing>
            {
                [InquirySelectAllByPredicate]
                [InquiryWhere("ExternalId", Compare.In)]
                public partial Task<IReadOnlyList<Thing>> ByIds(IReadOnlyList<ExternalId> ids, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source, dialect: "MySql");
        AssertNoErrors(result);
        var text = Assert.Single(result.RunResult.GeneratedTrees,
            static t => t.FilePath.EndsWith("ThingStore.InquiryStore.g.cs", StringComparison.Ordinal)).GetText().ToString();
        Assert.Contains("COLUMNS(val CHAR(36) PATH '$')", text);
        Assert.Contains("ExternalIdConverter>.Instance.ToProvider(_e)", text);
        Assert.DoesNotContain(result.RunResult.Diagnostics, d => d.Id == "INQ038");
    }

    private const string ConverterInSource = """
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using Inquiry;
        using Inquiry.Entities;
        using Inquiry.Stores;

        namespace Demo;

        public struct Money { public decimal Amount { get; set; } }

        public sealed class MoneyConverter : IInquiryValueConverter<Money, decimal>
        {
            public decimal ToProvider(Money model) => model.Amount;
            public Money FromProvider(decimal provider) => new Money { Amount = provider };
        }

        [InquiryTable("Account")]
        public sealed class Account
        {
            [InquiryKey(IsGenerated = true)]
            public long Id { get; set; }

            [InquiryColumn("Balance", Converter = typeof(MoneyConverter))]
            public Money Balance { get; set; }
        }

        public partial class AccountStore : Inquiry.Stores.InquiryStore<Demo.Account>
        {
            [InquirySelectAllByPredicate]
            [InquiryWhere("Balance", Compare.In)]
            public partial Task<IReadOnlyList<Account>> ByBalancesAsync(IReadOnlyList<Money> balances, CancellationToken cancellationToken = default);

            [InquirySelectAllByPredicate]
            [InquiryWhere("Balance", Compare.NotIn)]
            public partial Task<IReadOnlyList<Account>> ExcludeBalancesAsync(IReadOnlyList<Money> balances, CancellationToken cancellationToken = default);
        }
        """;

    [Fact]
    public void ConverterInPredicateProjectsThroughToProvider()
    {
        var result = RunGenerator(ConverterInSource);
        AssertNoErrors(result);

        var tree = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("AccountStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var text = tree.GetText().ToString();

        // The IN collection must be projected through ToProvider so the provider sees the decimal, not the Money struct.
        Assert.Contains("global::System.Linq.Enumerable.Select(balances, static _e => global::Inquiry.Entities.InquiryConverterCache<global::Demo.MoneyConverter>.Instance.ToProvider(_e))", text);
    }

    [Fact]
    public void ConverterNotInPredicateProjectsThroughToProvider()
    {
        var result = RunGenerator(ConverterInSource);
        AssertNoErrors(result);

        var tree = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("AccountStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var text = tree.GetText().ToString();

        // The NOT IN collection must also be projected through ToProvider (behind a null guard).
        Assert.Contains("InquiryInExpansion.ExpandNotIn(_c, \"@Balance\", balances is null ? null : global::System.Linq.Enumerable.Select(balances, static _e => global::Inquiry.Entities.InquiryConverterCache<global::Demo.MoneyConverter>.Instance.ToProvider(_e))", text);
    }

    [Fact]
    public void ConverterInPredicateProjectsThroughArrayBindOnPostgreSql()
    {
        // The PostgreSQL '= ANY(array)' path binds the whole collection via InquiryArrayParameter.Bind;
        // the projection must reach it too, so the bound array carries provider decimals, not Money structs.
        var result = RunGenerator(ConverterInSource, dialect: "PostgreSql");
        AssertNoErrors(result);

        var tree = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("AccountStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var text = tree.GetText().ToString();

        Assert.Contains("InquiryArrayParameter.Bind(_c, \"@Balance\", balances is null ? null : global::System.Linq.Enumerable.Select(balances, static _e => global::Inquiry.Entities.InquiryConverterCache<global::Demo.MoneyConverter>.Instance.ToProvider(_e)))", text);
    }

    [Fact]
    public void ReferenceTypeConverterInPredicateGuardsNullElements()
    {
        // A reference-type converter model can hold null elements; the projection must guard each so
        // ToProvider is never called on null, binding a typed null instead (matching the scalar binder).
        const string source = """
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry;
            using Inquiry.Entities;
            using Inquiry.Stores;

            namespace Demo;

            public sealed class Label { public string Text { get; set; } = string.Empty; }

            public sealed class LabelConverter : IInquiryValueConverter<Label, string>
            {
                public string ToProvider(Label model) => model.Text;
                public Label FromProvider(string provider) => new Label { Text = provider };
            }

            [InquiryTable("Item")]
            public sealed class Item
            {
                [InquiryKey(IsGenerated = true)]
                public long Id { get; set; }

                [InquiryColumn("Tag", Converter = typeof(LabelConverter))]
                public Label Tag { get; set; } = new Label();
            }

            public partial class ItemStore : Inquiry.Stores.InquiryStore<Demo.Item>
            {
                [InquirySelectAllByPredicate]
                [InquiryWhere("Tag", Compare.In)]
                public partial Task<IReadOnlyList<Item>> ByTagsAsync(IReadOnlyList<Label> tags, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        AssertNoErrors(result);

        var tree = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("ItemStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var text = tree.GetText().ToString();

        // Per-element null guard, binding a typed (string?)null instead of calling ToProvider(null).
        Assert.Contains("global::System.Linq.Enumerable.Select(tags, static _e => _e is null ? (string?)null : global::Inquiry.Entities.InquiryConverterCache<global::Demo.LabelConverter>.Instance.ToProvider(_e))", text);
    }
}
