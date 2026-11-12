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

    [Column("Phone", SkipOnInsert = true, SkipOnUpdate = true), Nullable]
    public string? Phone { get; set; }
}

// The write legs set five columns, as the handwritten SQL does. The other columns are read only.
[Table("Customers")]
public sealed class L2Customer
{
    [PrimaryKey, Column("CustomerID"), NotNull]
    public string CustomerID { get; set; } = "";

    [Column("CompanyName"), NotNull]
    public string CompanyName { get; set; } = "";

    [Column("ContactName"), Nullable]
    public string? ContactName { get; set; }

    [Column("ContactTitle", SkipOnInsert = true, SkipOnUpdate = true), Nullable]
    public string? ContactTitle { get; set; }

    [Column("Address", SkipOnInsert = true, SkipOnUpdate = true), Nullable]
    public string? Address { get; set; }

    [Column("City"), Nullable]
    public string? City { get; set; }

    [Column("Region", SkipOnInsert = true, SkipOnUpdate = true), Nullable]
    public string? Region { get; set; }

    [Column("PostalCode", SkipOnInsert = true, SkipOnUpdate = true), Nullable]
    public string? PostalCode { get; set; }

    [Column("Country"), Nullable]
    public string? Country { get; set; }

    [Column("Phone", SkipOnInsert = true, SkipOnUpdate = true), Nullable]
    public string? Phone { get; set; }

    [Column("Fax", SkipOnInsert = true, SkipOnUpdate = true), Nullable]
    public string? Fax { get; set; }
}

[Table("Products")]
public sealed class L2Product
{
    [PrimaryKey, Identity, Column("ProductID")]
    public int ProductID { get; set; }

    [Column("ProductName"), NotNull]
    public string ProductName { get; set; } = "";

    [Column("SupplierID", SkipOnInsert = true, SkipOnUpdate = true), Nullable]
    public int? SupplierID { get; set; }

    [Column("CategoryID"), Nullable]
    public int? CategoryID { get; set; }

    [Column("QuantityPerUnit", SkipOnInsert = true, SkipOnUpdate = true), Nullable]
    public string? QuantityPerUnit { get; set; }

    [Column("UnitPrice"), Nullable]
    public decimal? UnitPrice { get; set; }

    [Column("UnitsInStock"), Nullable]
    public short? UnitsInStock { get; set; }

    [Column("UnitsOnOrder", SkipOnInsert = true, SkipOnUpdate = true), Nullable]
    public short? UnitsOnOrder { get; set; }

    [Column("ReorderLevel", SkipOnInsert = true, SkipOnUpdate = true), Nullable]
    public short? ReorderLevel { get; set; }

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
