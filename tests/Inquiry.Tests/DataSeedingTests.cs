using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Inquiry.Seeding;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Inquiry.Tests;

/// <summary>
/// The seeding convention: <c>AddInquirySeeder&lt;T&gt;()</c> registers scoped seeders that
/// <c>SeedInquiryAsync()</c> runs sequentially in registration order, inside one scope;
/// duplicate registration of the same seeder type is a no-op.
/// </summary>
public sealed class DataSeedingTests
{
    private sealed class Journal
    {
        public List<string> Entries { get; } = new();
    }

    private sealed class FirstSeeder : IInquiryDataSeeder
    {
        private readonly Journal _journal;
        public FirstSeeder(Journal journal) => _journal = journal;

        public Task SeedAsync(CancellationToken cancellationToken = default)
        {
            _journal.Entries.Add("first");
            return Task.CompletedTask;
        }
    }

    private sealed class SecondSeeder : IInquiryDataSeeder
    {
        private readonly Journal _journal;
        public SecondSeeder(Journal journal) => _journal = journal;

        public Task SeedAsync(CancellationToken cancellationToken = default)
        {
            _journal.Entries.Add("second");
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task SeedersRunSequentiallyInRegistrationOrder()
    {
        var services = new ServiceCollection()
            .AddSingleton<Journal>()
            .AddInquirySeeder<FirstSeeder>()
            .AddInquirySeeder<SecondSeeder>()
            .BuildServiceProvider();

        await services.SeedInquiryAsync();

        Assert.Equal(new[] { "first", "second" }, services.GetRequiredService<Journal>().Entries);
    }

    [Fact]
    public async Task DuplicateRegistrationRunsOnce()
    {
        var services = new ServiceCollection()
            .AddSingleton<Journal>()
            .AddInquirySeeder<FirstSeeder>()
            .AddInquirySeeder<FirstSeeder>()
            .BuildServiceProvider();

        await services.SeedInquiryAsync();

        Assert.Single(services.GetRequiredService<Journal>().Entries);
    }

    [Fact]
    public async Task NoRegisteredSeedersIsANoOp()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        await services.SeedInquiryAsync();
    }

    [Fact]
    public async Task SeedersResolveScopedDependencies()
    {
        // The seeder scope must supply scoped services (generated stores are scoped).
        var services = new ServiceCollection()
            .AddSingleton<Journal>()
            .AddScoped<ScopeProbe>()
            .AddInquirySeeder<ScopedSeeder>()
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        await services.SeedInquiryAsync();

        Assert.Single(services.GetRequiredService<Journal>().Entries);
    }

    private sealed class ScopeProbe
    {
    }

    private sealed class ScopedSeeder : IInquiryDataSeeder
    {
        private readonly Journal _journal;

        public ScopedSeeder(ScopeProbe probe, Journal journal)
        {
            // The probe parameter exists purely to prove scoped resolution succeeds.
            _ = probe ?? throw new ArgumentNullException(nameof(probe));
            _journal = journal;
        }

        public Task SeedAsync(CancellationToken cancellationToken = default)
        {
            _journal.Entries.Add("scoped");
            return Task.CompletedTask;
        }
    }
}
