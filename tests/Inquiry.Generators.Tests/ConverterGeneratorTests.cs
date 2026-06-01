using System;

namespace Inquiry.Generators.Tests;

/// <summary>
/// W10b value converters: <c>[InquiryColumn(Converter = typeof(X))]</c> and <c>[InquiryJson]</c> map a
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
        Assert.Contains("Balance = new global::Demo.MoneyConverter().FromProvider(reader.GetDecimal(1))", text);
        Assert.Contains("Meta = reader.IsDBNull(2) ? null : new global::Inquiry.Converters.InquiryJsonConverter<global::System.Collections.Generic.Dictionary<string, string>>().FromProvider(reader.GetString(2))", text);
    }

    [Fact]
    public void ConverterColumnsWriteViaToProviderWithProviderDbType()
    {
        var result = RunGenerator(ConverterSource);
        AssertNoErrors(result);

        var tree = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("AccountStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var text = tree.GetText().ToString();

        Assert.Contains("new global::Demo.MoneyConverter().ToProvider(", text);
        Assert.Contains("new global::Inquiry.Converters.InquiryJsonConverter<global::System.Collections.Generic.Dictionary<string, string>>().ToProvider(", text);
        // Provider DbType, not the model type: decimal for Money, string for the JSON column.
        Assert.Contains("global::System.Data.DbType.Decimal", text);
        Assert.Contains("global::System.Data.DbType.String", text);
        // Nullable JSON model → null guard maps to DBNull.
        Assert.Contains("is null ? global::System.DBNull.Value", text);
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
        Assert.Contains("reader.IsDBNull(1) ? (global::Demo.Money?)null : new global::Demo.MoneyConverter().FromProvider(reader.GetDecimal(1))", entityText);

        var store = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("AccountStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var storeText = store.GetText().ToString();
        // Write: null guard, then ToProvider on the unwrapped .Value.
        Assert.Contains(".ToProvider(", storeText);
        Assert.Contains(".Value", storeText);
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
}
