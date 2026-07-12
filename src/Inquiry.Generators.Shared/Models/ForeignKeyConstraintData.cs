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
    string ConstraintName);
