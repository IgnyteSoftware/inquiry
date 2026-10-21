using Microsoft.EntityFrameworkCore;

namespace Inquiry.Benchmarks.PostgreSql.Ef;

/// <summary>
/// EF Core model for the PostgreSQL Shipper benchmark. Mapped to the same quoted mixed-case
/// identifiers that <c>NorthwindSchema.PostgreSqlDdl</c> creates — PostgreSQL preserves
/// case for identifiers that were created with double-quotes, so EF's quoted output must match.
/// Reuses the <see cref="PgEfShipper"/> POCO to keep this project self-contained.
/// </summary>
public sealed class PgShipperContext : DbContext
{
    public PgShipperContext(DbContextOptions<PgShipperContext> options) : base(options) { }

    public DbSet<PgEfShipper> Shippers => Set<PgEfShipper>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PgEfShipper>(e =>
        {
            e.ToTable("Shippers");
            e.HasKey(x => x.ShipperID);
            e.Property(x => x.ShipperID).HasColumnName("ShipperID").ValueGeneratedOnAdd();
            e.Property(x => x.CompanyName).HasColumnName("CompanyName");
            e.Property(x => x.Phone).HasColumnName("Phone");
        });
    }
}

public sealed class PgEfShipper
{
    public int ShipperID { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public string? Phone { get; set; }
}
