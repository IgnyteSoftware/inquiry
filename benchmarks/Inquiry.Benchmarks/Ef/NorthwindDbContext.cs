using Microsoft.EntityFrameworkCore;

namespace Inquiry.Benchmarks.Ef;

/// <summary>
/// EF Core model mapped onto the same Northwind tables the Inquiry benchmarks use. Only the three
/// benchmarked entities are mapped. Each maps every column that the ADO.NET and Inquiry legs read, so
/// every leg does the same work per row.
/// </summary>
/// <remarks>
/// The EF entities are dedicated types — not the Inquiry POCOs — so EF's conventions can
/// run without colliding with the Inquiry attributes / navigation properties.
/// </remarks>
public sealed class NorthwindDbContext : DbContext
{
    public NorthwindDbContext(DbContextOptions<NorthwindDbContext> options) : base(options) { }

    public DbSet<EfCustomer> Customers => Set<EfCustomer>();
    public DbSet<EfProduct> Products => Set<EfProduct>();
    public DbSet<EfShipper> Shippers => Set<EfShipper>();

    // The write legs set five columns, as the handwritten SQL does. These columns are read only, so
    // EF Core leaves them out of INSERT and UPDATE.
    private static void ReadOnly<T>(Microsoft.EntityFrameworkCore.Metadata.Builders.PropertyBuilder<T> property)
    {
        property.Metadata.SetBeforeSaveBehavior(Microsoft.EntityFrameworkCore.Metadata.PropertySaveBehavior.Ignore);
        property.Metadata.SetAfterSaveBehavior(Microsoft.EntityFrameworkCore.Metadata.PropertySaveBehavior.Ignore);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EfCustomer>(e =>
        {
            e.ToTable("Customers");
            e.HasKey(x => x.CustomerID);
            e.Property(x => x.CustomerID).HasColumnName("CustomerID").ValueGeneratedNever();
            e.Property(x => x.CompanyName).HasColumnName("CompanyName");
            e.Property(x => x.ContactName).HasColumnName("ContactName");
            ReadOnly(e.Property(x => x.ContactTitle).HasColumnName("ContactTitle"));
            ReadOnly(e.Property(x => x.Address).HasColumnName("Address"));
            e.Property(x => x.City).HasColumnName("City");
            ReadOnly(e.Property(x => x.Region).HasColumnName("Region"));
            ReadOnly(e.Property(x => x.PostalCode).HasColumnName("PostalCode"));
            e.Property(x => x.Country).HasColumnName("Country");
            ReadOnly(e.Property(x => x.Phone).HasColumnName("Phone"));
            ReadOnly(e.Property(x => x.Fax).HasColumnName("Fax"));
        });

        modelBuilder.Entity<EfProduct>(e =>
        {
            e.ToTable("Products");
            e.HasKey(x => x.ProductID);
            e.Property(x => x.ProductID).HasColumnName("ProductID").ValueGeneratedOnAdd();
            e.Property(x => x.ProductName).HasColumnName("ProductName");
            ReadOnly(e.Property(x => x.SupplierID).HasColumnName("SupplierID"));
            e.Property(x => x.CategoryID).HasColumnName("CategoryID");
            ReadOnly(e.Property(x => x.QuantityPerUnit).HasColumnName("QuantityPerUnit"));
            e.Property(x => x.UnitPrice).HasColumnName("UnitPrice");
            e.Property(x => x.UnitsInStock).HasColumnName("UnitsInStock");
            ReadOnly(e.Property(x => x.UnitsOnOrder).HasColumnName("UnitsOnOrder"));
            ReadOnly(e.Property(x => x.ReorderLevel).HasColumnName("ReorderLevel"));
            e.Property(x => x.Discontinued).HasColumnName("Discontinued");
        });

        modelBuilder.Entity<EfShipper>(e =>
        {
            e.ToTable("Shippers");
            e.HasKey(x => x.ShipperID);
            e.Property(x => x.ShipperID).HasColumnName("ShipperID").ValueGeneratedOnAdd();
            e.Property(x => x.CompanyName).HasColumnName("CompanyName");
            ReadOnly(e.Property(x => x.Phone).HasColumnName("Phone"));
        });
    }
}

public sealed class EfCustomer
{
    public string CustomerID { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string? ContactName { get; set; }
    public string? ContactTitle { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? Region { get; set; }
    public string? PostalCode { get; set; }
    public string? Country { get; set; }
    public string? Phone { get; set; }
    public string? Fax { get; set; }
}

public sealed class EfProduct
{
    public int ProductID { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public int? SupplierID { get; set; }
    public int? CategoryID { get; set; }
    public string? QuantityPerUnit { get; set; }
    public decimal? UnitPrice { get; set; }
    public short? UnitsInStock { get; set; }
    public short? UnitsOnOrder { get; set; }
    public short? ReorderLevel { get; set; }
    public bool Discontinued { get; set; }
}

public sealed class EfShipper
{
    public int ShipperID { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public string? Phone { get; set; }
}
