using Microsoft.EntityFrameworkCore;

namespace Inquiry.Benchmarks.SqlServer.Ef;

/// <summary>EF Core model for the Product/Category read-extra benchmarks (Count, OffsetPage, Search).</summary>
public sealed class SqlServerProductContext : DbContext
{
    public SqlServerProductContext(DbContextOptions<SqlServerProductContext> options) : base(options) { }

    public DbSet<SqlServerEfProduct> Products => Set<SqlServerEfProduct>();
    public DbSet<SqlServerEfCategory> Categories => Set<SqlServerEfCategory>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SqlServerEfProduct>(e =>
        {
            e.ToTable("Products");
            e.HasKey(x => x.ProductID);
            e.Property(x => x.ProductID).HasColumnName("ProductID").ValueGeneratedOnAdd();
            e.Property(x => x.ProductName).HasColumnName("ProductName");
            e.Property(x => x.CategoryID).HasColumnName("CategoryID");
            e.Property(x => x.UnitPrice).HasColumnName("UnitPrice");
            e.Property(x => x.Discontinued).HasColumnName("Discontinued");
        });

        modelBuilder.Entity<SqlServerEfCategory>(e =>
        {
            e.ToTable("Categories");
            e.HasKey(x => x.CategoryID);
            e.Property(x => x.CategoryID).HasColumnName("CategoryID").ValueGeneratedOnAdd();
            e.Property(x => x.CategoryName).HasColumnName("CategoryName");
            e.Property(x => x.Description).HasColumnName("Description");
        });
    }
}

public sealed class SqlServerEfProduct
{
    public int ProductID { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public int? CategoryID { get; set; }
    public decimal? UnitPrice { get; set; }
    public bool Discontinued { get; set; }
}

public sealed class SqlServerEfCategory
{
    public int CategoryID { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public string? Description { get; set; }
}
