using Inquiry.Commands;
using Inquiry.Interceptors;
using Inquiry.Parameters;
using Inquiry.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace Inquiry.Tests;

public sealed class InquiryTestingTests
{
    [Fact]
    public async Task SandboxRollsBackAndDisposesItsScope()
    {
        var disposal = new DisposalState();
        await using var fixture = await SqliteInquiryFixture.CreateAsync(services =>
        {
            services.AddSingleton(disposal);
            services.AddScoped<DisposalProbe>();
        });
        await fixture.ExecuteDdlAsync("CREATE TABLE Items (Id INTEGER PRIMARY KEY, Name TEXT NOT NULL);");
        var sandbox = new InquirySandbox(fixture.Services);

        var result = await sandbox.RunAsync(async (context, token) =>
        {
            _ = context.Services.GetRequiredService<DisposalProbe>();
            await context.Transaction.ExecuteAsync(
                new InquiryCommand("INSERT INTO Items (Id, Name) VALUES (1, 'sandbox')"), token);
            return await context.Transaction.ExecuteScalarAsync<long>(
                new InquiryCommand("SELECT COUNT(*) FROM Items"), token);
        });

        Assert.Equal(1, result);
        Assert.True(disposal.Disposed);
        using var scope = fixture.CreateScope();
        var inquiry = scope.ServiceProvider.GetRequiredService<IInquiry>();
        Assert.Equal(0, await inquiry.ExecuteScalarAsync<long>(new InquiryCommand("SELECT COUNT(*) FROM Items")));
    }

    [Fact]
    public async Task SandboxPreservesUserExceptionAndRollsBack()
    {
        var disposal = new DisposalState();
        await using var fixture = await SqliteInquiryFixture.CreateAsync(services =>
        {
            services.AddSingleton(disposal);
            services.AddScoped<DisposalProbe>();
        });
        await fixture.ExecuteDdlAsync("CREATE TABLE Items (Id INTEGER PRIMARY KEY);");
        var sandbox = new InquirySandbox(fixture.Services);
        var expected = new InvalidOperationException("user failure");

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() => sandbox.RunAsync(async (context, token) =>
        {
            _ = context.Services.GetRequiredService<DisposalProbe>();
            await context.Transaction.ExecuteAsync(new InquiryCommand("INSERT INTO Items (Id) VALUES (1)"), token);
            throw expected;
        }));

        Assert.Same(expected, actual);
        Assert.True(disposal.Disposed);
        using var scope = fixture.CreateScope();
        var inquiry = scope.ServiceProvider.GetRequiredService<IInquiry>();
        Assert.Equal(0, await inquiry.ExecuteScalarAsync<long>(new InquiryCommand("SELECT COUNT(*) FROM Items")));
    }

    [Fact]
    public async Task SandboxPreservesUserExceptionWhenRollbackAndDisposalFail()
    {
        var transaction = DispatchProxy.Create<Transactions.IInquiryTransaction, FailingSandboxProxy>();
        ((FailingSandboxProxy)(object)transaction).Mode = FailingSandboxProxyMode.Transaction;
        var inquiry = DispatchProxy.Create<IInquiry, FailingSandboxProxy>();
        ((FailingSandboxProxy)(object)inquiry).Transaction = transaction;
        var services = new ServiceCollection()
            .AddScoped(_ => inquiry)
            .AddScoped<FailingScopeDisposable>()
            .BuildServiceProvider();
        await using (services.ConfigureAwait(false))
        {
            var sandbox = new InquirySandbox(services);
            var expected = new InvalidOperationException("user failure");

            var actual = await Assert.ThrowsAsync<InvalidOperationException>(() => sandbox.RunAsync((context, cancellationToken) =>
            {
                context.Services.GetRequiredService<FailingScopeDisposable>();
                throw expected;
            }));

            Assert.Same(expected, actual);
        }
    }

    [Fact]
    public async Task SandboxCancellationRollsBackAndDisposesItsScope()
    {
        var disposal = new DisposalState();
        await using var fixture = await SqliteInquiryFixture.CreateAsync(services =>
        {
            services.AddSingleton(disposal);
            services.AddScoped<DisposalProbe>();
        });
        await fixture.ExecuteDdlAsync("CREATE TABLE Items (Id INTEGER PRIMARY KEY);");
        var sandbox = new InquirySandbox(fixture.Services);
        using var cancellation = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sandbox.RunAsync(async (context, token) =>
        {
            _ = context.Services.GetRequiredService<DisposalProbe>();
            await context.Transaction.ExecuteAsync(new InquiryCommand("INSERT INTO Items (Id) VALUES (1)"), token);
            cancellation.Cancel();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        }, cancellation.Token));

        Assert.True(disposal.Disposed);
        using var scope = fixture.CreateScope();
        var inquiry = scope.ServiceProvider.GetRequiredService<IInquiry>();
        Assert.Equal(0, await inquiry.ExecuteScalarAsync<long>(new InquiryCommand("SELECT COUNT(*) FROM Items")));
    }

    [Fact]
    public async Task SandboxRejectsNestedRunsWithoutEndingOuterRun()
    {
        await using var fixture = await SqliteInquiryFixture.CreateAsync();
        await fixture.ExecuteDdlAsync("CREATE TABLE Items (Id INTEGER PRIMARY KEY);");
        var sandbox = new InquirySandbox(fixture.Services);

        await sandbox.RunAsync(async (context, token) =>
        {
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => sandbox.RunAsync((_, _) => Task.CompletedTask, token));
            Assert.Contains("cannot be nested", exception.Message);
            await context.Transaction.ExecuteAsync(new InquiryCommand("INSERT INTO Items (Id) VALUES (1)"), token);
        });

        using var scope = fixture.CreateScope();
        var inquiry = scope.ServiceProvider.GetRequiredService<IInquiry>();
        Assert.Equal(0, await inquiry.ExecuteScalarAsync<long>(new InquiryCommand("SELECT COUNT(*) FROM Items")));
    }

    [Fact]
    public void EntityFactoriesHaveIndependentDeterministicSequences()
    {
        var first = new EntityFactory<FactoryEntity>(sequence => new FactoryEntity { Id = sequence });
        var second = new EntityFactory<FactoryEntity>(sequence => new FactoryEntity { Id = sequence });

        Assert.Equal(new long[] { 1, 2, 3 }, first.BuildMany(3).Select(entity => entity.Id));
        Assert.Equal(new long[] { 1, 2 }, second.BuildMany(2).Select(entity => entity.Id));
    }

    [Fact]
    public async Task EntityFactorySequenceIsUniqueAcrossParallelBuilds()
    {
        var factory = new EntityFactory<FactoryEntity>(sequence => new FactoryEntity { Id = sequence });

        var entities = await Task.WhenAll(Enumerable.Range(0, 200)
            .Select(_ => Task.Run(() => factory.Build())));

        Assert.Equal(200, entities.Select(entity => entity.Id).Distinct().Count());
        Assert.Equal(Enumerable.Range(1, 200).Select(value => (long)value), entities.Select(entity => entity.Id).Order());
    }

    [Fact]
    public void EntityFactoryAppliesComposableStatesInRequestedOrder()
    {
        var factory = new EntityFactory<FactoryEntity>(sequence => new FactoryEntity { Id = sequence, Name = "base" })
            .State("prefix", entity => entity.Name = "prefix-" + entity.Name)
            .State("suffix", (entity, sequence) => entity.Name += "-" + sequence);

        var entity = factory.Build("prefix", "suffix");

        Assert.Equal("prefix-base-1", entity.Name);
    }

    [Fact]
    public void EntityFactoryAcceptsBogusStyleGenerateDelegate()
    {
        var generator = new BogusStyleGenerator();
        var factory = new EntityFactory<FactoryEntity>(generator.Generate);

        Assert.Equal("generated-1", factory.Build().Name);
        Assert.Equal("generated-2", factory.Build().Name);
    }

    [Fact]
    public async Task FixtureCreatesWorkingPipeline()
    {
        await using var fixture = await SqliteInquiryFixture.CreateAsync();
        await fixture.ExecuteDdlAsync("CREATE TABLE Items (Id INTEGER PRIMARY KEY, Name TEXT NOT NULL);");

        using var scope = fixture.CreateScope();
        var inquiry = scope.ServiceProvider.GetRequiredService<IInquiry>();

        var affected = await inquiry.ExecuteAsync(new InquiryCommand(
            "INSERT INTO Items (Id, Name) VALUES (@Id, @Name)",
            new[]
            {
                new InquiryParameter("Id", 1),
                new InquiryParameter("Name", "Alpha"),
            }));
        Assert.Equal(1, affected);

        var name = await inquiry.ExecuteScalarAsync<string>(new InquiryCommand(
            "SELECT Name FROM Items WHERE Id = @Id",
            new[] { new InquiryParameter("Id", 1) }));
        Assert.Equal("Alpha", name);
    }

    [Fact]
    public async Task FixturesAreIsolatedFromEachOther()
    {
        await using var first = await SqliteInquiryFixture.CreateAsync();
        await using var second = await SqliteInquiryFixture.CreateAsync();

        Assert.NotEqual(first.ConnectionString, second.ConnectionString);

        await first.ExecuteDdlAsync("CREATE TABLE Items (Id INTEGER PRIMARY KEY);");

        using var scope = second.CreateScope();
        var inquiry = scope.ServiceProvider.GetRequiredService<IInquiry>();
        await Assert.ThrowsAsync<SqliteException>(
            () => inquiry.ExecuteScalarAsync<long>(new InquiryCommand("SELECT COUNT(*) FROM Items")));
    }

    [Fact]
    public async Task RecordingInterceptorCapturesTextAndParameters()
    {
        var recorder = new RecordingCommandInterceptor();
        await using var fixture = await SqliteInquiryFixture.CreateAsync(
            services => services.AddSingleton<IInquiryCommandInterceptor>(recorder));
        await fixture.ExecuteDdlAsync("CREATE TABLE Items (Id INTEGER PRIMARY KEY, Name TEXT NOT NULL);");

        using var scope = fixture.CreateScope();
        var inquiry = scope.ServiceProvider.GetRequiredService<IInquiry>();
        await inquiry.ExecuteAsync(new InquiryCommand(
            "INSERT INTO Items (Id, Name) VALUES (@Id, @Name)",
            new[]
            {
                new InquiryParameter("Id", 1),
                new InquiryParameter("Name", "Alpha"),
            }));

        var recorded = Assert.Single(recorder.Commands);
        Assert.Equal("INSERT INTO Items (Id, Name) VALUES (@Id, @Name)", recorded.CommandText);
        Assert.Equal(1, recorded.RecordsAffected);
        Assert.Null(recorded.Exception);
        Assert.Equal(2, recorded.Parameters.Count);
        Assert.Contains(recorded.Parameters, p => p.Name.EndsWith("Id", StringComparison.Ordinal) && Equals(p.Value, 1));
        Assert.Contains(recorded.Parameters, p => p.Name.EndsWith("Name", StringComparison.Ordinal) && Equals(p.Value, "Alpha"));

        recorder.Clear();
        Assert.Empty(recorder.Commands);
    }

    [Fact]
    public async Task RecordingInterceptorCapturesFailures()
    {
        var recorder = new RecordingCommandInterceptor();
        await using var fixture = await SqliteInquiryFixture.CreateAsync(
            services => services.AddSingleton<IInquiryCommandInterceptor>(recorder));

        using var scope = fixture.CreateScope();
        var inquiry = scope.ServiceProvider.GetRequiredService<IInquiry>();
        await Assert.ThrowsAsync<SqliteException>(
            () => inquiry.ExecuteAsync(new InquiryCommand("INSERT INTO NoSuchTable (Id) VALUES (1)")));

        var recorded = Assert.Single(recorder.Commands);
        Assert.IsType<SqliteException>(recorded.Exception);
        Assert.Null(recorded.RecordsAffected);
    }

    [Fact]
    public async Task AssertExecutedFindsMatchingCommandAndThrowsOtherwise()
    {
        var recorder = new RecordingCommandInterceptor();
        await using var fixture = await SqliteInquiryFixture.CreateAsync(
            services => services.AddSingleton<IInquiryCommandInterceptor>(recorder));
        await fixture.ExecuteDdlAsync("CREATE TABLE Items (Id INTEGER PRIMARY KEY);");

        using var scope = fixture.CreateScope();
        var inquiry = scope.ServiceProvider.GetRequiredService<IInquiry>();
        await inquiry.ExecuteAsync(new InquiryCommand("INSERT INTO Items (Id) VALUES (1)"));

        var recorded = recorder.AssertExecuted("INSERT INTO Items");
        Assert.Equal(1, recorded.RecordsAffected);

        recorder.AssertNotExecuted("DELETE FROM Items");

        var missing = Assert.Throws<InvalidOperationException>(() => recorder.AssertExecuted("DELETE FROM Items"));
        Assert.Contains("DELETE FROM Items", missing.Message);
        Assert.Contains("INSERT INTO Items (Id) VALUES (1)", missing.Message);

        var unexpected = Assert.Throws<InvalidOperationException>(() => recorder.AssertNotExecuted("INSERT INTO Items"));
        Assert.Contains("INSERT INTO Items", unexpected.Message);
    }

    private sealed class DisposalState
    {
        public bool Disposed { get; set; }
    }

    private sealed class DisposalProbe : IDisposable
    {
        private readonly DisposalState _state;

        public DisposalProbe(DisposalState state) => _state = state;

        public void Dispose() => _state.Disposed = true;
    }

    private sealed class FactoryEntity
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    private sealed class BogusStyleGenerator
    {
        private int _sequence;

        public FactoryEntity Generate()
            => new() { Name = "generated-" + Interlocked.Increment(ref _sequence) };
    }

    public class FailingSandboxProxy : DispatchProxy
    {
        public FailingSandboxProxyMode Mode { get; set; }
        public Transactions.IInquiryTransaction? Transaction { get; set; }

        protected override object? Invoke(System.Reflection.MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(IInquiry.BeginTransactionAsync))
            {
                return Task.FromResult(Transaction!);
            }

            if (Mode == FailingSandboxProxyMode.Transaction && targetMethod?.Name == nameof(Transactions.IInquiryTransaction.RollbackAsync))
            {
                return Task.FromException(new InvalidOperationException("rollback failure"));
            }

            if (Mode == FailingSandboxProxyMode.Transaction && targetMethod?.Name == nameof(IAsyncDisposable.DisposeAsync))
            {
                return new ValueTask(Task.FromException(new InvalidOperationException("transaction disposal failure")));
            }

            throw new NotSupportedException(targetMethod?.Name);
        }
    }

    public enum FailingSandboxProxyMode
    {
        Inquiry,
        Transaction,
    }

    private sealed class FailingScopeDisposable : IDisposable
    {
        public void Dispose() => throw new InvalidOperationException("scope disposal failure");
    }
}
