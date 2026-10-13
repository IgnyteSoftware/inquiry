using Inquiry.Tests.Shared;

namespace Inquiry.MariaDb.Tests;

/// <summary>Connection ownership and open telemetry of <see cref="MariaDbBulkCopier"/> (#330). No database required.</summary>
public sealed class BulkCopierConnectionOwnershipTests
{
    [Fact]
    public Task ThrowingOpenTelemetryDisposesDedicatedConnection()
        => BulkCopierConnectionOwnershipAssertions.ThrowingOpenTelemetryDisposesDedicatedConnectionAsync(factory => new MariaDbBulkCopier(factory));

    [Fact]
    public Task FailedOpenIsRecordedAsFailure()
        => BulkCopierConnectionOwnershipAssertions.FailedOpenIsRecordedAsFailureAsync(factory => new MariaDbBulkCopier(factory));

    [Fact]
    public Task FailedOpenErrorSurvivesThrowingTelemetry()
        => BulkCopierConnectionOwnershipAssertions.FailedOpenErrorSurvivesThrowingTelemetryAsync(factory => new MariaDbBulkCopier(factory));

    [Fact]
    public Task RejectedBorrowedConnectionIsNotDisposed()
        => BulkCopierConnectionOwnershipAssertions.RejectedBorrowedConnectionIsNotDisposedAsync(factory => new MariaDbBulkCopier(factory));
}
