using Inquiry.Entities;

namespace Inquiry.Sample.Models;

[InquiryTable("TUser")]
public sealed class User
{
    [InquiryKey]
    public Guid Key { get; set; } = Guid.NewGuid();

    [InquiryColumn]
    public string FirstName { get; set; } = string.Empty;

    [InquiryColumn]
    public string LastName { get; set; } = string.Empty;

    [InquiryColumn]
    public string Email { get; set; } = string.Empty;
}
