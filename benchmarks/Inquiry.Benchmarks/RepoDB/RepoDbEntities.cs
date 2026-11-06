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

    [Map("Country")]
    public string? Country { get; set; }

    [Map("City")]
    public string? City { get; set; }
}

[Map("Products")]
public sealed class RdProduct
{
    [Primary, Identity, Map("ProductID")]
    public int ProductID { get; set; }

    [Map("ProductName")]
    public string ProductName { get; set; } = "";

    [Map("CategoryID")]
    public int? CategoryID { get; set; }

    [Map("UnitPrice")]
    public decimal? UnitPrice { get; set; }

    [Map("UnitsInStock")]
    public short? UnitsInStock { get; set; }

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

[Map("Region")]
public sealed class RdRegion
{
    [Primary, Map("RegionID")]
    public int RegionID { get; set; }

    [Map("RegionDescription")]
    public string RegionDescription { get; set; } = "";
}
