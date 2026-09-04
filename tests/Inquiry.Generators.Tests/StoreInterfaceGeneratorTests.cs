using Microsoft.CodeAnalysis;

namespace Inquiry.Generators.Tests;

/// <summary>
/// Opt-in store interface generation tests: <c>[InquiryGenerateInterface]</c> emits a
/// <c>public partial interface I{Store}</c> mirroring the generator-implemented method signatures
/// (with default parameter values), declares the generated partial class as implementing it, and
/// registers the interface in DI as a scoped forward to the concrete store. Without the attribute
/// nothing interface-related is emitted.
/// </summary>
public sealed partial class InquiryGeneratorTests
{
    private const string InterfaceStoreSource = """
        using System;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using Inquiry;
        using Inquiry.Entities;
        using Inquiry.Stores;

        namespace Demo;

        [InquiryTable("TCustomer")]
        public sealed class Customer
        {
            [InquiryKey]
            public Guid Key { get; set; } = Guid.NewGuid();

            [InquiryColumn("Name")]
            public string Name { get; set; } = string.Empty;

            [InquiryColumn]
            public bool IsActive { get; set; } = true;
        }

        [InquiryGenerateInterface]
        public partial class CustomerStore : InquiryStore<Customer>
        {
            [InquirySelectAll]
            public partial IAsyncEnumerable<Customer> SelectAllAsync(CancellationToken cancellationToken = default);

            [InquirySelectOneByKey]
            public partial Task<Customer?> SelectByKeyAsync(Guid key, CancellationToken cancellationToken = default);

            [InquirySelectAllByField("IsActive")]
            public partial IAsyncEnumerable<Customer> SelectByIsActiveAsync(bool isActive = true, CancellationToken cancellationToken = default);

            [InquiryInsert]
            public partial Task<int> InsertAsync(Customer customer, CancellationToken cancellationToken = default);

            [InquiryDelete]
            public partial Task<bool> DeleteByKeyAsync(Guid key, CancellationToken cancellationToken = default);
        }
        """;

    [Fact]
    public void GenerateInterfaceEmitsInterfaceWithMethodSignatures()
    {
        var result = RunGenerator(InterfaceStoreSource);
        AssertNoErrors(result);

        var tree = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("CustomerStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var text = tree.GetText().ToString();

        Assert.Contains("public partial interface ICustomerStore", text);

        // The interface carries the full signatures, including default parameter values (defaults
        // live on the user's partial declaration, so the implementation half must not repeat them —
        // but interface call-sites keep their optional arguments).
        Assert.Contains("global::System.Collections.Generic.IAsyncEnumerable<global::Demo.Customer> SelectAllAsync(global::System.Threading.CancellationToken cancellationToken = default);", text);
        Assert.Contains("global::System.Threading.Tasks.Task<global::Demo.Customer?> SelectByKeyAsync(global::System.Guid key, global::System.Threading.CancellationToken cancellationToken = default);", text);
        Assert.Contains("global::System.Collections.Generic.IAsyncEnumerable<global::Demo.Customer> SelectByIsActiveAsync(bool isActive = true, global::System.Threading.CancellationToken cancellationToken = default);", text);
        Assert.Contains("global::System.Threading.Tasks.Task<int> InsertAsync(global::Demo.Customer customer, global::System.Threading.CancellationToken cancellationToken = default);", text);
        Assert.Contains("global::System.Threading.Tasks.Task<bool> DeleteByKeyAsync(global::System.Guid key, global::System.Threading.CancellationToken cancellationToken = default);", text);
    }

    [Fact]
    public void GenerateInterfacePartialClassImplementsInterface()
    {
        var result = RunGenerator(InterfaceStoreSource);
        AssertNoErrors(result);

        var tree = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("CustomerStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var text = tree.GetText().ToString();

        Assert.Contains("partial class CustomerStore : ICustomerStore", text);

        // AssertNoErrors above already proves the partial class satisfies the interface (a missing
        // member would be a compile error in the output compilation); also confirm via the symbol.
        var store = result.Compilation.GetTypeByMetadataName("Demo.CustomerStore");
        Assert.NotNull(store);
        Assert.Contains(store!.Interfaces, static i => i.ToDisplayString() == "Demo.ICustomerStore");
    }

    [Fact]
    public void GenerateInterfaceRegistersInterfaceForwardInDependencyInjection()
    {
        var result = RunGenerator(InterfaceStoreSource);
        AssertNoErrors(result);

        var tree = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("InquiryGeneratedServiceRegistration.g.cs", StringComparison.Ordinal));
        var text = tree.GetText().ToString();

        // The concrete store registration is unchanged; the interface forwards to it so both
        // resolutions share the one scoped instance.
        Assert.Contains("global::Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.TryAddScoped<global::Demo.CustomerStore>(services);", text);
        Assert.Contains("global::Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.TryAddScoped<global::Demo.ICustomerStore>(services, static sp => global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<global::Demo.CustomerStore>(sp));", text);
    }

    [Fact]
    public void NoInterfaceEmittedWithoutAttribute()
    {
        var result = RunGenerator(InterfaceStoreSource.Replace("[InquiryGenerateInterface]", string.Empty));
        AssertNoErrors(result);

        var storeTree = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("CustomerStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var storeText = storeTree.GetText().ToString();

        Assert.DoesNotContain("ICustomerStore", storeText);
        Assert.DoesNotContain("interface", storeText);

        var servicesTree = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("InquiryGeneratedServiceRegistration.g.cs", StringComparison.Ordinal));
        var servicesText = servicesTree.GetText().ToString();

        Assert.Contains("TryAddScoped<global::Demo.CustomerStore>(services);", servicesText);
        Assert.DoesNotContain("ICustomerStore", servicesText);
    }
}
