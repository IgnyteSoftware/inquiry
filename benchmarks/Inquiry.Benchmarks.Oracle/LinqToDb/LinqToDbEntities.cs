using LinqToDB.Mapping;

namespace Inquiry.Benchmarks.Oracle.LinqToDb;

[Table("SHIPPERS")]
public sealed class L2Shipper
{
    [PrimaryKey, Identity, Column("SHIPPERID")]
    public int ShipperID { get; set; }

    [Column("COMPANYNAME"), NotNull]
    public string CompanyName { get; set; } = "";

    [Column("PHONE"), Nullable]
    public string? Phone { get; set; }
}
