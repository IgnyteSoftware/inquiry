namespace Inquiry.Entities;

/// <summary>Specifies the action taken when a referenced key is deleted or updated.</summary>
public enum InquiryReferentialAction
{
    /// <summary>Uses the provider's default blocking behavior.</summary>
    NoAction = 0,
    /// <summary>Rejects the referenced-key change immediately.</summary>
    Restrict = 1,
    /// <summary>Propagates the referenced-key change.</summary>
    Cascade = 2,
    /// <summary>Sets the local foreign-key value to null.</summary>
    SetNull = 3,
    /// <summary>Sets the local foreign-key value to its declared default.</summary>
    SetDefault = 4,
}
