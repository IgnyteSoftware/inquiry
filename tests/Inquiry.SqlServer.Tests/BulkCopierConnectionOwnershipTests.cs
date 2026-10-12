using Inquiry.Tests.Shared;

namespace Inquiry.SqlServer.Tests;

/// <summary>Connection ownership and open telemetry of <see cref="SqlServerBulkCopier"/> (#330). No database required.</summary>
public sealed class BulkCopierConnectionOwnershipTests
{
    [Fact]
    public Task ThrowingOpenTelemetryDisposesDedicatedConnection()
        => BulkCopierConnectionOwnershipAssertions.ThrowingOpenTelemetryDisposesDedicatedConnectionAsync(factory => new SqlServerBulkCopier(factory));

    [Fact]
    public Task FailedOpenIsRecordedAsFailure()
        => BulkCopierConnectionOwnershipAssertions.FailedOpenIsRecordedAsFailureAsync(factory => new SqlServerBulkCopier(factory));

    [Fact]
    public Task FailedOpenErrorSurvivesThrowingTelemetry()
        => BulkCopierConnectionOwnershipAssertions.FailedOpenErrorSurvivesThrowingTelemetryAsync(factory => new SqlServerBulkCopier(factory));

    [Fact]
    public Task RejectedBorrowedConnectionIsNotDisposed()
        => BulkCopierConnectionOwnershipAssertions.RejectedBorrowedConnectionIsNotDisposedAsync(factory => new SqlServerBulkCopier(factory));
}
