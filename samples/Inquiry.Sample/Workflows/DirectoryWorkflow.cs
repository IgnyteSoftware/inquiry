using Inquiry.Sample.Models;
using Inquiry.Sample.Stores;

namespace Inquiry.Sample.Workflows;

/// <summary>
/// Demonstrates an Inquiry-mapped directory model: organizations, users, and a join table
/// (TOrganizationToUser) with two foreign keys. Navigates relationships through generated
/// SelectAllByField queries on the foreign-key columns.
/// </summary>
public sealed class DirectoryWorkflow
{
    private readonly OrganizationStore _organizations;
    private readonly UserStore _users;
    private readonly OrganizationToUserStore _memberships;

    public DirectoryWorkflow(
        OrganizationStore organizations,
        UserStore users,
        OrganizationToUserStore memberships)
    {
        _organizations = organizations;
        _users = users;
        _memberships = memberships;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var (acme, globex) = await SeedOrganizationsAsync(cancellationToken).ConfigureAwait(false);
        var (alice, bob, carol) = await SeedUsersAsync(cancellationToken).ConfigureAwait(false);
        await SeedMembershipsAsync(acme, globex, alice, bob, carol, cancellationToken).ConfigureAwait(false);

        await PrintMembersOfAsync(acme, cancellationToken).ConfigureAwait(false);
        await PrintMembersOfAsync(globex, cancellationToken).ConfigureAwait(false);
        await PrintOrganizationsForAsync(alice, cancellationToken).ConfigureAwait(false);

        await DeactivateMembershipAsync(globex, alice, cancellationToken).ConfigureAwait(false);
        await PrintOrganizationsForAsync(alice, cancellationToken).ConfigureAwait(false);

        await PrintUserLookupAsync("carol@example.com", cancellationToken).ConfigureAwait(false);
    }

    private async Task<(Organization Acme, Organization Globex)> SeedOrganizationsAsync(CancellationToken cancellationToken)
    {
        var acme = new Organization { Key = Guid.NewGuid(), Name = "Acme Research", IsActive = true };
        var globex = new Organization { Key = Guid.NewGuid(), Name = "Globex Industries", IsActive = true };
        await _organizations.InsertAsync(acme, cancellationToken).ConfigureAwait(false);
        await _organizations.InsertAsync(globex, cancellationToken).ConfigureAwait(false);
        Console.WriteLine($"Seeded organizations: {acme.Name}, {globex.Name}");
        return (acme, globex);
    }

    private async Task<(User Alice, User Bob, User Carol)> SeedUsersAsync(CancellationToken cancellationToken)
    {
        var alice = new User { Key = Guid.NewGuid(), FirstName = "Alice", LastName = "Anders", Email = "alice@example.com" };
        var bob = new User { Key = Guid.NewGuid(), FirstName = "Bob", LastName = "Brown", Email = "bob@example.com" };
        var carol = new User { Key = Guid.NewGuid(), FirstName = "Carol", LastName = "Carter", Email = "carol@example.com" };
        await _users.InsertAsync(alice, cancellationToken).ConfigureAwait(false);
        await _users.InsertAsync(bob, cancellationToken).ConfigureAwait(false);
        await _users.InsertAsync(carol, cancellationToken).ConfigureAwait(false);
        Console.WriteLine($"Seeded users: {alice.Email}, {bob.Email}, {carol.Email}");
        return (alice, bob, carol);
    }

    private async Task SeedMembershipsAsync(
        Organization acme,
        Organization globex,
        User alice,
        User bob,
        User carol,
        CancellationToken cancellationToken)
    {
        await InsertMembershipAsync(acme, alice, cancellationToken).ConfigureAwait(false);
        await InsertMembershipAsync(acme, bob, cancellationToken).ConfigureAwait(false);
        await InsertMembershipAsync(globex, alice, cancellationToken).ConfigureAwait(false);
        await InsertMembershipAsync(globex, carol, cancellationToken).ConfigureAwait(false);
        Console.WriteLine("Seeded memberships.");
    }

    private Task<int> InsertMembershipAsync(Organization organization, User user, CancellationToken cancellationToken)
    {
        return _memberships.InsertAsync(
            new OrganizationToUser
            {
                Key = Guid.NewGuid(),
                OrganizationKey = organization.Key,
                UserKey = user.Key,
                IsActive = true,
            },
            cancellationToken);
    }

    private async Task PrintMembersOfAsync(Organization organization, CancellationToken cancellationToken)
    {
        Console.WriteLine($"Members of {organization.Name}:");
        await foreach (var membership in _memberships.SelectByOrganizationAsync(organization.Key, cancellationToken).ConfigureAwait(false))
        {
            var user = await _users.SelectByKeyAsync(membership.UserKey, cancellationToken).ConfigureAwait(false);
            if (user is null)
            {
                continue;
            }

            var status = membership.IsActive ? "active" : "inactive";
            Console.WriteLine($"  - {user.FirstName} {user.LastName} <{user.Email}> ({status})");
        }
    }

    private async Task PrintOrganizationsForAsync(User user, CancellationToken cancellationToken)
    {
        Console.WriteLine($"Organizations for {user.FirstName} {user.LastName}:");
        await foreach (var membership in _memberships.SelectByUserAsync(user.Key, cancellationToken).ConfigureAwait(false))
        {
            var organization = await _organizations.SelectByKeyAsync(membership.OrganizationKey, cancellationToken).ConfigureAwait(false);
            if (organization is null)
            {
                continue;
            }

            var status = membership.IsActive ? "active" : "inactive";
            Console.WriteLine($"  - {organization.Name} ({status})");
        }
    }

    private async Task DeactivateMembershipAsync(Organization organization, User user, CancellationToken cancellationToken)
    {
        // Buffer the query results before issuing the UPDATE: each store call opens its own
        // connection, and SQLite blocks writes while a reader still holds the file.
        var memberships = new List<OrganizationToUser>();
        await foreach (var membership in _memberships.SelectByOrganizationAsync(organization.Key, cancellationToken).ConfigureAwait(false))
        {
            memberships.Add(membership);
        }

        foreach (var membership in memberships)
        {
            if (membership.UserKey != user.Key)
            {
                continue;
            }

            membership.IsActive = false;
            await _memberships.UpdateAsync(membership, cancellationToken).ConfigureAwait(false);
            Console.WriteLine($"Deactivated {user.FirstName}'s membership in {organization.Name}.");
            return;
        }
    }

    private async Task PrintUserLookupAsync(string email, CancellationToken cancellationToken)
    {
        Console.WriteLine($"Lookup by email '{email}':");
        await foreach (var user in _users.SelectByEmailAsync(email, cancellationToken).ConfigureAwait(false))
        {
            Console.WriteLine($"  - {user.FirstName} {user.LastName} (Key={user.Key:N})");
        }
    }
}
