namespace Inquiry.Generators.Tests;

/// <summary>
/// <c>[InquiryUpdate(StampModified = true)]</c> on a set-based update writes the entity's
/// <c>[InquiryModifiedAt]</c> and <c>[InquiryModifiedBy]</c> columns without method parameters.
/// Without the opt-in the generated SQL is unchanged.
/// </summary>
public sealed partial class InquiryGeneratorTests
{
    private static string StampedMemberSource(string methods) => $$"""
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Inquiry;
        using Inquiry.Entities;
        using Inquiry.Stores;

        namespace Demo;

        [InquiryTable("Member")]
        public sealed class Member
        {
            [InquiryKey]
            public long Id { get; set; }

            [InquiryColumn]
            public string Status { get; set; } = string.Empty;

            [InquiryColumn]
            public int Score { get; set; }

            [InquiryModifiedAt]
            public DateTime TStamp { get; set; }

            [InquiryModifiedBy]
            public string? LastUpdatedBy { get; set; }
        }

        public partial class MemberStore : InquiryStore<Member>
        {
        {{methods}}
        }
        """;

    private static string GetMemberStore(GeneratorTestResult result)
    {
        var tree = Assert.Single(
            result.RunResult.GeneratedTrees,
            static t => t.FilePath.EndsWith("MemberStore.InquiryStore.g.cs", StringComparison.Ordinal));
        return tree.GetText().ToString();
    }

    [Fact]
    public void StampModifiedSetBasedUpdateStampsModifiedColumns()
    {
        var result = RunGenerator(StampedMemberSource("""
            [InquiryUpdate(StampModified = true)]
            [InquiryWhere(nameof(Member.Id))]
            public partial Task<int> SetStatusAsync(string status, long id, CancellationToken cancellationToken = default);
            """), dialect: "SqlServer");
        AssertNoErrors(result);
        var text = GetMemberStore(result);

        Assert.Contains(
            "_sqlUpdateWhere_SetStatusAsync = \"UPDATE [Member] SET [Status] = @Status, [TStamp] = @TStamp, [LastUpdatedBy] = @LastUpdatedBy WHERE [Id] = @Id\";",
            text);
        Assert.Contains("= global::System.DateTime.UtcNow;", text);
        Assert.Contains("= global::Inquiry.InquiryAuditContext.CurrentUser;", text);
        Assert.Contains("ParameterName = \"@TStamp\";", text);
        Assert.Contains("ParameterName = \"@LastUpdatedBy\";", text);
    }

    [Fact]
    public void SetBasedUpdateWithoutStampModifiedIsUnchanged()
    {
        const string method = """
            [InquiryUpdate]
            [InquiryWhere(nameof(Member.Id))]
            public partial Task<int> SetStatusAsync(string status, long id, CancellationToken cancellationToken = default);
            """;
        var result = RunGenerator(StampedMemberSource(method), dialect: "SqlServer");
        AssertNoErrors(result);
        var text = GetMemberStore(result);

        Assert.Contains("_sqlUpdateWhere_SetStatusAsync = \"UPDATE [Member] SET [Status] = @Status WHERE [Id] = @Id\";", text);
        Assert.DoesNotContain("InquiryAuditContext", text);
        Assert.DoesNotContain("@TStamp", text.Substring(text.IndexOf("SetStatusAsync(", StringComparison.Ordinal)));

        var explicitFalse = RunGenerator(
            StampedMemberSource(method.Replace("[InquiryUpdate]", "[InquiryUpdate(StampModified = false)]", StringComparison.Ordinal)),
            dialect: "SqlServer");
        AssertNoErrors(explicitFalse);
        Assert.Equal(text, GetMemberStore(explicitFalse));
    }

    [Fact]
    public void StampModifiedSetExpressionUpdateStampsModifiedColumns()
    {
        var result = RunGenerator(StampedMemberSource("""
            [InquiryUpdate(StampModified = true)]
            [InquirySet(nameof(Member.Score), "{Score} + @delta")]
            [InquiryWhere(nameof(Member.Id))]
            public partial Task<int> AddScoreAsync(int delta, long id, CancellationToken cancellationToken = default);
            """), dialect: "SqlServer");
        AssertNoErrors(result);
        var text = GetMemberStore(result);

        Assert.Contains(
            "_sqlUpdateWhere_AddScoreAsync = \"UPDATE [Member] SET [Score] = [Score] + @delta, [TStamp] = @TStamp, [LastUpdatedBy] = @LastUpdatedBy WHERE [Id] = @Id\";",
            text);
        Assert.Contains("ParameterName = \"@TStamp\";", text);
    }

    [Fact]
    public void StampModifiedRenamesFilterOnStampedColumn()
    {
        var result = RunGenerator(StampedMemberSource("""
            [InquiryUpdate(StampModified = true)]
            [InquiryWhere(nameof(Member.TStamp), Compare.LessThan)]
            public partial Task<int> ExpireAsync(string status, DateTime tStamp, CancellationToken cancellationToken = default);
            """), dialect: "SqlServer");
        AssertNoErrors(result);
        var text = GetMemberStore(result);

        Assert.Contains(
            "_sqlUpdateWhere_ExpireAsync = \"UPDATE [Member] SET [Status] = @Status, [TStamp] = @TStamp, [LastUpdatedBy] = @LastUpdatedBy WHERE [TStamp] < @TStamp2\";",
            text);
    }

    [Theory]
    [InlineData("""
        [InquiryUpdate(StampModified = true)]
        [InquiryWhere(nameof(Member.Id))]
        public partial Task<int> SetStatusAsync(string status, DateTime tStamp, long id, CancellationToken cancellationToken = default);
        """, "TStamp")]
    [InlineData("""
        [InquiryUpdate(StampModified = true)]
        [InquirySet(nameof(Member.LastUpdatedBy), "@user")]
        [InquiryWhere(nameof(Member.Id))]
        public partial Task<int> SetUserAsync(string? user, long id, CancellationToken cancellationToken = default);
        """, "LastUpdatedBy")]
    public void StampModifiedWithStampedColumnAssignmentReportsINQ118(string method, string column)
    {
        var result = RunGenerator(StampedMemberSource(method), dialect: "SqlServer");

        var diagnostic = Assert.Single(result.RunResult.Diagnostics, static d => d.Id == "INQ118");
        Assert.Contains($"'{column}'", diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }
}
