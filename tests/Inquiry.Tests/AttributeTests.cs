using Inquiry.Entities;
using Inquiry.Stores;

namespace Inquiry.Tests;

public sealed class AttributeTests
{
    [Fact]
    public void TableAttributeStoresNameAndSchema()
    {
        var attribute = new InquiryTableAttribute("TOrganization")
        {
            Schema = "dbo",
        };

        Assert.Equal("TOrganization", attribute.Name);
        Assert.Equal("dbo", attribute.Schema);
    }

    [Fact]
    public void SelectByFieldAttributeStoresField()
    {
        var attribute = new InquirySelectAllByFieldAttribute("IsActive");

        Assert.Equal("IsActive", attribute.Field);
    }

    [Fact]
    public void ForeignKeyAttributeStoresReferencedTableAndColumn()
    {
        var attribute = new InquiryForeignKeyAttribute("TOrganization", "Key");

        Assert.Null(attribute.Name);
        Assert.Equal("TOrganization", attribute.ReferencedTable);
        Assert.Equal("Key", attribute.ReferencedColumn);
    }

    [Fact]
    public void ForeignKeyAttributeCanStoreExplicitColumnName()
    {
        var attribute = new InquiryForeignKeyAttribute("OrganizationKey", "TOrganization", "Key");

        Assert.Equal("OrganizationKey", attribute.Name);
        Assert.Equal("TOrganization", attribute.ReferencedTable);
        Assert.Equal("Key", attribute.ReferencedColumn);
    }

    [Fact]
    public void ForeignKeyAttributeRejectsEmptyReference()
    {
        Assert.Throws<ArgumentException>(() => new InquiryForeignKeyAttribute(string.Empty, "Key"));
        Assert.Throws<ArgumentException>(() => new InquiryForeignKeyAttribute("TOrganization", string.Empty));
    }
}
