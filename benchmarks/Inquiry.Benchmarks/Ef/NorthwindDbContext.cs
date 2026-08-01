using Microsoft.EntityFrameworkCore;

namespace Inquiry.Benchmarks.Ef;

/// <summary>
/// Minimal EF Core model mapped onto the same Northwind tables the Inquiry benchmarks use.
/// Only the three benchmarked entities are mapped; everything else in the schema is left
/// to EF to ignore.
/// </summary>
/// <remarks>
/// The EF entities are dedicated types — not the Inquiry POCOs — so EF's conventions can
/// run without colliding with the Inquiry attributes / navigation properties.
/// </remarks>
public sealed class NorthwindDbContext : DbContext
{
    public NorthwindDbContext(DbContextOptions<NorthwindDbContext> options) : base(options) { }

    public DbSet<EfCustomer> Customers => Set<EfCustomer>();
    public DbSet<EfProduct>  Products  => Set<EfProduct>();
    public DbSet<EfShipper>  Shippers  => Set<EfShipper>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EfCustomer>(e =>
        {
            e.ToTable("Customers");
            e.HasKey(x => x.CustomerID);
            e.Property(x => x.CustomerID).HasColumnName("CustomerID").ValueGeneratedNever();
            e.Property(x => x.CompanyName).HasColumnName("CompanyName");
            e.Property(x => x.ContactName).HasColumnName("ContactName");
            e.Property(x => x.Country).HasColumnName("Country");
            e.Property(x => x.City).HasColumnName("City");
        });

        modelBuilder.Entity<EfProduct>(e =>
        {
            e.ToTable("Products");
            e.HasKey(x => x.ProductID);
            e.Property(x => x.ProductID).HasColumnName("ProductID").ValueGeneratedOnAdd();
            e.Property(x => x.ProductName).HasColumnName("ProductName");
            e.Property(x => x.CategoryID).HasColumnName("CategoryID");
            e.Property(x => x.UnitPrice).HasColumnName("UnitPrice");
            e.Property(x => x.UnitsInStock).HasColumnName("UnitsInStock");
            e.Property(x => x.Discontinued).HasColumnName("Discontinued");
        });

        modelBuilder.Entity<EfShipper>(e =>
        {
            e.ToTable("Shippers");
            e.HasKey(x => x.ShipperID);
            e.Property(x => x.ShipperID).HasColumnName("ShipperID").ValueGeneratedOnAdd();
            e.Property(x => x.CompanyName).HasColumnName("CompanyName");
            e.Property(x => x.Phone).HasColumnName("Phone");
        });
    }
}

public sealed class EfCustomer
{
    public string CustomerID { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string? ContactName { get; set; }
    public string? Country { get; set; }
    public string? City { get; set; }
}

public sealed class EfProduct
{
    public int ProductID { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public int? CategoryID { get; set; }
    public decimal? UnitPrice { get; set; }
    public short? UnitsInStock { get; set; }
    public bool Discontinued { get; set; }
}

public sealed class EfShipper
{
    public int ShipperID { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public string? Phone { get; set; }
}
