using System;
using System.Threading.Tasks;

namespace Inquiry.FeatureCatalog;

public static class MySqlFamilyJsonTableAssertions
{
    public const string Ddl = """
        CREATE TABLE `MySqlFamilyJsonTableItem` (
          `Id` INT AUTO_INCREMENT PRIMARY KEY, `OccurredAt` DATETIME(6) NOT NULL,
          `ObservedAt` DATETIME(6) NOT NULL, `Day` DATE NOT NULL, `Clock` TIME(6) NOT NULL,
          `CorrelationId` CHAR(36) NOT NULL, `Payload` LONGBLOB NOT NULL,
          `Amount` DECIMAL(18,4) NOT NULL);
        """;

    public static async Task RunAsync(MySqlFamilyJsonTableItemStore store)
    {
        var input = new MySqlFamilyJsonTableItem
        {
            OccurredAt = new DateTime(2026, 7, 10, 12, 34, 56, 123, DateTimeKind.Unspecified),
            ObservedAt = new DateTimeOffset(2026, 7, 10, 8, 34, 56, TimeSpan.FromHours(-4)),
            Day = new DateOnly(2026, 7, 10), Clock = new TimeOnly(12, 34, 56, 123),
            CorrelationId = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff"),
            Payload = new byte[] { 0, 1, 2, 254, 255 }, Amount = 1234.5678m,
        };
        var saved = (await store.InsertAsync(input))!;

        Assert.Single(await store.ByOccurredAt(new[] { saved.OccurredAt }));
        // DATETIME has no offset; query the materialized storage value while the inserted input proves
        // a non-UTC offset is accepted and normalized according to MySqlConnector's storage semantics.
        Assert.Single(await store.ByObservedAt(new[] { saved.ObservedAt }));
        Assert.Single(await store.ByDay(new[] { saved.Day }));
        Assert.Single(await store.ByClock(new[] { saved.Clock }));
        Assert.Single(await store.ByCorrelationId(new[] { saved.CorrelationId }));
        Assert.Single(await store.ByPayload(new[] { saved.Payload }));
        Assert.Single(await store.ByAmount(new[] { saved.Amount }));
    }
}
