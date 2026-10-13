using Inquiry.Tests.Shared;

namespace Inquiry.MySql.Tests;

/// <summary>Connection ownership and open telemetry of <see cref="MySqlBulkCopier"/> (#330). No database required.</summary>
public sealed class BulkCopierConnectionOwnershipTests
{
    [Fact]
    public Task ThrowingOpenTelemetryDisposesDedicatedConnection()
        => BulkCopierConnectionOwnershipAssertions.ThrowingOpenTelemetryDisposesDedicatedConnectionAsync(factory => new MySqlBulkCopier(factory));

    [Fact]
    public Task FailedOpenIsRecordedAsFailure()
        => BulkCopierConnectionOwnershipAssertions.FailedOpenIsRecordedAsFailureAsync(factory => new MySqlBulkCopier(factory));

    [Fact]
    public Task FailedOpenErrorSurvivesThrowingTelemetry()
        => BulkCopierConnectionOwnershipAssertions.FailedOpenErrorSurvivesThrowingTelemetryAsync(factory => new MySqlBulkCopier(factory));

    [Fact]
    public Task RejectedBorrowedConnectionIsNotDisposed()
        => BulkCopierConnectionOwnershipAssertions.RejectedBorrowedConnectionIsNotDisposedAsync(factory => new MySqlBulkCopier(factory));
}
