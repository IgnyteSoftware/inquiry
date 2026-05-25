using Microsoft.CodeAnalysis;

namespace Inquiry.Generators.Models;

/// <summary>
/// Describes a navigation property marked with <c>[InquiryRelation]</c>.
/// Navigation properties are populated by eager-loading store operations.
/// </summary>
internal sealed class RelationModel
{
    public RelationModel(
        IPropertySymbol symbol,
        string propertyName,
        string foreignKeyProperty,
        INamedTypeSymbol childEntitySymbol,
        bool isCollection)
    {
        Symbol = symbol;
        PropertyName = propertyName;
        ForeignKeyProperty = foreignKeyProperty;
        ChildEntitySymbol = childEntitySymbol;
        IsCollection = isCollection;
    }

    /// <summary>The navigation property symbol.</summary>
    public IPropertySymbol Symbol { get; }

    /// <summary>Property name on the parent entity (e.g. <c>Products</c>).</summary>
    public string PropertyName { get; }

    /// <summary>
    /// Property name on the child entity that holds the FK reference back to the parent key
    /// (e.g. <c>CategoryKey</c>).
    /// </summary>
    public string ForeignKeyProperty { get; }

    /// <summary>The child entity type symbol (element type of the collection or the reference type).</summary>
    public INamedTypeSymbol ChildEntitySymbol { get; }

    /// <summary>True when the navigation property is a collection (List&lt;T&gt;, IReadOnlyList&lt;T&gt;, etc.).</summary>
    public bool IsCollection { get; }
}
