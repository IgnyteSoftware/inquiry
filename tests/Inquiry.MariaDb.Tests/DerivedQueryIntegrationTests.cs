using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Inquiry.Entities;
using Inquiry.MariaDb.Tests.Fixtures;
using Inquiry.Stores;

namespace Inquiry.MariaDb.Tests;

[InquiryTable("Person", GenerateDdl = false)]
public sealed class DerivedPerson
{
    [InquiryKey(IsGenerated = true)]
    public long Id { get; set; }

    [InquiryColumn("Country")]
    public string Country { get; set; } = string.Empty;

    [InquiryColumn("City")]
    public string City { get; set; } = string.Empty;
}

public partial class DerivedPersonStore : InquiryStore<DerivedPerson>
{
    [InquiryInsert]
    public partial Task<int> InsertAsync(DerivedPerson person, CancellationToken cancellationToken = default);

    // Field-less: filter columns derived from the method name.
    [InquirySelectAllByField]
    public partial Task<IReadOnlyList<DerivedPerson>> SelectByCountryAsync(string country, CancellationToken cancellationToken = default);

    [InquirySelectAllByField]
    public partial Task<IReadOnlyList<DerivedPerson>> SelectByCountryAndCityAsync(string country, string city, CancellationToken cancellationToken = default);
}

/// <summary>
/// Derived query methods end-to-end against MariaDB: a field-less <c>[InquirySelectAllByField]</c>
/// filters on columns inferred from the method name.
/// </summary>
[Collection(MariaDbCollection.Name)]
public sealed class DerivedQueryIntegrationTests
{
    private readonly MariaDbContainerFixture _fixture;
    public DerivedQueryIntegrationTests(MariaDbContainerFixture fixture) => _fixture = fixture;

    private const string Ddl = "CREATE TABLE `Person` (`Id` BIGINT AUTO_INCREMENT PRIMARY KEY, `Country` VARCHAR(255) NOT NULL, `City` VARCHAR(255) NOT NULL);";

    [SkippableFact]
    public async Task DerivedSingleAndMultiFieldFiltersWork()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await MariaDbTestHarness.CreateFromDdlAsync(_fixture.AdminConnectionString, Ddl, "derived");
        var store = harness.GetRequiredService<DerivedPersonStore>();

        await store.InsertAsync(new DerivedPerson { Country = "UK", City = "London" });
        await store.InsertAsync(new DerivedPerson { Country = "UK", City = "Leeds" });
        await store.InsertAsync(new DerivedPerson { Country = "France", City = "Paris" });

        var uk = await store.SelectByCountryAsync("UK");
        Assert.Equal(2, uk.Count);
        Assert.All(uk, p => Assert.Equal("UK", p.Country));

        var ukLondon = Assert.Single(await store.SelectByCountryAndCityAsync("UK", "London"));
        Assert.Equal("London", ukLondon.City);

        Assert.Empty(await store.SelectByCountryAndCityAsync("France", "London"));
    }
}
