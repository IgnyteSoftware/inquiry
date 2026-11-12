namespace Inquiry.Generators.Tests;

public sealed partial class InquiryGeneratorTests
{
    [Fact]
    public void SqlServerOutputTableUsesTheDeclaredSqlType()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry.Entities;
            using Inquiry.Stores;

            namespace Demo;

            [InquiryTable("TInvoice", GenerateDdl = false)]
            public sealed class Invoice
            {
                [InquiryKey(IsGenerated = true)]
                public int Id { get; set; }

                [InquiryColumn(SqlType = "decimal(19,4)")]
                public decimal Amount { get; set; }

                [InquiryColumn]
                public decimal Tax { get; set; }

                [InquiryColumn(SqlType = " RowVersion ", UseDatabaseDefault = true)]
                public byte[] Stamp { get; set; } = System.Array.Empty<byte>();
            }

            public partial class InvoiceStore : Inquiry.Stores.InquiryStore<Demo.Invoice>
            {
                [InquiryInsert]
                public partial Task<Invoice?> InsertAsync(Invoice invoice, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source, dialect: "SqlServer");
        AssertNoErrors(result);
        var tree = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("InvoiceStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var text = tree.GetText().ToString();

        // The inferred DECIMAL(18,2) would round Amount on its way back to the caller. A rowversion
        // column in a table variable cannot receive a value, so it stays BINARY(8).
        Assert.Contains("DECLARE @_out TABLE ([Id] INT, [Amount] decimal(19,4), [Tax] DECIMAL(18,2), [Stamp] BINARY(8));", text);
    }
}
