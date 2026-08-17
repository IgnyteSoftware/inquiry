using Inquiry.BulkCopy;
using Inquiry.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Inquiry.Tests;

public sealed class BulkInsertTransactionTests
{
    [Fact]
    public async Task NativeBulkInsertInsideAmbientTransactionPassesBorrowedConnectionAndTransactionToCopier()
    {
        var copier = new RecordingBulkCopier();
        await using var fixture = await SqliteInquiryFixture.CreateAsync(
            services => services.AddSingleton<IInquiryBulkCopier>(copier));
        using var scope = fixture.CreateScope();
        var inquiry = scope.ServiceProvider.GetRequiredService<IInquiry>();
        await using var transaction = await inquiry.BeginTransactionAsync();
        var definition = new InquiryBulkInsertDefinition<Row>(
            null,
            "Rows",
            new[] { "Id" },
            static (row, _) => row.Id);

        var written = await inquiry.BulkInsertAsync(definition, new[] { new Row(1) });

        Assert.Equal(1, written);
        Assert.True(copier.Called);
        Assert.Same(transaction.Connection, copier.Context!.Connection);
        Assert.Same(transaction.Transaction, copier.Context.Transaction);
        Assert.True(copier.Context.IsEnlisted);
    }

    [Fact]
    public async Task NativeBulkInsertFromCapturedChildAfterTransactionClosesFailsBeforeCallingCopier()
    {
        var copier = new RecordingBulkCopier();
        await using var fixture = await SqliteInquiryFixture.CreateAsync(
            services => services.AddSingleton<IInquiryBulkCopier>(copier));
        using var scope = fixture.CreateScope();
        var inquiry = scope.ServiceProvider.GetRequiredService<IInquiry>();
        var definition = new InquiryBulkInsertDefinition<Row>(
            null,
            "Rows",
            new[] { "Id" },
            static (row, _) => row.Id);
        var releaseChild = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Task child;
        await using (var transaction = await inquiry.BeginTransactionAsync())
        {
            child = Task.Run(async () =>
            {
                await releaseChild.Task;
                await inquiry.BulkInsertAsync(definition, new[] { new Row(1) });
            });
        }

        releaseChild.SetResult();
        var exception = await Assert.ThrowsAsync<ObjectDisposedException>(() => child);

        Assert.Contains("captured an Inquiry transaction that has already been committed, rolled back, or disposed", exception.Message);
        Assert.False(copier.Called);
    }

    [Fact]
    public async Task RequiredAmbientTransactionFailsBeforeCallingCopierWhenNoneIsActive()
    {
        var copier = new RecordingBulkCopier();
        await using var fixture = await SqliteInquiryFixture.CreateAsync(
            services => services.AddSingleton<IInquiryBulkCopier>(copier));
        using var scope = fixture.CreateScope();
        var inquiry = scope.ServiceProvider.GetRequiredService<IInquiry>();
        var definition = new InquiryBulkInsertDefinition<Row>(null, "Rows", new[] { "Id" }, static (row, _) => row.Id);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => inquiry.BulkInsertAsync(
            definition,
            new[] { new Row(1) },
            new InquiryBulkInsertOptions { ConnectionBehavior = InquiryBulkInsertConnectionBehavior.RequireAmbientTransaction }));

        Assert.Contains("requires an ambient Inquiry transaction", exception.Message);
        Assert.False(copier.Called);
    }

    [Fact]
    public async Task RequiredDedicatedConnectionFailsBeforeCallingCopierInsideTransaction()
    {
        var copier = new RecordingBulkCopier();
        await using var fixture = await SqliteInquiryFixture.CreateAsync(
            services => services.AddSingleton<IInquiryBulkCopier>(copier));
        using var scope = fixture.CreateScope();
        var inquiry = scope.ServiceProvider.GetRequiredService<IInquiry>();
        await using var transaction = await inquiry.BeginTransactionAsync();
        var definition = new InquiryBulkInsertDefinition<Row>(null, "Rows", new[] { "Id" }, static (row, _) => row.Id);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => inquiry.BulkInsertAsync(
            definition,
            new[] { new Row(1) },
            new InquiryBulkInsertOptions { ConnectionBehavior = InquiryBulkInsertConnectionBehavior.RequireDedicatedConnection }));

        Assert.Contains("requires a dedicated connection", exception.Message);
        Assert.False(copier.Called);
    }

    private sealed record Row(int Id);

    private sealed class RecordingBulkCopier : IInquiryBulkCopier
    {
        public bool Called { get; private set; }
        public InquiryBulkInsertContext? Context { get; private set; }

        public Task<long> BulkInsertAsync<TEntity>(
            InquiryBulkInsertDefinition<TEntity> definition,
            IEnumerable<TEntity> rows,
            InquiryBulkInsertContext context,
            CancellationToken cancellationToken = default)
            where TEntity : class
        {
            Called = true;
            Context = context;
            return Task.FromResult(rows.LongCount());
        }
    }
}
