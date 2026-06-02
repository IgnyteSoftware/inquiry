using Microsoft.EntityFrameworkCore;

namespace Inquiry.Benchmarks.MySql.Ef;

/// <summary>
/// EF Core model for the MySQL Shipper benchmark. Mapped to the same backtick-quoted mixed-case
/// identifiers that <c>NorthwindSchema.MySqlDdl</c> creates — Pomelo's MySQL provider emits
/// backtick-quoted identifiers, so the tables must be created the same way or the generated SQL
/// will fail to resolve them.
/// Reuses the <see cref="MySqlEfShipper"/> POCO to keep this project self-contained.
/// </summary>
public sealed class MySqlShipperContext : DbContext
{
    public MySqlShipperContext(DbContextOptions<MySqlShipperContext> options) : base(options) { }

    public DbSet<MySqlEfShipper> Shippers => Set<MySqlEfShipper>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MySqlEfShipper>(e =>
        {
            e.ToTable("Shippers");
            e.HasKey(x => x.ShipperID);
            e.Property(x => x.ShipperID).HasColumnName("ShipperID").ValueGeneratedOnAdd();
            e.Property(x => x.CompanyName).HasColumnName("CompanyName");
            e.Property(x => x.Phone).HasColumnName("Phone");
        });
    }
}

public sealed class MySqlEfShipper
{
    public int ShipperID { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public string? Phone { get; set; }
}
