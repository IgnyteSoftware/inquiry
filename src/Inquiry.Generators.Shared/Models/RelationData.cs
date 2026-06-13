namespace Inquiry.Generators.Models;

/// <summary>
/// Value-equatable replacement for the old <c>RelationModel</c>. References the child entity by its
/// fully-qualified name; the child's columns/key are resolved from the entity set at emit time.
/// </summary>
internal sealed record RelationData(
    string PropertyName,
    string ForeignKeyProperty,
    string ChildEntityFullyQualifiedName,
    bool IsCollection,
    LocationData? Location = null);
