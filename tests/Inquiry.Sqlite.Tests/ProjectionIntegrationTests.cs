using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Inquiry.Entities;
using Inquiry.Sqlite.Tests.Fixtures;
using Inquiry.Stores;

namespace Inquiry.Sqlite.Tests;

[InquiryTable("Person")]
public sealed class Person
{
    [InquiryKey(IsGenerated = true)]
    public long Id { get; set; }

    [InquiryColumn("FullName")]
    public string FullName { get; set; } = string.Empty;

    [InquiryColumn("Age")]
    public int Age { get; set; }

    [InquiryColumn("Email")]
    public string? Email { get; set; }
}

[InquiryProjection(typeof(Person))]
public sealed record PersonName
{
    [InquiryColumn("Id")]
    public long Id { get; init; }

    [InquiryColumn("FullName")]
    public string Name { get; init; } = string.Empty;
}

public partial class PersonStore : InquiryStore<Person>
{
    [InquiryInsert]
    public partial Task<int> InsertAsync(Person person, CancellationToken cancellationToken = default);

    [InquirySelectAll]
    public partial Task<IReadOnlyList<Person>> AllAsync(CancellationToken cancellationToken = default);

    [InquirySelectAll]
    public partial Task<IReadOnlyList<PersonName>> NamesAsync(CancellationToken cancellationToken = default);

    [InquirySelectAllByField(nameof(Person.Age))]
    public partial Task<IReadOnlyList<PersonName>> NamesByAgeAsync(int age, CancellationToken cancellationToken = default);
}

/// <summary>W5b projection end-to-end against SQLite: a projection-returning SelectAll selects only the
/// declared columns and materializes the DTO; the full-entity select still works alongside it.</summary>
public sealed class ProjectionIntegrationTests
{
    private const string Ddl = "CREATE TABLE Person (Id INTEGER PRIMARY KEY AUTOINCREMENT, FullName TEXT NOT NULL, Age INTEGER NOT NULL, Email TEXT NULL);";

    [Fact]
    public async Task ProjectionMaterializesDeclaredColumnsSubset()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Ddl, "Projection");
        var store = harness.GetRequiredService<PersonStore>();

        await store.InsertAsync(new Person { FullName = "Ada Lovelace", Age = 36, Email = "ada@example.com" });
        await store.InsertAsync(new Person { FullName = "Alan Turing", Age = 41, Email = null });

        var names = await store.NamesAsync();
        Assert.Equal(2, names.Count);
        Assert.Contains(names, n => n.Id == 1 && n.Name == "Ada Lovelace");
        Assert.Contains(names, n => n.Id == 2 && n.Name == "Alan Turing");

        // The full-entity select still hydrates every column.
        var all = await store.AllAsync();
        Assert.Equal(36, all.Single(p => p.Id == 1).Age);
        Assert.Null(all.Single(p => p.Id == 2).Email);
    }

    [Fact]
    public async Task ProjectionByFieldFiltersAndProjects()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Ddl, "Projection");
        var store = harness.GetRequiredService<PersonStore>();

        await store.InsertAsync(new Person { FullName = "Ada", Age = 36 });
        await store.InsertAsync(new Person { FullName = "Alan", Age = 41 });
        await store.InsertAsync(new Person { FullName = "Grace", Age = 36 });

        var aged36 = await store.NamesByAgeAsync(36);
        Assert.Equal(2, aged36.Count);
        Assert.Contains(aged36, n => n.Name == "Ada");
        Assert.Contains(aged36, n => n.Name == "Grace");
    }
}
