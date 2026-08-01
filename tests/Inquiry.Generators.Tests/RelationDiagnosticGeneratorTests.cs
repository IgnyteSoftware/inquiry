namespace Inquiry.Generators.Tests;

/// <summary>
/// Relation-shape diagnostics are reported at declaration time — a mistyped foreign key (INQ040), a
/// reversed relation whose FK is on the wrong side (INQ058), or a composite-key child (INQ041) — so
/// they surface even when no store method eager-loads the relation.
/// </summary>
public sealed partial class InquiryGeneratorTests
{
    [Fact]
    public void MistypedRelationForeignKeyIsReportedEvenWithNoEagerMethod()
    {
        // No store and no eager method at all — previously this bad relation was silently skipped.
        const string source = """
            using System.Collections.Generic;
            using Inquiry.Entities;

            namespace Demo;

            [InquiryTable("Customer")]
            public sealed class Customer
            {
                [InquiryKey]
                public int Id { get; set; }
            }

            [InquiryTable("Order")]
            public sealed class Order
            {
                [InquiryKey]
                public int Id { get; set; }

                [InquiryColumn]
                public int? CustomerId { get; set; }

                // Typo: 'Custmr' is not a column on Order.
                [InquiryRelation("Custmr")]
                public Customer? Customer { get; set; }
            }
            """;

        var result = RunGenerator(source);
        Assert.All(result.RunResult.Results, static r => Assert.Null(r.Exception));
        Assert.Contains(result.RunResult.Diagnostics, d => d.Id == "INQ040");
    }

    [Fact]
    public void ReversedCollectionRelationReportsWrongSideINQ058()
    {
        // A collection relation's FK must be on the child (Order). Here the named property is a
        // column on the parent (Customer) instead — the relation is reversed.
        const string source = """
            using System.Collections.Generic;
            using Inquiry.Entities;

            namespace Demo;

            [InquiryTable("Customer")]
            public sealed class Customer
            {
                [InquiryKey]
                public int Id { get; set; }

                [InquiryColumn]
                public int RegionId { get; set; }

                [InquiryRelation("RegionId")]
                public List<Order> Orders { get; set; } = new();
            }

            [InquiryTable("Order")]
            public sealed class Order
            {
                [InquiryKey]
                public int Id { get; set; }

                [InquiryColumn]
                public int CustomerId { get; set; }
            }
            """;

        var result = RunGenerator(source);
        Assert.Contains(result.RunResult.Diagnostics, d => d.Id == "INQ058");
        // Not the generic unknown-FK error — the FK exists, just on the wrong side.
        Assert.DoesNotContain(result.RunResult.Diagnostics, d => d.Id == "INQ040");
    }

    [Fact]
    public void ValidRelationProducesNoRelationDiagnostics()
    {
        const string source = """
            using System.Collections.Generic;
            using Inquiry.Entities;

            namespace Demo;

            [InquiryTable("Customer")]
            public sealed class Customer
            {
                [InquiryKey]
                public int Id { get; set; }

                [InquiryRelation("CustomerId")]
                public List<Order> Orders { get; set; } = new();
            }

            [InquiryTable("Order")]
            public sealed class Order
            {
                [InquiryKey]
                public int Id { get; set; }

                [InquiryColumn]
                public int CustomerId { get; set; }
            }
            """;

        var result = RunGenerator(source);
        Assert.DoesNotContain(result.RunResult.Diagnostics, d => d.Id is "INQ040" or "INQ041" or "INQ058");
    }
}
