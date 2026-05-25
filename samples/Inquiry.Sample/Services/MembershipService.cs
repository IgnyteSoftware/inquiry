using Inquiry.Sample.Models;
using Inquiry.Sample.Stores;

namespace Inquiry.Sample.Services;

/// <summary>
/// Manages the many-to-many join between organizations and users. Exposes view-models
/// rather than raw join rows so Blazor pages do not have to chase the foreign keys.
/// </summary>
public sealed class MembershipService
{
    private readonly OrganizationToUserStore _memberships;
    private readonly OrganizationStore _organizations;
    private readonly UserStore _users;

    public MembershipService(
        OrganizationToUserStore memberships,
        OrganizationStore organizations,
        UserStore users)
    {
        _memberships = memberships;
        _organizations = organizations;
        _users = users;
    }

    public record MembershipRow(OrganizationToUser Membership, Organization? Organization, User? User);

    public async Task<List<MembershipRow>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var memberships = new List<OrganizationToUser>();
        await foreach (var m in _memberships.SelectAllAsync(cancellationToken).ConfigureAwait(false))
        {
            memberships.Add(m);
        }

        var result = new List<MembershipRow>(memberships.Count);
        foreach (var m in memberships)
        {
            var org = await _organizations.SelectByKeyAsync(m.OrganizationKey, cancellationToken).ConfigureAwait(false);
            var user = await _users.SelectByKeyAsync(m.UserKey, cancellationToken).ConfigureAwait(false);
            result.Add(new MembershipRow(m, org, user));
        }
        return result;
    }

    public async Task<List<MembershipRow>> GetForOrganizationAsync(Guid organizationKey, CancellationToken cancellationToken = default)
    {
        var memberships = new List<OrganizationToUser>();
        await foreach (var m in _memberships.SelectByOrganizationAsync(organizationKey, cancellationToken).ConfigureAwait(false))
        {
            memberships.Add(m);
        }

        var org = await _organizations.SelectByKeyAsync(organizationKey, cancellationToken).ConfigureAwait(false);
        var rows = new List<MembershipRow>(memberships.Count);
        foreach (var m in memberships)
        {
            var user = await _users.SelectByKeyAsync(m.UserKey, cancellationToken).ConfigureAwait(false);
            rows.Add(new MembershipRow(m, org, user));
        }
        return rows;
    }

    public async Task<List<MembershipRow>> GetForUserAsync(Guid userKey, CancellationToken cancellationToken = default)
    {
        var memberships = new List<OrganizationToUser>();
        await foreach (var m in _memberships.SelectByUserAsync(userKey, cancellationToken).ConfigureAwait(false))
        {
            memberships.Add(m);
        }

        var user = await _users.SelectByKeyAsync(userKey, cancellationToken).ConfigureAwait(false);
        var rows = new List<MembershipRow>(memberships.Count);
        foreach (var m in memberships)
        {
            var org = await _organizations.SelectByKeyAsync(m.OrganizationKey, cancellationToken).ConfigureAwait(false);
            rows.Add(new MembershipRow(m, org, user));
        }
        return rows;
    }

    public Task<int> AddAsync(Guid organizationKey, Guid userKey, CancellationToken cancellationToken = default)
        => _memberships.InsertAsync(
            new OrganizationToUser
            {
                Key = Guid.NewGuid(),
                OrganizationKey = organizationKey,
                UserKey = userKey,
                IsActive = true,
            },
            cancellationToken);

    public Task<bool> SetActiveAsync(OrganizationToUser membership, bool isActive, CancellationToken cancellationToken = default)
    {
        membership.IsActive = isActive;
        return _memberships.UpdateAsync(membership, cancellationToken);
    }

    public Task<bool> RemoveAsync(Guid membershipKey, CancellationToken cancellationToken = default)
        => _memberships.DeleteByKeyAsync(membershipKey, cancellationToken);
}
