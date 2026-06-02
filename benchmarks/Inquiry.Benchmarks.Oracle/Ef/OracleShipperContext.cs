using Microsoft.EntityFrameworkCore;

namespace Inquiry.Benchmarks.Oracle.Ef;

/// <summary>
/// EF Core model for the Oracle Shipper benchmark. Mapped to the unquoted identifiers that
/// <c>NorthwindSchema.OracleDdl</c> creates — Oracle folds unquoted DDL to uppercase, so EF's
/// column names must match the stored (uppercase) names. Uses <see cref="OracleEfShipper"/> as
/// the POCO to keep this project self-contained.
/// </summary>
public sealed class OracleShipperContext : DbContext
{
    public OracleShipperContext(DbContextOptions<OracleShipperContext> options) : base(options) { }

    public DbSet<OracleEfShipper> Shippers => Set<OracleEfShipper>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OracleEfShipper>(e =>
        {
            // OracleDdl creates table SHIPPERS (unquoted → stored uppercase).
            e.ToTable("SHIPPERS");
            e.HasKey(x => x.ShipperID);
            e.Property(x => x.ShipperID).HasColumnName("SHIPPERID").ValueGeneratedOnAdd();
            e.Property(x => x.CompanyName).HasColumnName("COMPANYNAME");
            e.Property(x => x.Phone).HasColumnName("PHONE");
        });
    }
}

public sealed class OracleEfShipper
{
    public int ShipperID { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public string? Phone { get; set; }
}
