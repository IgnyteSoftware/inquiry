using Inquiry;

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
        var attribute = new InquirySelectByFieldAttribute("IsActive");

        Assert.Equal("IsActive", attribute.Field);
    }
}
