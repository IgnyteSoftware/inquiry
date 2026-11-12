namespace Inquiry.Generators.Tests;

public sealed partial class InquiryGeneratorTests
{
    [Fact]
    public void StoreThatRepeatsItsBaseListOnTwoDeclarationsIsGeneratedOnce()
    {
        const string source = """
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry.Entities;
            using Inquiry.Stores;

            namespace Demo;

            [InquiryTable("TOrder")]
            public sealed class Order
            {
                [InquiryKey]
                public long Id { get; set; }

                [InquiryColumn]
                public string Name { get; set; } = string.Empty;
            }

            public partial class OrderStore : InquiryStore<Order>
            {
                [InquirySelectOneByKey]
                public partial Task<Order?> GetAsync(long id, CancellationToken cancellationToken = default);
            }

            public partial class OrderStore : InquiryStore<Order>
            {
                [InquiryInsert]
                public partial Task<int> InsertAsync(Order order, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);

        // Two outputs with one hint name stop the generator (CS8785) and remove all generated code.
        AssertNoErrors(result);
        var store = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("OrderStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var text = store.GetText().ToString();
        Assert.Contains("GetAsync(", text);
        Assert.Contains("InsertAsync(", text);
    }
}
