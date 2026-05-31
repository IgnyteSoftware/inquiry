using Inquiry.Connections;
using Inquiry.DependencyInjection;
using Inquiry.Northwind;
using Inquiry.Northwind.Models;
using Inquiry.Northwind.Stores;
using Inquiry.Sqlite.Tests.Fixtures;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using System.Data.Common;

namespace Inquiry.Sqlite.Tests;

/// <summary>
/// W4 integration: running the full CRUD suite with <see cref="PreparedStatementMode.Auto"/> (over a
/// SQLite factory that opts into the persistent-prepared capability so <c>PrepareAsync</c> actually
/// fires) produces identical results to the default <see cref="PreparedStatementMode.None"/> path,
/// including correctness after a parameter value changes between calls.
/// </summary>
public sealed class PreparedStatementIntegrationTests
{
    [Fact]
    public async Task AutoPrepareYieldsSameResultsAsNone()
    {
        var none = await RunCrudAsync(prepare: false);
        var auto = await RunCrudAsync(prepare: true);

        Assert.Equal(none.Inserted, auto.Inserted);
        Assert.Equal(none.FirstName, auto.FirstName);
        Assert.Equal(none.UsCount, auto.UsCount);
        Assert.Equal(none.Updated, auto.Updated);
        Assert.Equal(none.UpdatedName, auto.UpdatedName);
        Assert.Equal(none.UpdatedCountry, auto.UpdatedCountry);
        Assert.Equal(none.Deleted, auto.Deleted);
        Assert.Equal(none.AfterDeleteNull, auto.AfterDeleteNull);
    }

    private static async Task<CrudResult> RunCrudAsync(bool prepare)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = "InquiryPrepared_" + Guid.NewGuid().ToString("N"),
            Mode = SqliteOpenMode.Memory,
            Cache = SqliteCacheMode.Shared,
        }.ToString();

        await using var keeper = new SqliteConnection(connectionString);
        await keeper.OpenAsync();
        await using (var cmd = keeper.CreateCommand())
        {
            cmd.CommandText = NorthwindSchema.SqliteDdl;
            await cmd.ExecuteNonQueryAsync();
        }

        var services = new ServiceCollection();
        if (prepare)
        {
            services.AddInquiry(o => o.PrepareStatements = PreparedStatementMode.Auto);
        }
        else
        {
            services.AddInquiry();
        }

        // A SQLite factory that advertises the persistent-prepared capability so the Auto path
        // genuinely exercises DbCommand.PrepareAsync (the shipped SqliteInquiryConnectionFactory
        // reports false by design). AddInquiry() registers the generated stores via assembly scan.
        services.AddSingleton<IInquiryConnectionFactory>(new PrepareCapableSqliteFactory(connectionString));

        await using var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<CustomerStore>();

        var customer = new Customer { CustomerID = "ACME1", CompanyName = "Acme Research", Country = "USA" };

        var inserted = await store.InsertAsync(customer);
        var selected = await store.SelectByKeyAsync("ACME1");
        var usCustomers = await store.SelectByCountryAsync("USA");

        customer.CompanyName = "Acme Updated";
        customer.Country = "Canada";
        var updated = await store.UpdateAsync(customer);
        var afterUpdate = await store.SelectByKeyAsync("ACME1");

        var deleted = await store.DeleteByKeyAsync("ACME1");
        var afterDelete = await store.SelectByKeyAsync("ACME1");

        return new CrudResult(
            inserted,
            selected?.CompanyName,
            usCustomers.Count,
            updated,
            afterUpdate?.CompanyName,
            afterUpdate?.Country,
            deleted,
            afterDelete is null);
    }

    private sealed record CrudResult(
        int Inserted,
        string? FirstName,
        int UsCount,
        bool Updated,
        string? UpdatedName,
        string? UpdatedCountry,
        bool Deleted,
        bool AfterDeleteNull);

    private sealed class PrepareCapableSqliteFactory : IInquiryConnectionFactory
    {
        private readonly string _connectionString;
        public PrepareCapableSqliteFactory(string connectionString) => _connectionString = connectionString;

        public bool SupportsPersistentPreparedStatements => true;

        public async ValueTask<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
        {
            var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
    }
}
