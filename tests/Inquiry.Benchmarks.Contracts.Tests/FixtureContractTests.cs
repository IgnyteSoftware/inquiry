using Inquiry.Benchmarks.Contracts.Fixtures;

namespace Inquiry.Benchmarks.Contracts.Tests;

public sealed class FixtureContractTests
{
    [Fact]
    public void CatalogDefinesCompleteThirteenTableNorthwindGraph()
    {
        Assert.Equal(13, NorthwindFixtureCatalog.Schema.Tables.Count);
        Assert.Equal(13, NorthwindFixtureCatalog.Schema.ForeignKeys.Count);
        Assert.Contains(NorthwindFixtureCatalog.Schema.Tables, static table => table.Name == "Order Details");
        Assert.Contains(NorthwindFixtureCatalog.Schema.Tables, static table => table.Name == "CustomerCustomerDemo");
        Assert.Matches("^[a-f0-9]{64}$", NorthwindFixtureCatalog.SchemaHash);
        Assert.All(NorthwindFixtureCatalog.Schema.Tables, static table =>
        {
            Assert.NotEmpty(table.Statistics);
            Assert.All(table.Columns, static column => Assert.False(string.IsNullOrWhiteSpace(column.DatabaseType)));
        });
        Assert.Equal(5, DatabaseImageCatalog.Images.Count);
        Assert.All(DatabaseImageCatalog.Images, static image => Assert.Matches("^sha256:[a-f0-9]{64}$", image.Digest));
    }

    [Theory]
    [InlineData(FixtureTier.Tiny, 100, 1_000, 5_000, 100)]
    [InlineData(FixtureTier.Standard, 10_000, 100_000, 500_000, 10_000)]
    [InlineData(FixtureTier.Large, 100_000, 1_000_000, 5_000_000, 100_000)]
    public void TierCountsAreFixed(FixtureTier tier, int customers, int orders, int details, int products)
    {
        var manifest = NorthwindFixtureCatalog.For(tier);
        Assert.Equal(customers, manifest.RowCounts["Customers"]);
        Assert.Equal(orders, manifest.RowCounts["Orders"]);
        Assert.Equal(details, manifest.RowCounts["Order Details"]);
        Assert.Equal(products, manifest.RowCounts["Products"]);
        Assert.Equal(13, manifest.RowCounts.Count);
        Assert.All(manifest.TableChecksums, static pair => Assert.Matches("^[a-f0-9]{64}$", pair.Value));
    }

    [Fact]
    public void GeneratorIsDeterministicAndSeedDriftChangesChecksum()
    {
        var first = NorthwindFixtureGenerator.Generate("Orders", FixtureTier.Tiny, NorthwindFixtureCatalog.Seed).Take(20).ToArray();
        var second = NorthwindFixtureGenerator.Generate("Orders", FixtureTier.Tiny, NorthwindFixtureCatalog.Seed).Take(20).ToArray();
        var drifted = NorthwindFixtureGenerator.Generate("Orders", FixtureTier.Tiny, NorthwindFixtureCatalog.Seed + 1).Take(20).ToArray();

        Assert.Equal(first, second);
        Assert.NotEqual(FixtureChecksum.Compute(first), FixtureChecksum.Compute(drifted));
    }

    [Fact]
    public void TinyTierUsesExactCheckedCustomerAndOrderSelectivityBuckets()
    {
        var customers = NorthwindFixtureGenerator.Generate("Customers", FixtureTier.Tiny, NorthwindFixtureCatalog.Seed).ToArray();
        Assert.Equal(50, customers.Count(static row => (string)row.Values["City"]! == "Hot City"));
        Assert.Equal(35, customers.Count(static row => (string)row.Values["City"]! == "Warm City"));
        Assert.Equal(15, customers.Count(static row => ((string)row.Values["City"]!).StartsWith("City", StringComparison.Ordinal)));

        var orders = NorthwindFixtureGenerator.Generate("Orders", FixtureTier.Tiny, NorthwindFixtureCatalog.Seed).ToArray();
        var hot = customers.Take(1).Select(static row => (string)row.Values["CustomerID"]!).ToHashSet(StringComparer.Ordinal);
        var warm = customers.Skip(1).Take(9).Select(static row => (string)row.Values["CustomerID"]!).ToHashSet(StringComparer.Ordinal);
        var tail = customers.Skip(10).Select(static row => (string)row.Values["CustomerID"]!).ToHashSet(StringComparer.Ordinal);
        Assert.Equal(500, orders.Count(row => hot.Contains((string)row.Values["CustomerID"]!)));
        Assert.Equal(350, orders.Count(row => warm.Contains((string)row.Values["CustomerID"]!)));
        Assert.Equal(150, orders.Count(row => tail.Contains((string)row.Values["CustomerID"]!)));
    }

    [Fact]
    public void CheckedTinyChecksumsMatchGeneratedRows()
    {
        var manifest = NorthwindFixtureCatalog.For(FixtureTier.Tiny);
        foreach (var table in NorthwindFixtureCatalog.Schema.Tables)
        {
            var rows = NorthwindFixtureGenerator.Generate(table.Name, FixtureTier.Tiny, manifest.Seed);
            Assert.Equal(manifest.TableChecksums[table.Name], FixtureChecksum.Compute(rows));
        }
    }

    [Fact]
    public void GeneratedForeignKeysStayInsideDeclaredParentRanges()
    {
        var manifest = NorthwindFixtureCatalog.For(FixtureTier.Tiny);
        foreach (var row in NorthwindFixtureGenerator.Generate("Order Details", FixtureTier.Tiny, manifest.Seed).Take(200))
        {
            Assert.InRange((int)row.Values["OrderID"]!, 1, manifest.RowCounts["Orders"]);
            Assert.InRange((int)row.Values["ProductID"]!, 1, manifest.RowCounts["Products"]);
        }
    }

    [Fact]
    public void EveryGeneratedRowMatchesItsCompleteSchemaColumns()
    {
        foreach (var table in NorthwindFixtureCatalog.Schema.Tables)
        {
            var row = Assert.Single(NorthwindFixtureGenerator.Generate(table.Name, FixtureTier.Tiny, NorthwindFixtureCatalog.Seed).Take(1));
            Assert.Equal(
                table.Columns.Select(static column => column.Name).Order(StringComparer.Ordinal),
                row.Values.Keys.Order(StringComparer.Ordinal));
        }
    }

    [Fact]
    public void FixtureIdentityIncludesSchemaSeedTierSettingsAndChecksums()
    {
        var tiny = NorthwindFixtureCatalog.For(FixtureTier.Tiny);
        Assert.NotEqual(tiny.IdentityHash, (tiny with { Seed = tiny.Seed + 1 }).IdentityHash);
        Assert.Equal("UTC", tiny.TimeZone);
        Assert.False(string.IsNullOrWhiteSpace(tiny.Collation));
        Assert.NotEmpty(tiny.IdentityState);
        Assert.NotEmpty(tiny.SelectivityBuckets);
    }

    [Fact]
    public void ManifestValidatorRejectsSchemaSeedAndTableKeyDrift()
    {
        var manifest = NorthwindFixtureCatalog.For(FixtureTier.Tiny);
        var driftedCounts = manifest.RowCounts
            .Where(static pair => pair.Key != "Products")
            .ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal);
        var drifted = manifest with
        {
            SchemaHash = new string('0', 64),
            Seed = manifest.Seed + 1,
            RowCounts = driftedCounts,
        };

        var codes = FixtureContractValidator.Validate(drifted).Select(static error => error.Code).ToHashSet();
        Assert.Contains("fixture-schema", codes);
        Assert.Contains("fixture-seed", codes);
        Assert.Contains("fixture-table-keys", codes);
    }

    [Fact]
    public void ManifestValidatorRejectsEveryCheckedIdentityFacetDrift()
    {
        var manifest = NorthwindFixtureCatalog.For(FixtureTier.Tiny);
        var checksums = manifest.TableChecksums.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal);
        checksums["Customers"] = new string('0', 64);
        var identities = manifest.IdentityState.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal);
        identities[identities.Keys.First()]++;
        var buckets = manifest.SelectivityBuckets.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal);
        buckets[buckets.Keys.First()] = "drifted";
        var drifted = manifest with
        {
            TableChecksums = checksums,
            IdentityState = identities,
            SelectivityBuckets = buckets,
            Collation = "drifted",
        };

        var codes = FixtureContractValidator.Validate(drifted).Select(static error => error.Code).ToHashSet();
        Assert.Contains("fixture-checksum-drift", codes);
        Assert.Contains("fixture-identity", codes);
        Assert.Contains("fixture-distribution", codes);
        Assert.Contains("fixture-settings", codes);
    }

    [Fact]
    public void StateValidatorRejectsMutationLeakage()
    {
        var manifest = NorthwindFixtureCatalog.For(FixtureTier.Tiny);
        var afterChecksums = manifest.TableChecksums.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal);
        afterChecksums["Orders"] = new string('0', 64);
        var before = FixtureState.FromManifest(manifest);
        var after = before with { TableChecksums = afterChecksums };

        Assert.Contains(FixtureContractValidator.ValidateReset(before, after), static error => error.Code == "fixture-mutation-leakage");
    }
}
