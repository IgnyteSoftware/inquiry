namespace Inquiry.Tests;

/// <summary>
/// Ambient-scope semantics for <see cref="InquiryFilterContext"/> (#82 phase B): values flow with the
/// async context, scopes restore their predecessor on dispose, and every missing-value shape throws
/// <see cref="InquiryFilterValueMissingException"/> — the binder contract is fail-loud-before-execute,
/// never bind-null-and-return-nothing.
/// </summary>
public sealed class InquiryFilterContextTests
{
    [Fact]
    public void GetRequiredReturnsTheScopedValue()
    {
        using var _ = InquiryFilterContext.BeginScope(new Dictionary<string, object> { ["TenantId"] = 42L });

        Assert.Equal(42L, InquiryFilterContext.GetRequired<long>("TenantId"));
    }

    [Fact]
    public void ScopesNestAndRestoreThePreviousValuesOnDispose()
    {
        using var outer = InquiryFilterContext.BeginScope(new Dictionary<string, object> { ["TenantId"] = 1L });
        using (InquiryFilterContext.BeginScope(new Dictionary<string, object> { ["TenantId"] = 2L }))
        {
            Assert.Equal(2L, InquiryFilterContext.GetRequired<long>("TenantId"));
        }

        Assert.Equal(1L, InquiryFilterContext.GetRequired<long>("TenantId"));
    }

    [Fact]
    public async Task ValuesFlowAcrossAwaitAndAreIsolatedPerAsyncFlow()
    {
        using var _ = InquiryFilterContext.BeginScope(new Dictionary<string, object> { ["TenantId"] = 7L });
        await Task.Yield();
        Assert.Equal(7L, InquiryFilterContext.GetRequired<long>("TenantId"));

        // A sibling task that opens its own scope must not leak it back into this flow.
        await Task.Run(() =>
        {
            using var inner = InquiryFilterContext.BeginScope(new Dictionary<string, object> { ["TenantId"] = 9L });
            Assert.Equal(9L, InquiryFilterContext.GetRequired<long>("TenantId"));
        });

        Assert.Equal(7L, InquiryFilterContext.GetRequired<long>("TenantId"));
    }

    [Fact]
    public void NoScopeThrowsTheDedicatedException()
    {
        var exception = Assert.Throws<InquiryFilterValueMissingException>(
            static () => InquiryFilterContext.GetRequired<long>("TenantId"));
        Assert.Contains("TenantId", exception.Message);
    }

    [Fact]
    public void MissingKeyAndTypeMismatchThrowTheDedicatedException()
    {
        using var _ = InquiryFilterContext.BeginScope(new Dictionary<string, object> { ["Other"] = 1L, ["TenantId"] = "not-a-long" });

        Assert.Throws<InquiryFilterValueMissingException>(static () => InquiryFilterContext.GetRequired<long>("Absent"));
        var mismatch = Assert.Throws<InquiryFilterValueMissingException>(static () => InquiryFilterContext.GetRequired<long>("TenantId"));
        // The message names the key and the types — never the value itself.
        Assert.DoesNotContain("not-a-long", mismatch.Message);
    }
}
