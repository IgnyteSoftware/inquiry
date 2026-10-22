using System.Diagnostics.CodeAnalysis;
using Inquiry.Seeding;
using Microsoft.Extensions.DependencyInjection;

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
        [SuppressMessage("Major Code Smell", "S1144:Unused private types or members should be removed", Justification = "Dependency injection calls this constructor through reflection.")]
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
        [SuppressMessage("Major Code Smell", "S1144:Unused private types or members should be removed", Justification = "Dependency injection calls this constructor through reflection.")]
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

        Assert.Equal(["first", "second"], services.GetRequiredService<Journal>().Entries);
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
        var exception = await Record.ExceptionAsync(() => services.SeedInquiryAsync());
        Assert.Null(exception);
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

    [SuppressMessage("Minor Code Smell", "S2094:Classes should not be empty", Justification = "Dependency injection resolves this marker type. It needs no members.")]
    private sealed class ScopeProbe
    {
    }

    private sealed class ScopedSeeder : IInquiryDataSeeder
    {
        private readonly Journal _journal;

        [SuppressMessage("Major Code Smell", "S1144:Unused private types or members should be removed", Justification = "Dependency injection calls this constructor through reflection.")]
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
