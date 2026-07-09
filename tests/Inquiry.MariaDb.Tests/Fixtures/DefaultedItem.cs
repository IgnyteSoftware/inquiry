using Inquiry.Entities;

namespace Inquiry.MariaDb.Tests.Fixtures;

/// <summary>
/// Test fixture mirroring the SQLite DefaultedItem fixture: a Status column declared with
/// <c>UseDatabaseDefault = true</c> so the database default applies on INSERT but the
/// entity's value should be persisted on UPDATE. Local to the MariaDB test project so we can
/// pin the cross-dialect contract for the upsert UPDATE branch on the real engine.
/// </summary>
[InquiryTable("TDefaultedItem")]
public sealed class DefaultedItem
{
    [InquiryKey]
    public long Key { get; set; }

    [InquiryColumn]
    public string Name { get; set; } = string.Empty;

    [InquiryColumn(UseDatabaseDefault = true)]
    public string Status { get; set; } = string.Empty;
}
