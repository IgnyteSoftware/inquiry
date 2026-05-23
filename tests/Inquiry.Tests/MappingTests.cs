namespace Inquiry.Tests;

public sealed class MappingTests
{
    [Fact]
    public void ReflectionDescriptor_ReadsAttributeMapping()
    {
        var registry = new InquiryMetadataRegistry();

        var descriptor = registry.GetDescriptor<TestUser>();

        Assert.Equal("users", descriptor.TableName);
        Assert.Equal("public", descriptor.Schema);
        Assert.Equal(4, descriptor.Properties.Count);
        Assert.Single(descriptor.Keys);
        Assert.Equal("Id", descriptor.Keys[0].PropertyName);
        Assert.Equal("id", descriptor.Keys[0].ColumnName);
        Assert.DoesNotContain(descriptor.Properties, property => property.PropertyName == nameof(TestUser.NotMapped));
    }

    [Fact]
    public void ReflectionDescriptor_DetectsDuplicateColumns()
    {
        var registry = new InquiryMetadataRegistry();

        var ex = Assert.Throws<InquiryMappingException>(() => registry.GetDescriptor<DuplicateColumnEntity>());

        Assert.Contains("duplicate", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("same", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReflectionDescriptor_DetectsConflictingColumnAndIgnore()
    {
        var registry = new InquiryMetadataRegistry();

        var ex = Assert.Throws<InquiryMappingException>(() => registry.GetDescriptor<ConflictingMappingEntity>());

        Assert.Contains("InquiryIgnore", ex.Message, StringComparison.Ordinal);
        Assert.Contains("InquiryColumn", ex.Message, StringComparison.Ordinal);
    }

    [InquiryTable("users", Schema = "public")]
    internal sealed class TestUser
    {
        [InquiryKey]
        [InquiryColumn("id")]
        public Guid Id { get; set; }

        [InquiryColumn("email")]
        public string Email { get; set; } = string.Empty;

        [InquiryColumn("display_name")]
        public string? DisplayName { get; set; }

        [InquiryConcurrencyToken]
        [InquiryColumn("version")]
        public int Version { get; set; }

        [InquiryIgnore]
        public string? NotMapped { get; set; }
    }

    [InquiryTable("duplicates")]
    internal sealed class DuplicateColumnEntity
    {
        [InquiryKey]
        [InquiryColumn("same")]
        public int Id { get; set; }

        [InquiryColumn("same")]
        public string Name { get; set; } = string.Empty;
    }

    [InquiryTable("conflicts")]
    internal sealed class ConflictingMappingEntity
    {
        [InquiryColumn("id")]
        [InquiryIgnore]
        public int Id { get; set; }
    }
}
