using Inquiry.Entities;
using System;
using System.Linq;

namespace Inquiry.FeatureCatalog;

[InquiryTable("CyclicAlpha")]
public sealed class CyclicAlpha
{
    [InquiryKey]
    public long Id { get; set; }

    [InquiryForeignKey("BetaId", "CyclicBeta", "Id")]
    public long? BetaId { get; set; }

    [InquiryForeignKey("ParentId", "CyclicAlpha", "Id")]
    public long? ParentId { get; set; }
}

[InquiryTable("CyclicBeta")]
public sealed class CyclicBeta
{
    [InquiryKey]
    public long Id { get; set; }

    [InquiryForeignKey("AlphaId", "CyclicAlpha", "Id")]
    public long? AlphaId { get; set; }
}

public static class CyclicForeignKeyDdl
{
    public static string Extract(string generatedDdl)
        => string.Join("\n\n", generatedDdl
            .Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Where(static statement =>
                statement.Contains("CyclicAlpha", StringComparison.Ordinal)
                || statement.Contains("CyclicBeta", StringComparison.Ordinal)));
}
