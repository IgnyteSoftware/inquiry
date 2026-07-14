using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Inquiry.Benchmarks.Contracts.Fixtures;

public sealed class SeedRow : IEquatable<SeedRow>
{
    public SeedRow(string table, int ordinal, IReadOnlyDictionary<string, object?> values)
    {
        Table = table;
        Ordinal = ordinal;
        Values = values;
        CanonicalText = Canonicalize(values);
    }

    public string Table { get; }
    public int Ordinal { get; }
    public IReadOnlyDictionary<string, object?> Values { get; }
    public string CanonicalText { get; }

    public bool Equals(SeedRow? other)
        => other is not null && Table == other.Table && Ordinal == other.Ordinal && CanonicalText == other.CanonicalText;

    public override bool Equals(object? obj) => obj is SeedRow other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Table, Ordinal, CanonicalText);

    private static string Canonicalize(IReadOnlyDictionary<string, object?> values)
        => CanonicalHash.Join(values.OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .Select(static pair => pair.Key + "=" + Format(pair.Value)));

    private static string Format(object? value) => value switch
    {
        null => "null",
        DateTime dateTime => dateTime.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
        DateOnly date => date.ToString("O", CultureInfo.InvariantCulture),
        bool boolean => boolean ? "true" : "false",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty,
        _ => value.ToString() ?? string.Empty,
    };
}

public static class FixtureChecksum
{
    public static string Compute(IEnumerable<SeedRow> rows)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var row in rows)
        {
            hash.AppendData(Encoding.UTF8.GetBytes(row.Table));
            hash.AppendData([0]);
            hash.AppendData(Encoding.UTF8.GetBytes(row.Ordinal.ToString(CultureInfo.InvariantCulture)));
            hash.AppendData([0]);
            hash.AppendData(Encoding.UTF8.GetBytes(row.CanonicalText));
            hash.AppendData([10]);
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
}

public static class NorthwindFixtureGenerator
{
    private static readonly DateTime Epoch = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public static IEnumerable<SeedRow> Generate(string table, FixtureTier tier, int seed)
    {
        var manifest = NorthwindFixtureCatalog.For(tier);
        if (!manifest.RowCounts.TryGetValue(table, out var count))
            throw new ArgumentOutOfRangeException(nameof(table), table, "Unknown Northwind fixture table.");

        for (var ordinal = 1; ordinal <= count; ordinal++)
            yield return GenerateRow(table, ordinal, manifest, seed);
    }

    public static IReadOnlyDictionary<string, string> ComputeTableChecksums(FixtureTier tier, int? seed = null)
    {
        var actualSeed = seed ?? NorthwindFixtureCatalog.For(tier).Seed;
        return NorthwindFixtureCatalog.Schema.Tables.ToDictionary(
            static table => table.Name,
            table => FixtureChecksum.Compute(Generate(table.Name, tier, actualSeed)),
            StringComparer.Ordinal);
    }

    private static SeedRow GenerateRow(string table, int i, FixtureManifest manifest, int seed)
    {
        var random = Mix(((ulong)(uint)seed << 32) | (uint)i, StableTableSalt(table));
        return table switch
        {
            "Categories" => Row(table, i,
                ("CategoryID", i),
                ("CategoryName", $"Category {i:D3}"),
                ("Description", i % 7 == 0 ? null : $"Deterministic category {i:D3}")),
            "Customers" => Row(table, i,
                ("CustomerID", Base36(i, 5)),
                ("CompanyName", $"Company {i:D6}"),
                ("ContactName", $"Contact {i:D6}"),
                ("ContactTitle", $"Role {i % 12:D2}"),
                ("Address", $"{i % 10_000} Deterministic Street"),
                ("City", CustomerCity(i, random)),
                ("Region", i % 5 == 0 ? null : $"R{Bucket(random >> 24, 16):D2}"),
                ("PostalCode", $"{Bucket(random >> 32, 100_000):D5}"),
                ("Country", $"Country {Bucket(random >> 40, 12):D2}"),
                ("Phone", $"555-{i % 10_000:D4}"),
                ("Fax", i % 4 == 0 ? null : $"556-{i % 10_000:D4}")),
            "CustomerDemographics" => Row(table, i,
                ("CustomerTypeID", $"TYPE{i:D6}"),
                ("CustomerDesc", $"Deterministic demographic {i:D3}")),
            "CustomerCustomerDemo" => Row(table, i,
                ("CustomerID", Base36(((i - 1) % manifest.RowCounts["Customers"]) + 1, 5)),
                ("CustomerTypeID", $"TYPE{((i - 1) % manifest.RowCounts["CustomerDemographics"]) + 1:D6}")),
            "Employees" => Row(table, i,
                ("EmployeeID", i),
                ("LastName", $"Employee{i:D4}"),
                ("FirstName", $"First{i:D4}"),
                ("Title", $"Title {i % 8:D2}"),
                ("TitleOfCourtesy", i % 3 == 0 ? "Dr." : null),
                ("BirthDate", Epoch.AddDays(-10_000 - (i * 31))),
                ("HireDate", Epoch.AddDays(-2_000 + i)),
                ("Address", $"{i} Employee Avenue"),
                ("City", $"City {i % 20:D2}"),
                ("Region", i % 5 == 0 ? null : $"R{i % 16:D2}"),
                ("PostalCode", $"{10_000 + i % 90_000:D5}"),
                ("Country", $"Country {i % 12:D2}"),
                ("HomePhone", $"557-{i % 10_000:D4}"),
                ("Extension", $"{i % 1_000:D3}"),
                ("Notes", $"Deterministic employee note {i:D4}"),
                ("ReportsTo", i == 1 ? null : ((i - 2) % manifest.RowCounts["Employees"]) + 1),
                ("PhotoPath", $"employees/{i:D4}.jpg")),
            "EmployeeTerritories" => Row(table, i,
                ("EmployeeID", ((i - 1) % manifest.RowCounts["Employees"]) + 1),
                ("TerritoryID", $"T{(((i - 1) / manifest.RowCounts["Employees"]) % manifest.RowCounts["Territories"]) + 1:D6}")),
            "Orders" => Row(table, i,
                ("OrderID", i),
                ("CustomerID", Base36(HotSetKey(i, manifest.RowCounts["Customers"]), 5)),
                ("EmployeeID", ((i - 1) % manifest.RowCounts["Employees"]) + 1),
                ("OrderDate", Epoch.AddDays(i % 1_461).AddMinutes(Bucket(random, 1_440))),
                ("RequiredDate", Epoch.AddDays((i % 1_461) + 14)),
                ("ShippedDate", i % 17 == 0 ? null : Epoch.AddDays((i % 1_461) + 3)),
                ("ShipVia", ((i - 1) % manifest.RowCounts["Shippers"]) + 1),
                ("Freight", decimal.Round((decimal)(random % 50_000) / 100m, 2)),
                ("ShipName", $"Recipient {i:D7}"),
                ("ShipAddress", $"{i % 10_000} Shipping Lane"),
                ("ShipCity", $"City {Bucket(random >> 16, 97):D2}"),
                ("ShipRegion", i % 6 == 0 ? null : $"R{Bucket(random >> 24, 16):D2}"),
                ("ShipPostalCode", $"{Bucket(random >> 32, 100_000):D5}"),
                ("ShipCountry", $"Country {Bucket(random >> 40, 12):D2}")),
            "Order Details" => OrderDetailRow(table, i, manifest, seed),
            "Products" => Row(table, i,
                ("ProductID", i),
                ("ProductName", $"Product {i:D6}"),
                ("SupplierID", ((i - 1) % manifest.RowCounts["Suppliers"]) + 1),
                ("CategoryID", ((i - 1) % manifest.RowCounts["Categories"]) + 1),
                ("QuantityPerUnit", $"{(i % 24) + 1} units"),
                ("UnitPrice", decimal.Round((decimal)(random % 25_000) / 100m + 1m, 2)),
                ("UnitsInStock", (short)(random % 200)),
                ("UnitsOnOrder", (short)((random >> 8) % 50)),
                ("ReorderLevel", (short)((random >> 16) % 25)),
                ("Discontinued", i % 19 == 0)),
            "Region" => Row(table, i, ("RegionID", i), ("RegionDescription", $"Region {i:D3}")),
            "Shippers" => Row(table, i, ("ShipperID", i), ("CompanyName", $"Shipper {i:D3}"), ("Phone", $"555-7{i:D3}")),
            "Suppliers" => Row(table, i,
                ("SupplierID", i),
                ("CompanyName", $"Supplier {i:D5}"),
                ("ContactName", $"Supplier Contact {i:D5}"),
                ("ContactTitle", $"Supplier Role {i % 8:D2}"),
                ("Address", $"{i} Supplier Road"),
                ("City", $"City {i % 97:D2}"),
                ("Region", i % 5 == 0 ? null : $"R{i % 16:D2}"),
                ("PostalCode", $"{20_000 + i % 80_000:D5}"),
                ("Country", $"Country {i % 12:D2}"),
                ("Phone", $"555-8{i % 1_000:D3}"),
                ("Fax", i % 4 == 0 ? null : $"558-{i % 10_000:D4}"),
                ("HomePage", $"https://supplier.invalid/{i:D5}")),
            "Territories" => Row(table, i,
                ("TerritoryID", $"T{i:D6}"),
                ("TerritoryDescription", $"Territory {i:D6}"),
                ("RegionID", ((i - 1) % manifest.RowCounts["Region"]) + 1)),
            _ => throw new ArgumentOutOfRangeException(nameof(table), table, "Unknown Northwind fixture table."),
        };
    }

    private static SeedRow OrderDetailRow(string table, int i, FixtureManifest manifest, int seed)
    {
        const int detailsPerOrder = 5;
        var orderId = ((i - 1) / detailsPerOrder) + 1;
        var slot = (i - 1) % detailsPerOrder;
        var productId = ((orderId * 17 + slot + (seed % 31)) % manifest.RowCounts["Products"]) + 1;
        var random = Mix(((ulong)(uint)seed << 32) | (uint)i, StableTableSalt(table));
        return Row(table, i,
            ("OrderID", orderId),
            ("ProductID", productId),
            ("UnitPrice", decimal.Round((decimal)(random % 25_000) / 100m + 1m, 2)),
            ("Quantity", (short)((random % 25) + 1)),
            ("Discount", (float)((random >> 8) % 4) * 0.05f));
    }

    private static SeedRow Row(string table, int ordinal, params (string Key, object? Value)[] values)
    {
        var sorted = new SortedDictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (key, value) in values) sorted.Add(key, value);
        return new SeedRow(table, ordinal, new ReadOnlyDictionary<string, object?>(sorted));
    }

    private static string CustomerCity(int ordinal, ulong random)
    {
        var bucket = (ordinal - 1) % 100;
        if (bucket < 50) return "Hot City";
        if (bucket < 85) return "Warm City";
        return $"City {Bucket(random >> 16, 97):D2}";
    }

    private static int HotSetKey(int ordinal, int count)
    {
        // A stable hot-set distribution: 50% target the first 1%, 35% the next 9%, and 15% the tail.
        var bucket = ordinal % 20;
        var hot = Math.Max(1, count / 100);
        var warm = Math.Max(1, count / 10);
        if (bucket < 10) return ((ordinal * 17) % hot) + 1;
        if (bucket < 17) return hot + 1 + ((ordinal * 17) % Math.Max(1, warm - hot));
        return warm + 1 + ((ordinal * 17) % Math.Max(1, count - warm));
    }

    private static int Bucket(ulong value, int count) => (int)(value % (uint)count);

    private static ulong StableTableSalt(string table)
    {
        var hash = 14695981039346656037UL;
        foreach (var c in table)
        {
            hash ^= c;
            hash *= 1099511628211UL;
        }
        return hash;
    }

    private static ulong Mix(ulong value, ulong salt)
    {
        var x = value ^ salt;
        x ^= x >> 12;
        x ^= x << 25;
        x ^= x >> 27;
        return x * 2685821657736338717UL;
    }

    private static string Base36(int value, int width)
    {
        const string alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        Span<char> buffer = stackalloc char[width];
        var remaining = value;
        for (var i = width - 1; i >= 0; i--)
        {
            buffer[i] = alphabet[remaining % 36];
            remaining /= 36;
        }
        if (remaining != 0) throw new ArgumentOutOfRangeException(nameof(value), value, "Value exceeds the fixed-width base36 key space.");
        return new string(buffer);
    }
}
