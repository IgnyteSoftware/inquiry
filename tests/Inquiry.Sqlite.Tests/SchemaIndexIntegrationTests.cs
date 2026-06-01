using System.Threading;
using System.Threading.Tasks;
using Inquiry.Entities;
using Inquiry.Generated;
using Inquiry.Sqlite.Tests.Fixtures;
using Inquiry.Stores;
using Microsoft.Data.Sqlite;

namespace Inquiry.Sqlite.Tests;

[InquiryTable("Member")]
public sealed class Member
{
    [InquiryKey(IsGenerated = true)]
    public long Id { get; set; }

    [InquiryColumn("Email", IsUnique = true)]
    public string Email { get; set; } = string.Empty;
}

public partial class MemberStore : InquiryStore<Member>
{
    [InquiryInsert]
    public partial Task<int> InsertAsync(Member member, CancellationToken cancellationToken = default);
}

/// <summary>W7b: the generated schema's UNIQUE index is created and enforced when the full
/// <see cref="InquiryGeneratedSchema.Ddl"/> is executed against SQLite.</summary>
public sealed class SchemaIndexIntegrationTests
{
    [Fact]
    public async Task UniqueIndexFromGeneratedSchemaIsEnforced()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(InquiryGeneratedSchema.Ddl, "GenIndex");
        var store = harness.GetRequiredService<MemberStore>();

        await store.InsertAsync(new Member { Email = "a@example.com" });

        // The generated UNIQUE index rejects the duplicate.
        await Assert.ThrowsAsync<SqliteException>(
            async () => await store.InsertAsync(new Member { Email = "a@example.com" }));
    }
}
