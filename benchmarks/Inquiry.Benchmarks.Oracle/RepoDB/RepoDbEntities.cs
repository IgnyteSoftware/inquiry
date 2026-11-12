using RepoDb.Attributes;

namespace Inquiry.Benchmarks.Oracle.RepoDB;

[Map("SHIPPERS")]
public sealed class RdShipper
{
    [Primary, Identity, Map("SHIPPERID")]
    public int ShipperID { get; set; }

    [Map("COMPANYNAME")]
    public string CompanyName { get; set; } = "";

    [Map("PHONE")]
    public string? Phone { get; set; }
}
