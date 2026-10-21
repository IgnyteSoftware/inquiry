using Microsoft.EntityFrameworkCore;

namespace Inquiry.Benchmarks.SqlServer.Ef;

/// <summary>
/// EF Core model for the SQL Server Shipper benchmark. Mapped to the unquoted identifiers
/// that <c>NorthwindSchema.SqlServerDdl</c> creates — SQL Server is case-insensitive by
/// default, so EF's default quoted output resolves correctly against unquoted DDL names.
/// Reuses the <see cref="SqlServerEfShipper"/> POCO to keep this project self-contained.
/// </summary>
public sealed class SqlServerShipperContext : DbContext
{
    public SqlServerShipperContext(DbContextOptions<SqlServerShipperContext> options) : base(options) { }

    public DbSet<SqlServerEfShipper> Shippers => Set<SqlServerEfShipper>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SqlServerEfShipper>(e =>
        {
            e.ToTable("Shippers");
            e.HasKey(x => x.ShipperID);
            e.Property(x => x.ShipperID).HasColumnName("ShipperID").ValueGeneratedOnAdd();
            e.Property(x => x.CompanyName).HasColumnName("CompanyName");
            e.Property(x => x.Phone).HasColumnName("Phone");
        });
    }
}

public sealed class SqlServerEfShipper
{
    public int ShipperID { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public string? Phone { get; set; }
}
