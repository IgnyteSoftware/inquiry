using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.DependencyInjection;

namespace Inquiry.Generators.Tests;

/// <summary>
/// Layered generating libraries (#607): a library that generates stores and references another
/// library that also generates stores. Each one registers its stores through its own wrapper, and a
/// host that references both calls both wrappers.
/// </summary>
public sealed partial class InquiryGeneratorTests
{
    private const string LayeredCoreSource = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Inquiry;
        using Inquiry.DependencyInjection;
        using Inquiry.Entities;
        using Inquiry.Stores;
        using Microsoft.Extensions.DependencyInjection;

        [assembly: InquiryDialect("Sqlite")]

        namespace Core;

        [InquiryTable("TOrder")]
        public sealed class Order
        {
            [InquiryKey]
            public Guid Key { get; set; }
        }

        public partial class OrderStore : InquiryStore<Order>
        {
            [InquirySelectOneByKey]
            public partial Task<Order?> SelectByKeyAsync(Guid key, CancellationToken cancellationToken = default);
        }

        public static class CoreRegistration
        {
            public static IServiceCollection AddCoreData(this IServiceCollection services)
                => services.AddInquiryGeneratedStores();
        }
        """;

    private const string LayeredWebSource = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Inquiry;
        using Inquiry.DependencyInjection;
        using Inquiry.Entities;
        using Inquiry.Stores;
        using Microsoft.Extensions.DependencyInjection;

        [assembly: InquiryDialect("Sqlite")]

        namespace Web;

        [InquiryTable("TAudit")]
        public sealed class Audit
        {
            [InquiryKey]
            public Guid Key { get; set; }

            [InquiryColumn]
            public Guid OrderKey { get; set; }
        }

        public partial class AuditStore : InquiryStore<Audit>
        {
            [InquirySelectOneByKey]
            public partial Task<Audit?> SelectByKeyAsync(Guid key, CancellationToken cancellationToken = default);
        }

        public static class WebRegistration
        {
            // Uses a type from the core library so the reference cannot be dropped as unused.
            public static Type CoreStoreType => typeof(Core.OrderStore);

            public static IServiceCollection AddWebData(this IServiceCollection services)
                => services.AddInquiryGeneratedStores();
        }
        """;

    private const string LayeredHostSource = """
        using Core;
        using Microsoft.Extensions.DependencyInjection;
        using Web;

        namespace Host;

        public static class Composition
        {
            public static IServiceCollection Compose(IServiceCollection services)
                => services.AddCoreData().AddWebData();

            public static IServiceCollection ComposeWebOnly(IServiceCollection services)
                => services.AddWebData();
        }
        """;

    [Fact]
    public void LayeredGeneratingLibrariesRegisterTheirOwnStoresWithWarningsAsErrors()
    {
        var core = CompileLayeredProject("Layered.Core", LayeredCoreSource, runGenerator: true);
        var web = CompileLayeredProject("Layered.Web", LayeredWebSource, runGenerator: true, core.Reference);
        var host = CompileLayeredProject("Layered.Host", LayeredHostSource, runGenerator: false, core.Reference, web.Reference);

        var context = new AssemblyLoadContext("LayeredRegistration", isCollectible: true);
        try
        {
            context.LoadFromStream(new MemoryStream(core.Image));
            context.LoadFromStream(new MemoryStream(web.Image));
            var hostAssembly = context.LoadFromStream(new MemoryStream(host.Image));

            var composition = hostAssembly.GetType("Host.Composition", throwOnError: true)!;

            var serviceTypes = ComposeServiceTypes(composition, "Compose");
            Assert.Contains("Core.OrderStore", serviceTypes);
            Assert.Contains("Web.AuditStore", serviceTypes);

            var webOnlyServiceTypes = ComposeServiceTypes(composition, "ComposeWebOnly");
            Assert.Contains("Web.AuditStore", webOnlyServiceTypes);
            Assert.DoesNotContain("Core.OrderStore", webOnlyServiceTypes);
        }
        finally
        {
            context.Unload();
        }
    }

    [Fact]
    public void GeneratedStoreRegistrationIsNotVisibleToReferencingAssemblies()
    {
        var core = CompileLayeredProject("Layered.Core", LayeredCoreSource, runGenerator: true);

        const string hostSource = """
            using Inquiry.DependencyInjection;
            using Microsoft.Extensions.DependencyInjection;

            public static class Composition
            {
                public static IServiceCollection Compose(IServiceCollection services)
                    => services.AddInquiryGeneratedStores();
            }
            """;
        var compilation = CSharpCompilation.Create(
            "Layered.LeakCheck",
            [CSharpSyntaxTree.ParseText(hostSource)],
            [.. GetReferences(), core.Reference],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var diagnostic = Assert.Single(compilation.GetDiagnostics(), static d => d.Severity == DiagnosticSeverity.Error);
        Assert.Equal("CS1061", diagnostic.Id);
    }

    private static List<string?> ComposeServiceTypes(Type composition, string methodName)
    {
        var method = composition.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static)!;
        var services = (IServiceCollection)method.Invoke(null, [new ServiceCollection()])!;
        return services.Select(static descriptor => descriptor.ServiceType.FullName).ToList();
    }

    private static (MetadataReference Reference, byte[] Image) CompileLayeredProject(
        string assemblyName,
        string source,
        bool runGenerator,
        params MetadataReference[] projectReferences)
    {
        // GeneralDiagnosticOption.Error mirrors TreatWarningsAsErrors, which turns CS0436 into a build failure.
        var options = new CSharpCompilationOptions(
            OutputKind.DynamicallyLinkedLibrary,
            nullableContextOptions: NullableContextOptions.Enable,
            generalDiagnosticOption: ReportDiagnostic.Error);
        Compilation compilation = CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source)],
            [.. GetReferences(), .. projectReferences],
            options);

        if (runGenerator)
        {
            GeneratorDriver driver = CSharpGeneratorDriver.Create(
                new global::Inquiry.Sqlite.Analyzer.InquirySqliteGenerator().AsSourceGenerator());
            driver.RunGeneratorsAndUpdateCompilation(compilation, out compilation, out var generatorDiagnostics);
            Assert.Empty(generatorDiagnostics);
        }

        using var stream = new MemoryStream();
        var emitResult = compilation.Emit(stream);
        Assert.True(
            emitResult.Success,
            $"{assemblyName} failed to compile:{Environment.NewLine}{string.Join(Environment.NewLine, emitResult.Diagnostics)}");

        var image = stream.ToArray();
        return (MetadataReference.CreateFromImage(image), image);
    }
}
