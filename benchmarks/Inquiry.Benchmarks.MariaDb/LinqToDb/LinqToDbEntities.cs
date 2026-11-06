using LinqToDB.Mapping;

namespace Inquiry.Benchmarks.MariaDb.LinqToDb;

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
