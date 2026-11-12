using System.Globalization;
using RepoDb.Attributes;
using RepoDb.Interfaces;
using RepoDb.Options;

namespace Inquiry.Benchmarks.RepoDB;

[Map("Shippers")]
public sealed class RdShipper
{
    [Primary, Identity, Map("ShipperID")]
    public int ShipperID { get; set; }

    [Map("CompanyName")]
    public string CompanyName { get; set; } = "";

    [Map("Phone")]
    public string? Phone { get; set; }
}

[Map("Customers")]
public sealed class RdCustomer
{
    [Primary, Map("CustomerID")]
    public string CustomerID { get; set; } = "";

    [Map("CompanyName")]
    public string CompanyName { get; set; } = "";

    [Map("ContactName")]
    public string? ContactName { get; set; }

    [Map("ContactTitle")]
    public string? ContactTitle { get; set; }

    [Map("Address")]
    public string? Address { get; set; }

    [Map("City")]
    public string? City { get; set; }

    [Map("Region")]
    public string? Region { get; set; }

    [Map("PostalCode")]
    public string? PostalCode { get; set; }

    [Map("Country")]
    public string? Country { get; set; }

    [Map("Phone")]
    public string? Phone { get; set; }

    [Map("Fax")]
    public string? Fax { get; set; }
}

[Map("Products")]
public sealed class RdProduct
{
    [Primary, Identity, Map("ProductID")]
    public int ProductID { get; set; }

    [Map("ProductName")]
    public string ProductName { get; set; } = "";

    [Map("SupplierID")]
    public int? SupplierID { get; set; }

    [Map("CategoryID")]
    public int? CategoryID { get; set; }

    [Map("QuantityPerUnit")]
    public string? QuantityPerUnit { get; set; }

    [Map("UnitPrice"), PropertyHandler(typeof(SqliteDecimalPropertyHandler))]
    public decimal? UnitPrice { get; set; }

    [Map("UnitsInStock")]
    public short? UnitsInStock { get; set; }

    [Map("UnitsOnOrder")]
    public short? UnitsOnOrder { get; set; }

    [Map("ReorderLevel")]
    public short? ReorderLevel { get; set; }

    [Map("Discontinued"), PropertyHandler(typeof(SqliteBooleanPropertyHandler))]
    public bool Discontinued { get; set; }
}

/// <summary>
/// SQLite stores <c>Discontinued</c> as INTEGER, and Microsoft.Data.Sqlite reads it as Int64. RepoDB has no
/// Int64 to Boolean conversion, so this handler maps 0 and 1 the same way the ADO.NET legs do.
/// </summary>
public sealed class SqliteBooleanPropertyHandler : IPropertyHandler<long, bool>
{
    public bool Get(long input, PropertyHandlerGetOptions options) => input != 0;

    public long Set(bool input, PropertyHandlerSetOptions options) => input ? 1L : 0L;
}

/// <summary>
/// SQLite stores <c>UnitPrice</c> as REAL, and Microsoft.Data.Sqlite reads it as Double. From .NET 11 the
/// Double to Decimal conversion is exact, so 21.99 becomes 21.98999. This handler parses the shortest
/// round-trip text instead, the same way the ADO.NET legs read the value.
/// </summary>
public sealed class SqliteDecimalPropertyHandler : IPropertyHandler<double?, decimal?>
{
    public decimal? Get(double? input, PropertyHandlerGetOptions options)
        => input is { } value ? decimal.Parse(value.ToString("R", CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture) : null;

    public double? Set(decimal? input, PropertyHandlerSetOptions options) => (double?)input;
}

[Map("Region")]
public sealed class RdRegion
{
    [Primary, Map("RegionID")]
    public int RegionID { get; set; }

    [Map("RegionDescription")]
    public string RegionDescription { get; set; } = "";
}
