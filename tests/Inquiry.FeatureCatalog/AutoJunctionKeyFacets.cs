using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Inquiry.Entities;
using Inquiry.Stores;

namespace Inquiry.FeatureCatalog;

// ---------------------------------------------------------------------------------------------
// Auto-managed junctions over non-integer keys (#280) with overridden columns declared from both
// sides (#281). AjCode has a bounded-string key, AjPrice a decimal key with precision and scale, and
// AjSku a converter-backed key. AjCode <-> AjPrice overrides both column names, relative to each side;
// AjCode -> AjSku uses the derived names. The tables are created from the generated DDL, so a dropped
// facet or a transposed column shows up on the live engine.
// ---------------------------------------------------------------------------------------------

public readonly record struct AjSkuCode(string Value);

public sealed class AjSkuCodeConverter : IInquiryValueConverter<AjSkuCode, string>
{
    public string ToProvider(AjSkuCode model) => model.Value;
    public AjSkuCode FromProvider(string provider) => new(provider);
}

[InquiryTable("AjCode")]
public sealed class AjCode
{
    [InquiryKey(Length = 24)]
    public string Name { get; set; } = string.Empty;

    [InquiryManyToMany(JunctionTable = "AjCode_Price", ParentColumn = "code_ref", ChildColumn = "price_ref")]
    public List<AjPrice> Prices { get; set; } = new();

    [InquiryManyToMany]
    public List<AjSku> Skus { get; set; } = new();
}

[InquiryTable("AjPrice")]
public sealed class AjPrice
{
    [InquiryKey(Precision = 9, Scale = 2)]
    public decimal Amount { get; set; }

    [InquiryManyToMany(JunctionTable = "AjCode_Price", ParentColumn = "price_ref", ChildColumn = "code_ref")]
    public List<AjCode> Codes { get; set; } = new();
}

[InquiryTable("AjSku")]
public sealed class AjSku
{
    [InquiryKey(Length = 12, IsUnicode = false, Converter = typeof(AjSkuCodeConverter))]
    public AjSkuCode Id { get; set; }
}

public partial class AjCodeStore : InquiryStore<AjCode>
{
    [InquiryInsert]
    public partial Task<int> InsertAsync(AjCode code, CancellationToken cancellationToken = default);

    [InquirySelectOneByKeyEager]
    public partial Task<AjCode?> GetAsync(string name, CancellationToken cancellationToken = default);

    [InquirySelectAllEager]
    public partial IAsyncEnumerable<AjCode> AllAsync(CancellationToken cancellationToken = default);
}

public partial class AjPriceStore : InquiryStore<AjPrice>
{
    [InquiryInsert]
    public partial Task<int> InsertAsync(AjPrice price, CancellationToken cancellationToken = default);

    [InquirySelectOneByKeyEager]
    public partial Task<AjPrice?> GetAsync(decimal amount, CancellationToken cancellationToken = default);

    [InquirySelectAllEager]
    public partial IAsyncEnumerable<AjPrice> AllAsync(CancellationToken cancellationToken = default);
}

public partial class AjSkuStore : InquiryStore<AjSku>
{
    [InquiryInsert]
    public partial Task<int> InsertAsync(AjSku sku, CancellationToken cancellationToken = default);
}

/// <summary>The SQL type each provider reports for the three key columns.</summary>
public sealed record AjKeyTypes(string Name, string Amount, string Sku);

/// <summary>
/// Shared seed and assertions for <see cref="AjCode"/>. Each provider suite supplies its harness, its
/// identifier quoting, and a catalog query that describes one column's SQL type.
/// </summary>
public static class AutoJunctionKeyFacetAssertions
{
    public static readonly string[] Tables = { "AjCode", "AjPrice", "AjSku" };

    /// <summary>
    /// Every junction column must report exactly the SQL type of the key it references, the key types
    /// must carry the declared length, precision, and scale, and each junction foreign key must reject a
    /// link to a missing row.
    /// </summary>
    public static async Task JunctionColumnsMatchKeysAndEnforceForeignKeysAsync(
        DbConnection connection,
        Func<string, string> quote,
        Func<string, string, string> describeColumnSql,
        AjKeyTypes expected)
    {
        async Task<string> Describe(string table, string column)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = describeColumnSql(table, column);
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync(), $"No catalog row for {table}.{column}.");
            var parts = new string[reader.FieldCount];
            for (var i = 0; i < parts.Length; i++)
            {
                parts[i] = reader.IsDBNull(i) ? string.Empty : Convert.ToString(reader.GetValue(i), System.Globalization.CultureInfo.InvariantCulture)!;
            }

            return string.Join("/", parts);
        }

        Assert.Equal(expected.Name, await Describe("AjCode", "Name"));
        Assert.Equal(expected.Amount, await Describe("AjPrice", "Amount"));
        Assert.Equal(expected.Sku, await Describe("AjSku", "Id"));
        Assert.Equal(expected.Name, await Describe("AjCode_Price", "code_ref"));
        Assert.Equal(expected.Amount, await Describe("AjCode_Price", "price_ref"));
        Assert.Equal(expected.Name, await Describe("AjCode_AjSku", "AjCode_Name"));
        Assert.Equal(expected.Sku, await Describe("AjCode_AjSku", "AjSku_Id"));

        await ExecuteAsync(connection, $"INSERT INTO {quote("AjCode")} ({quote("Name")}) VALUES ('alpha')");
        await ExecuteAsync(connection, $"INSERT INTO {quote("AjPrice")} ({quote("Amount")}) VALUES (1.50)");
        await ExecuteAsync(connection, $"INSERT INTO {quote("AjSku")} ({quote("Id")}) VALUES ('SKU-1')");
        await Assert.ThrowsAnyAsync<DbException>(() => LinkPriceAsync(connection, quote, "alpha", "7.77"));
        await Assert.ThrowsAnyAsync<DbException>(() => LinkPriceAsync(connection, quote, "nobody", "1.50"));
        await Assert.ThrowsAnyAsync<DbException>(() => LinkSkuAsync(connection, quote, "alpha", "SKU-9"));
        await Assert.ThrowsAnyAsync<DbException>(() => LinkSkuAsync(connection, quote, "nobody", "SKU-1"));
    }

    /// <summary>
    /// Seeds an asymmetric association and reads it from both navigations. Each side filters on the
    /// column that references its own key, so a transposed override returns the wrong rows or none.
    /// </summary>
    public static async Task BothNavigationsReadTheirOwnLinksAsync(
        AjCodeStore codes,
        AjPriceStore prices,
        AjSkuStore skus,
        DbConnection connection,
        Func<string, string> quote)
    {
        foreach (var name in new[] { "alpha", "beta", "gamma" })
        {
            await codes.InsertAsync(new AjCode { Name = name });
        }

        foreach (var amount in new[] { 1.50m, 2.25m, 9.99m })
        {
            await prices.InsertAsync(new AjPrice { Amount = amount });
        }

        foreach (var id in new[] { "SKU-1", "SKU-2", "SKU-3" })
        {
            await skus.InsertAsync(new AjSku { Id = new AjSkuCode(id) });
        }

        // alpha -> 1.50, 2.25; beta -> 2.25. alpha -> SKU-2; beta -> SKU-1, SKU-2. gamma, 9.99, and
        // SKU-3 have no links.
        await LinkPriceAsync(connection, quote, "alpha", "1.50");
        await LinkPriceAsync(connection, quote, "alpha", "2.25");
        await LinkPriceAsync(connection, quote, "beta", "2.25");
        await LinkSkuAsync(connection, quote, "alpha", "SKU-2");
        await LinkSkuAsync(connection, quote, "beta", "SKU-1");
        await LinkSkuAsync(connection, quote, "beta", "SKU-2");

        var expectedPrices = new Dictionary<string, decimal[]>
        {
            ["alpha"] = new[] { 1.50m, 2.25m },
            ["beta"] = new[] { 2.25m },
            ["gamma"] = Array.Empty<decimal>(),
        };
        var expectedSkus = new Dictionary<string, string[]>
        {
            ["alpha"] = new[] { "SKU-2" },
            ["beta"] = new[] { "SKU-1", "SKU-2" },
            ["gamma"] = Array.Empty<string>(),
        };
        var expectedCodes = new Dictionary<decimal, string[]>
        {
            [1.50m] = new[] { "alpha" },
            [2.25m] = new[] { "alpha", "beta" },
            [9.99m] = Array.Empty<string>(),
        };

        foreach (var name in expectedPrices.Keys)
        {
            var code = await codes.GetAsync(name);
            Assert.NotNull(code);
            AssertCode(code!, expectedPrices[name], expectedSkus[name]);
        }

        var allCodes = new List<AjCode>();
        await foreach (var code in codes.AllAsync())
        {
            allCodes.Add(code);
        }

        Assert.Equal(expectedPrices.Keys.OrderBy(static n => n, StringComparer.Ordinal), allCodes.Select(static c => c.Name).OrderBy(static n => n, StringComparer.Ordinal));
        foreach (var code in allCodes)
        {
            AssertCode(code, expectedPrices[code.Name], expectedSkus[code.Name]);
        }

        foreach (var amount in expectedCodes.Keys)
        {
            var price = await prices.GetAsync(amount);
            Assert.NotNull(price);
            Assert.Equal(expectedCodes[amount], Names(price!.Codes));
        }

        var allPrices = new List<AjPrice>();
        await foreach (var price in prices.AllAsync())
        {
            allPrices.Add(price);
        }

        Assert.Equal(3, allPrices.Count);
        foreach (var price in allPrices)
        {
            Assert.Equal(expectedCodes[price.Amount], Names(price.Codes));
        }
    }

    private static void AssertCode(AjCode code, decimal[] expectedPrices, string[] expectedSkus)
    {
        Assert.Equal(expectedPrices, code.Prices.Select(static p => p.Amount).OrderBy(static a => a));
        Assert.Equal(expectedSkus, code.Skus.Select(static s => s.Id.Value).OrderBy(static s => s, StringComparer.Ordinal));
    }

    private static string[] Names(IEnumerable<AjCode> codes)
        => codes.Select(static c => c.Name).OrderBy(static n => n, StringComparer.Ordinal).ToArray();

    private static Task LinkPriceAsync(DbConnection connection, Func<string, string> quote, string code, string amount)
        => ExecuteAsync(
            connection,
            $"INSERT INTO {quote("AjCode_Price")} ({quote("code_ref")}, {quote("price_ref")}) VALUES ('{code}', {amount})");

    private static Task LinkSkuAsync(DbConnection connection, Func<string, string> quote, string code, string sku)
        => ExecuteAsync(
            connection,
            $"INSERT INTO {quote("AjCode_AjSku")} ({quote("AjCode_Name")}, {quote("AjSku_Id")}) VALUES ('{code}', '{sku}')");

    private static async Task ExecuteAsync(DbConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
