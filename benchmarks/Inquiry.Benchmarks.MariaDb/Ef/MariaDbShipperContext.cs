using Microsoft.EntityFrameworkCore;

namespace Inquiry.Benchmarks.MariaDb.Ef;

public sealed class MariaDbShipperContext : DbContext
{
    public MariaDbShipperContext(DbContextOptions<MariaDbShipperContext> options) : base(options) { }

    public DbSet<MariaDbEfShipper> Shippers => Set<MariaDbEfShipper>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MariaDbEfShipper>(e =>
        {
            e.ToTable("Shippers");
            e.HasKey(x => x.ShipperID);
            e.Property(x => x.ShipperID).HasColumnName("ShipperID").ValueGeneratedOnAdd();
            e.Property(x => x.CompanyName).HasColumnName("CompanyName");
            e.Property(x => x.Phone).HasColumnName("Phone");
        });
    }
}

public sealed class MariaDbEfShipper
{
    public int ShipperID { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public string? Phone { get; set; }
}
