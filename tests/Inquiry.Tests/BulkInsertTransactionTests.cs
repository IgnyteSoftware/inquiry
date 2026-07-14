using Inquiry.BulkCopy;
using Inquiry.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Inquiry.Tests;

public sealed class BulkInsertTransactionTests
{
    [Fact]
    public async Task NativeBulkInsertInsideAmbientTransactionFailsBeforeCallingCopier()
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

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => inquiry.BulkInsertAsync(definition, new[] { new Row(1) }));

        Assert.Contains("cannot run inside an Inquiry transaction", exception.Message);
        Assert.Contains("[InquiryInsertAll]", exception.Message);
        Assert.False(copier.Called);
    }

    private sealed record Row(int Id);

    private sealed class RecordingBulkCopier : IInquiryBulkCopier
    {
        public bool Called { get; private set; }

        public Task<long> BulkInsertAsync<TEntity>(
            InquiryBulkInsertDefinition<TEntity> definition,
            IEnumerable<TEntity> rows,
            CancellationToken cancellationToken = default)
            where TEntity : class
        {
            Called = true;
            return Task.FromResult(0L);
        }
    }
}
