using Inquiry.Tests.Shared;

namespace Inquiry.PostgreSql.Tests;

/// <summary>Connection ownership and open telemetry of <see cref="PostgreSqlBulkCopier"/> (#330). No database required.</summary>
public sealed class BulkCopierConnectionOwnershipTests
{
    [Fact]
    public Task ThrowingOpenTelemetryDisposesDedicatedConnection()
        => BulkCopierConnectionOwnershipAssertions.ThrowingOpenTelemetryDisposesDedicatedConnectionAsync(factory => new PostgreSqlBulkCopier(factory));

    [Fact]
    public Task FailedOpenIsRecordedAsFailure()
        => BulkCopierConnectionOwnershipAssertions.FailedOpenIsRecordedAsFailureAsync(factory => new PostgreSqlBulkCopier(factory));

    [Fact]
    public Task FailedOpenErrorSurvivesThrowingTelemetry()
        => BulkCopierConnectionOwnershipAssertions.FailedOpenErrorSurvivesThrowingTelemetryAsync(factory => new PostgreSqlBulkCopier(factory));

    [Fact]
    public Task RejectedBorrowedConnectionIsNotDisposed()
        => BulkCopierConnectionOwnershipAssertions.RejectedBorrowedConnectionIsNotDisposedAsync(factory => new PostgreSqlBulkCopier(factory));
}
