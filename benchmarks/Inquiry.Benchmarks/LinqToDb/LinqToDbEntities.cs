using LinqToDB.Mapping;

#pragma warning disable CA1707

namespace Inquiry.Benchmarks.LinqToDb;

[Table("Shippers")]
public sealed class L2Shipper
{
    [PrimaryKey, Identity, Column("ShipperID")]
    public int ShipperID { get; set; }

    [Column("CompanyName"), NotNull]
    public string CompanyName { get; set; } = "";

    [Column("Phone"), Nullable]
    public string? Phone { get; set; }
}

[Table("Customers")]
public sealed class L2Customer
{
    [PrimaryKey, Column("CustomerID"), NotNull]
    public string CustomerID { get; set; } = "";

    [Column("CompanyName"), NotNull]
    public string CompanyName { get; set; } = "";

    [Column("ContactName"), Nullable]
    public string? ContactName { get; set; }

    [Column("Country"), Nullable]
    public string? Country { get; set; }

    [Column("City"), Nullable]
    public string? City { get; set; }
}

[Table("Products")]
public sealed class L2Product
{
    [PrimaryKey, Identity, Column("ProductID")]
    public int ProductID { get; set; }

    [Column("ProductName"), NotNull]
    public string ProductName { get; set; } = "";

    [Column("CategoryID"), Nullable]
    public int? CategoryID { get; set; }

    [Column("UnitPrice"), Nullable]
    public decimal? UnitPrice { get; set; }

    [Column("UnitsInStock"), Nullable]
    public short? UnitsInStock { get; set; }

    [Column("Discontinued"), NotNull]
    public bool Discontinued { get; set; }
}

[Table("Region")]
public sealed class L2Region
{
    [PrimaryKey, Column("RegionID")]
    public int RegionID { get; set; }

    [Column("RegionDescription"), NotNull]
    public string RegionDescription { get; set; } = "";
}
