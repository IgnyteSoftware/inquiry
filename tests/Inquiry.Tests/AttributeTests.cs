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
    public void SelectByFieldAttributeStoresSingleField()
    {
        var attribute = new InquirySelectAllByFieldAttribute("IsActive");

        Assert.Single(attribute.Fields);
        Assert.Equal("IsActive", attribute.Fields[0]);
    }

    [Fact]
    public void SelectByFieldAttributeStoresMultipleFieldsInOrder()
    {
        var attribute = new InquirySelectAllByFieldAttribute("CustomerID", "EmployeeID");

        Assert.Equal(2, attribute.Fields.Count);
        Assert.Equal("CustomerID", attribute.Fields[0]);
        Assert.Equal("EmployeeID", attribute.Fields[1]);
    }

    [Fact]
    public void SelectByFieldAttributeParameterlessIsFieldLessAndDerivesFromName()
    {
        // The field-less form is valid — the filter columns are derived from the method name.
        var attribute = new InquirySelectAllByFieldAttribute();
        Assert.Empty(attribute.Fields);
    }

    [Fact]
    public void SelectByFieldAttributeRejectsExplicitEmptyFieldArray()
    {
        Assert.Throws<ArgumentException>(() => new InquirySelectAllByFieldAttribute(System.Array.Empty<string>()));
    }

    [Fact]
    public void SelectByFieldAttributeRejectsWhitespaceField()
    {
        Assert.Throws<ArgumentException>(() => new InquirySelectAllByFieldAttribute("  "));
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

    [Fact]
    public void TableAttributeParameterlessLeavesNameNull()
    {
        var attribute = new InquiryTableAttribute();

        Assert.Null(attribute.Name);
        Assert.Null(attribute.Schema);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void TableAttributeRejectsBlankName(string name)
    {
        Assert.Throws<ArgumentException>(() => new InquiryTableAttribute(name));
    }

    [Fact]
    public void ColumnAttributeParameterlessLeavesNameNull()
    {
        var attribute = new InquiryColumnAttribute();

        Assert.Null(attribute.Name);
        Assert.False(attribute.UseDatabaseDefault);
    }

    [Fact]
    public void ColumnAttributeUseDatabaseDefaultIsSettable()
    {
        var attribute = new InquiryColumnAttribute { UseDatabaseDefault = true };

        Assert.True(attribute.UseDatabaseDefault);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ColumnAttributeRejectsBlankName(string name)
    {
        Assert.Throws<ArgumentException>(() => new InquiryColumnAttribute(name));
    }

    [Fact]
    public void KeyAttributeDefaultsIsGeneratedFalse()
    {
        var attribute = new InquiryKeyAttribute();

        Assert.False(attribute.IsGenerated);
        Assert.False(attribute.UseDatabaseDefault);
        Assert.Null(attribute.Name);
    }

    [Fact]
    public void KeyAttributeIsGeneratedIsSettable()
    {
        var attribute = new InquiryKeyAttribute { IsGenerated = true };

        Assert.True(attribute.IsGenerated);
    }

    [Fact]
    public void KeyAttributeAcceptsExplicitName()
    {
        var attribute = new InquiryKeyAttribute("Id");

        Assert.Equal("Id", attribute.Name);
    }

    [Fact]
    public void RelationAttributeStoresForeignKeyProperty()
    {
        var attribute = new InquiryRelationAttribute("CategoryKey");

        Assert.Equal("CategoryKey", attribute.ForeignKeyProperty);
    }

    [Fact]
    public void StoredProcedureAttributeStoresProcedureName()
    {
        var attribute = new InquiryStoredProcedureAttribute("usp_GetOrders");

        Assert.Equal("usp_GetOrders", attribute.ProcedureName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void StoredProcedureAttributeRejectsBlankName(string name)
    {
        Assert.Throws<ArgumentException>(() => new InquiryStoredProcedureAttribute(name));
    }

    [Fact]
    public void SelectByFieldAttributeRejectsBlankField()
    {
        Assert.Throws<ArgumentException>(() => new InquirySelectAllByFieldAttribute(""));
    }

    [Fact]
    public void MutationAttributesCanRequestReturnedEntity()
    {
        Assert.True(new InquiryInsertAttribute { ReturnEntity = true }.ReturnEntity);
        Assert.True(new InquiryUpdateAttribute { ReturnEntity = true }.ReturnEntity);
        Assert.True(new InquiryUpsertAttribute { ReturnEntity = true }.ReturnEntity);
    }
}
