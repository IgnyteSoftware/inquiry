using Microsoft.EntityFrameworkCore;

namespace Inquiry.Benchmarks.Ef;

/// <summary>
/// EF Core model for the cross-dialect <c>shippers</c> benchmark. Everything is mapped to all-lowercase
/// identifiers so one physical table is addressable identically by EF Core (which quotes identifiers),
/// Inquiry/Dapper/ADO (unquoted, portable SQL), PostgreSQL (folds unquoted names to lowercase), MySQL
/// (case-sensitive table names on Linux), and SQL Server (case-insensitive). Reuses the
/// <see cref="EfShipper"/> POCO from <see cref="NorthwindDbContext"/>.
/// </summary>
public sealed class CrossDialectShipperContext : DbContext
{
    public CrossDialectShipperContext(DbContextOptions<CrossDialectShipperContext> options) : base(options) { }

    public DbSet<EfShipper> Shippers => Set<EfShipper>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EfShipper>(e =>
        {
            e.ToTable("shippers");
            e.HasKey(x => x.ShipperID);
            e.Property(x => x.ShipperID).HasColumnName("shipperid").ValueGeneratedOnAdd();
            e.Property(x => x.CompanyName).HasColumnName("companyname");
            e.Property(x => x.Phone).HasColumnName("phone");
        });
    }
}
