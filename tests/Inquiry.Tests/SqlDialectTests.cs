namespace Inquiry.Tests;

public sealed class SqlDialectTests
{
    private readonly IInquiryEntityDescriptor<SqlUser> _descriptor = new InquiryMetadataRegistry().GetDescriptor<SqlUser>();

    [Fact]
    public void SqliteDialect_BuildsCrudCommands()
    {
        var factory = InquirySqliteProvider.Instance.CommandFactory;

        Assert.Equal(
            "SELECT \"id\", \"email\", \"display_name\", \"version\" FROM \"public\".\"users\" WHERE \"id\" = @Id",
            factory.BuildFind(_descriptor).CommandText);
        Assert.Equal(
            "INSERT INTO \"public\".\"users\" (\"id\", \"email\", \"display_name\", \"version\") VALUES (@Id, @Email, @DisplayName, @Version)",
            factory.BuildInsert(_descriptor).CommandText);
        Assert.Equal(
            "UPDATE \"public\".\"users\" SET \"email\" = @Email, \"display_name\" = @DisplayName WHERE \"id\" = @Id AND \"version\" = @Version",
            factory.BuildUpdate(_descriptor).CommandText);
        Assert.Equal(
            "DELETE FROM \"public\".\"users\" WHERE \"id\" = @Id AND \"version\" = @Version",
            factory.BuildDelete(_descriptor).CommandText);
    }

    [Fact]
    public void SqlServerDialect_QuotesIdentifiersAndUsesOffsetFetch()
    {
        var query = new InquiryQuery<SqlUser>()
            .Where("[email] = @email", new { email = "a@example.com" })
            .OrderBy("[email]")
            .Limit(10)
            .Offset(5);

        var sql = InquirySqlServerProvider.Instance.CommandFactory.BuildSelect(_descriptor, query).CommandText;

        Assert.Equal(
            "SELECT [id], [email], [display_name], [version] FROM [public].[users] WHERE [email] = @email ORDER BY [email] OFFSET 5 ROWS FETCH NEXT 10 ROWS ONLY",
            sql);
    }

    [InquiryTable("users", Schema = "public")]
    internal sealed class SqlUser
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
    }
}
