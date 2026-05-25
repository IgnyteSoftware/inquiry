using Inquiry.Sample.Models;
using Inquiry.Sample.Stores;

namespace Inquiry.Sample.Services;

/// <summary>
/// Populates the SQLite database with a small fixture so the dashboard has something to show.
/// Invoked once from <c>Program.cs</c> during startup; no-ops if data already exists.
/// </summary>
public sealed class DataSeeder
{
    private readonly OrganizationStore _organizations;
    private readonly UserStore _users;
    private readonly OrganizationToUserStore _memberships;
    private readonly CategoryStore _categories;
    private readonly ProductStore _products;

    public DataSeeder(
        OrganizationStore organizations,
        UserStore users,
        OrganizationToUserStore memberships,
        CategoryStore categories,
        ProductStore products)
    {
        _organizations = organizations;
        _users = users;
        _memberships = memberships;
        _categories = categories;
        _products = products;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        if (await HasAnyAsync(_organizations.SelectAllAsync(cancellationToken)).ConfigureAwait(false))
        {
            return;
        }

        // Organizations
        var acme = new Organization { Key = Guid.NewGuid(), Name = "Acme Research", IsActive = true };
        var globex = new Organization { Key = Guid.NewGuid(), Name = "Globex Industries", IsActive = true };
        await _organizations.InsertAsync(acme, cancellationToken).ConfigureAwait(false);
        await _organizations.InsertAsync(globex, cancellationToken).ConfigureAwait(false);

        // Users
        var alice = new User { Key = Guid.NewGuid(), FirstName = "Alice", LastName = "Anders", Email = "alice@example.com" };
        var bob = new User { Key = Guid.NewGuid(), FirstName = "Bob", LastName = "Brown", Email = "bob@example.com" };
        var carol = new User { Key = Guid.NewGuid(), FirstName = "Carol", LastName = "Carter", Email = "carol@example.com" };
        foreach (var u in new[] { alice, bob, carol })
        {
            await _users.InsertAsync(u, cancellationToken).ConfigureAwait(false);
        }

        // Memberships
        foreach (var m in new[]
        {
            new OrganizationToUser { Key = Guid.NewGuid(), OrganizationKey = acme.Key,   UserKey = alice.Key, IsActive = true },
            new OrganizationToUser { Key = Guid.NewGuid(), OrganizationKey = acme.Key,   UserKey = bob.Key,   IsActive = true },
            new OrganizationToUser { Key = Guid.NewGuid(), OrganizationKey = globex.Key, UserKey = alice.Key, IsActive = true },
            new OrganizationToUser { Key = Guid.NewGuid(), OrganizationKey = globex.Key, UserKey = carol.Key, IsActive = true },
        })
        {
            await _memberships.InsertAsync(m, cancellationToken).ConfigureAwait(false);
        }

        // Categories
        var electronics = new Category { Key = Guid.NewGuid(), Name = "Electronics" };
        var clothing = new Category { Key = Guid.NewGuid(), Name = "Clothing" };
        await _categories.InsertAsync(electronics, cancellationToken).ConfigureAwait(false);
        await _categories.InsertAsync(clothing, cancellationToken).ConfigureAwait(false);

        // Products
        foreach (var p in new[]
        {
            new Product { Key = Guid.NewGuid(), Name = "Laptop",     Price = 999.99m, CategoryKey = electronics.Key },
            new Product { Key = Guid.NewGuid(), Name = "Phone",      Price = 699.99m, CategoryKey = electronics.Key },
            new Product { Key = Guid.NewGuid(), Name = "Headphones", Price = 149.99m, CategoryKey = electronics.Key },
            new Product { Key = Guid.NewGuid(), Name = "T-Shirt",    Price =  29.99m, CategoryKey = clothing.Key },
            new Product { Key = Guid.NewGuid(), Name = "Jeans",      Price =  59.99m, CategoryKey = clothing.Key },
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
