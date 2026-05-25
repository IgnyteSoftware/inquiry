using Inquiry.Northwind.Models;
using Inquiry.Northwind.Stores;

namespace Inquiry.Sample.Services;

/// <summary>
/// Populates the Northwind database with a small fixture so the dashboard has something to show.
/// Invoked once from <c>Program.cs</c> during startup; no-ops if customers already exist.
/// </summary>
public sealed class DataSeeder
{
    private readonly CustomerStore _customers;
    private readonly EmployeeStore _employees;
    private readonly CategoryStore _categories;
    private readonly ProductStore _products;
    private readonly ShipperStore _shippers;

    public DataSeeder(
        CustomerStore customers,
        EmployeeStore employees,
        CategoryStore categories,
        ProductStore products,
        ShipperStore shippers)
    {
        _customers = customers;
        _employees = employees;
        _categories = categories;
        _products = products;
        _shippers = shippers;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        if (await HasAnyAsync(_customers.SelectAllAsync(cancellationToken)).ConfigureAwait(false))
        {
            return;
        }

        // Customers
        foreach (var c in new[]
        {
            new Customer { CustomerID = "ALFKI", CompanyName = "Alfreds Futterkiste", ContactName = "Maria Anders",   Country = "Germany", City = "Berlin"  },
            new Customer { CustomerID = "BLAUS", CompanyName = "Blauer See Delikatessen", ContactName = "Hanna Moos", Country = "Germany", City = "Mannheim" },
            new Customer { CustomerID = "BONAP", CompanyName = "Bon app'", ContactName = "Laurence Lebihan",          Country = "France",  City = "Marseille" },
        })
        {
            await _customers.InsertAsync(c, cancellationToken).ConfigureAwait(false);
        }

        // Employees
        foreach (var e in new[]
        {
            new Employee { FirstName = "Nancy",    LastName = "Davolio",  Title = "Sales Representative", HireDate = new DateTime(1992, 5, 1)  },
            new Employee { FirstName = "Andrew",   LastName = "Fuller",   Title = "Vice President, Sales", HireDate = new DateTime(1992, 8, 14) },
            new Employee { FirstName = "Janet",    LastName = "Leverling", Title = "Sales Representative", HireDate = new DateTime(1992, 4, 1)  },
        })
        {
            await _employees.InsertAsync(e, cancellationToken).ConfigureAwait(false);
        }

        // Shippers
        foreach (var s in new[]
        {
            new Shipper { CompanyName = "Speedy Express", Phone = "(503) 555-9831" },
            new Shipper { CompanyName = "United Package", Phone = "(503) 555-3199" },
        })
        {
            await _shippers.InsertAsync(s, cancellationToken).ConfigureAwait(false);
        }

        // Categories — InsertReturning so we capture the IDENTITY-assigned CategoryID.
        var beverages  = await _categories.InsertReturningAsync(new Category { CategoryName = "Beverages",   Description = "Soft drinks, coffees, teas, beers, and ales" }, cancellationToken).ConfigureAwait(false);
        var condiments = await _categories.InsertReturningAsync(new Category { CategoryName = "Condiments",  Description = "Sweet and savory sauces, relishes, spreads, and seasonings" }, cancellationToken).ConfigureAwait(false);
        var produce    = await _categories.InsertReturningAsync(new Category { CategoryName = "Produce",     Description = "Dried fruit and bean curd" }, cancellationToken).ConfigureAwait(false);

        // Products
        foreach (var p in new[]
        {
            new Product { ProductName = "Chai",                  CategoryID = beverages?.CategoryID,  UnitPrice = 18m,    UnitsInStock = 39 },
            new Product { ProductName = "Chang",                 CategoryID = beverages?.CategoryID,  UnitPrice = 19m,    UnitsInStock = 17 },
            new Product { ProductName = "Aniseed Syrup",         CategoryID = condiments?.CategoryID, UnitPrice = 10m,    UnitsInStock = 13 },
            new Product { ProductName = "Chef Anton's Cajun Seasoning", CategoryID = condiments?.CategoryID, UnitPrice = 22m, UnitsInStock = 53 },
            new Product { ProductName = "Tofu",                  CategoryID = produce?.CategoryID,    UnitPrice = 23.25m, UnitsInStock = 35 },
        })
        {
            await _products.InsertAsync(p, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<bool> HasAnyAsync<T>(IAsyncEnumerable<T> source)
    {
        await foreach (var _ in source.ConfigureAwait(false))
        {
            return true;
        }
        return false;
    }
}
