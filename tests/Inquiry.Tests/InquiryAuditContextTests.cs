using System.Threading.Tasks;
using Inquiry;
using Xunit;

namespace Inquiry.Tests;

/// <summary>
/// <see cref="InquiryAuditContext"/>: the ambient user is null by default, a scope sets it and
/// restores the previous value on dispose (nesting included), and the value flows across awaits.
/// </summary>
public sealed class InquiryAuditContextTests
{
    [Fact]
    public void CurrentUserIsNullByDefault()
    {
        Assert.Null(InquiryAuditContext.CurrentUser);
    }

    [Fact]
    public void ScopeSetsAndRestoresUser()
    {
        Assert.Null(InquiryAuditContext.CurrentUser);
        using (InquiryAuditContext.BeginScope("alice"))
        {
            Assert.Equal("alice", InquiryAuditContext.CurrentUser);

            using (InquiryAuditContext.BeginScope("bob"))
            {
                Assert.Equal("bob", InquiryAuditContext.CurrentUser);
            }

            // Nested scope restored the outer value.
            Assert.Equal("alice", InquiryAuditContext.CurrentUser);
        }

        Assert.Null(InquiryAuditContext.CurrentUser);
    }

    [Fact]
    public async Task UserFlowsAcrossAwaits()
    {
        using (InquiryAuditContext.BeginScope("carol"))
        {
            await Task.Yield();
            Assert.Equal("carol", InquiryAuditContext.CurrentUser);
            await Task.Delay(1);
            Assert.Equal("carol", InquiryAuditContext.CurrentUser);
        }
    }
}
