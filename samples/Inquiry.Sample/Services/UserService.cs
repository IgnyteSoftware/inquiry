using Inquiry.Sample.Models;
using Inquiry.Sample.Stores;

namespace Inquiry.Sample.Services;

/// <summary>
/// CRUD + lookup-by-email operations for users.
/// </summary>
public sealed class UserService
{
    private readonly UserStore _store;

    public UserService(UserStore store)
    {
        _store = store;
    }

    public async Task<List<User>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var list = new List<User>();
        await foreach (var item in _store.SelectAllAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(item);
        }
        return list;
    }

    public Task<User?> GetByKeyAsync(Guid key, CancellationToken cancellationToken = default)
        => _store.SelectByKeyAsync(key, cancellationToken);

    public async Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        await foreach (var item in _store.SelectByEmailAsync(email, cancellationToken).ConfigureAwait(false))
        {
            return item;
        }
        return null;
    }

    public Task<int> CreateAsync(User user, CancellationToken cancellationToken = default)
        => _store.InsertAsync(user, cancellationToken);

    public Task<bool> UpdateAsync(User user, CancellationToken cancellationToken = default)
        => _store.UpdateAsync(user, cancellationToken);

    public Task<bool> DeleteAsync(Guid key, CancellationToken cancellationToken = default)
        => _store.DeleteByKeyAsync(key, cancellationToken);
}
