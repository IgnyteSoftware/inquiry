namespace Inquiry.Generators.Models;

internal sealed record CheckConstraintData(
    string? Schema,
    string Table,
    string Expression,
    string? RequestedName,
    LocationData? Location)
{
    public string CanonicalIdentity { get; init; } = string.Empty;
    public string? EmittedName { get; init; }
    public int Ordinal { get; init; }
}
