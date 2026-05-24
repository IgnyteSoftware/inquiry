using Inquiry.Entities;

namespace Inquiry.Sample.Models;

[InquiryTable("TOrganizationToUser")]
public sealed class OrganizationToUser
{
    [InquiryKey]
    public Guid Key { get; set; } = Guid.NewGuid();

    [InquiryForeignKey("TOrganizationKey", "TOrganization", "Key")]
    public Guid OrganizationKey { get; set; }

    [InquiryForeignKey("TUserKey", "TUser", "Key")]
    public Guid UserKey { get; set; }

    [InquiryColumn]
    public bool IsActive { get; set; } = true;
}
