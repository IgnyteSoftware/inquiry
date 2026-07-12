namespace Inquiry.Generators.Models;

internal sealed record ForeignKeyConstraintData(
    string? LocalSchema,
    string LocalTable,
    string LocalColumn,
    string? ReferencedSchema,
    string ReferencedTable,
    string ReferencedColumn,
    LocationData? Location,
    string CanonicalIdentity,
    string ConstraintName,
    string? RequestedName = null,
    int OnDelete = 0,
    int OnUpdate = 0)
{
    public string LocalProperty { get; init; } = string.Empty;
    public string GeneratedNameCandidate { get; init; } = string.Empty;
    public string? EmittedName { get; init; }
    public ForeignKeyEmissionMode EmissionMode { get; init; }
}

internal enum ForeignKeyEmissionMode { Inline, Deferred, Suppressed }
