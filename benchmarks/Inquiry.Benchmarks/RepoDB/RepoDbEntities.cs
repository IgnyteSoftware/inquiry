using RepoDb.Attributes;

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

    [Map("Discontinued")]
    public bool Discontinued { get; set; }
}
