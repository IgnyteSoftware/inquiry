using Inquiry.Sample.Models;
using Inquiry.Sample.Stores;

namespace Inquiry.Sample.Workflows;

public sealed class OrganizationWorkflow
{
    private readonly OrganizationStore _organizations;

    public OrganizationWorkflow(OrganizationStore organizations)
    {
        _organizations = organizations;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var organization = new Organization
        {
            Key = Guid.NewGuid(),
            Name = "Acme Research",
            IsActive = true,
        };

        await _organizations.InsertAsync(organization, cancellationToken);

        var selected = await _organizations.SelectByKeyAsync(organization.Key, cancellationToken);
        Console.WriteLine($"Inserted: {selected?.Name} ({selected?.Key})");

        organization.Name = "Acme Research Group";
        organization.IsActive = false;
        await _organizations.UpdateAsync(organization, cancellationToken);

        Console.WriteLine("Active organizations:");
        await foreach (var activeOrganization in _organizations.SelectByIsActiveAsync(true, cancellationToken))
        {
            Console.WriteLine($"- {activeOrganization.Name}");
        }

        var updated = await _organizations.SelectByKeyAsync(organization.Key, cancellationToken);
        Console.WriteLine($"Updated: {updated?.Name}, active={updated?.IsActive}");

        Console.WriteLine("All organizations through IInquiry:");
        await foreach (var queriedOrganization in _organizations.SelectAllCustomAsync(cancellationToken))
        {
            Console.WriteLine($"- {queriedOrganization.Name}");
        }

        await _organizations.DeleteByKeyAsync(organization.Key, cancellationToken);
        Console.WriteLine("Deleted sample organization.");
    }
}
