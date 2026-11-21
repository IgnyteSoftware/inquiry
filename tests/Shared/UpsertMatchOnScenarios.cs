using Inquiry.Entities;
using Inquiry.Stores;

namespace Inquiry.Tests.UpsertMatchOn;

[InquiryTable("MatchPref")]
[InquiryIndex(nameof(MemberKey), nameof(TypeCode), IsUnique = true)]
public sealed class MatchPref
{
    [InquiryKey(IsGenerated = true)]
    public int Id { get; set; }

    [InquiryColumn]
    public int MemberKey { get; set; }

    [InquiryColumn(Length = 20)]
    public string TypeCode { get; set; } = string.Empty;

    [InquiryColumn]
    public int Weight { get; set; }
}

public partial class MatchPrefStore : InquiryStore<MatchPref>
{
    [InquiryUpsert(MatchOn = new[] { nameof(MatchPref.MemberKey), nameof(MatchPref.TypeCode) })]
    public partial Task<int> UpsertAsync(MatchPref pref, CancellationToken cancellationToken = default);

    [InquiryUpsert(MatchOn = new[] { nameof(MatchPref.MemberKey), nameof(MatchPref.TypeCode) })]
    public partial Task<MatchPref?> UpsertReturningAsync(MatchPref pref, CancellationToken cancellationToken = default);

    [InquirySelectAll(OrderBy = "Id ASC")]
    public partial Task<IReadOnlyList<MatchPref>> SelectAllAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Shared live checks for #614. An upsert matched on a unique natural key inserts a new row, updates
/// that row on the next call, and keeps the key the row already has.
/// </summary>
internal static class UpsertMatchOnScenarios
{
    internal static async Task InsertThenUpdateKeepsGeneratedKeyAsync(MatchPrefStore store)
    {
        var inserted = await store.UpsertReturningAsync(new MatchPref { MemberKey = 7, TypeCode = "email", Weight = 1 });
        Assert.NotNull(inserted);
        Assert.True(inserted.Id > 0);
        Assert.Equal(1, inserted.Weight);

        var otherType = await store.UpsertReturningAsync(new MatchPref { MemberKey = 7, TypeCode = "sms", Weight = 5 });
        Assert.NotNull(otherType);
        Assert.NotEqual(inserted.Id, otherType.Id);

        // The caller's key value plays no part. The update branch keeps the generated key.
        var updated = await store.UpsertReturningAsync(new MatchPref { Id = otherType.Id + 100, MemberKey = 7, TypeCode = "email", Weight = 2 });
        Assert.NotNull(updated);
        Assert.Equal(inserted.Id, updated.Id);
        Assert.Equal(2, updated.Weight);

        await store.UpsertAsync(new MatchPref { MemberKey = 7, TypeCode = "email", Weight = 3 });

        var rows = await store.SelectAllAsync();
        Assert.Equal(2, rows.Count);
        var email = Assert.Single(rows, static r => r.TypeCode == "email");
        Assert.Equal(inserted.Id, email.Id);
        Assert.Equal(3, email.Weight);
        Assert.Equal(5, Assert.Single(rows, static r => r.TypeCode == "sms").Weight);
    }

    internal static async Task NonReturningUpsertInsertsThenUpdatesAsync(MatchPrefStore store)
    {
        await store.UpsertAsync(new MatchPref { MemberKey = 3, TypeCode = "push", Weight = 1 });
        var first = Assert.Single(await store.SelectAllAsync());

        await store.UpsertAsync(new MatchPref { MemberKey = 3, TypeCode = "push", Weight = 9 });
        var second = Assert.Single(await store.SelectAllAsync());

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(9, second.Weight);
    }

    /// <summary>
    /// Runs parallel upserts of one natural key and returns each call's failure. The table must end with
    /// one row that holds one of the inputs.
    /// </summary>
    internal static async Task<IReadOnlyList<Exception>> ParallelUpsertsOfOneNaturalKeyAsync(MatchPrefStore store, int parallelism = 10)
    {
        var inputs = Enumerable.Range(1, parallelism)
            .Select(i => new MatchPref { MemberKey = 11, TypeCode = "push", Weight = i })
            .ToArray();

        var outcomes = await Task.WhenAll(inputs.Select(async input =>
        {
            try
            {
                await store.UpsertAsync(input);
                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        }));

        var row = Assert.Single(await store.SelectAllAsync());
        Assert.Contains(row.Weight, inputs.Select(static i => i.Weight));
        return outcomes.OfType<Exception>().ToArray();
    }
}
