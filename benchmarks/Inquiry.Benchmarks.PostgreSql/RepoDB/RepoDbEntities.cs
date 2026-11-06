using RepoDb.Attributes;

namespace Inquiry.Benchmarks.PostgreSql.RepoDB;

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
