using System;

namespace Inquiry.Generators.Tests;

/// <summary>
/// Delete emission covers explicit table-wide deletion and collection predicates composed from
/// <c>[InquiryDelete]</c> with <c>[InquiryWhere]</c>.
/// </summary>
public sealed partial class InquiryGeneratorTests
{
    [Fact]
    public void DeleteAllEmitsExplicitTableWideDelete()
    {
        const string source = """
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry;
            using Inquiry.Entities;
            using Inquiry.Stores;

            namespace Demo;

            [InquiryTable("TThing")]
            public sealed class Thing
            {
                [InquiryKey(IsGenerated = true)]
                public long Id { get; set; }

                [InquiryColumn("Name")]
                public string Name { get; set; } = string.Empty;
            }

            public partial class ThingStore : Inquiry.Stores.InquiryStore<Demo.Thing>
            {
                [InquiryDeleteAll]
                public partial Task<int> DeleteAllAsync(CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        AssertNoErrors(result);
        var tree = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("ThingStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var text = tree.GetText().ToString();

        Assert.Contains("private const string _sqlDeleteAll_DeleteAllAsync = \"DELETE FROM \\\"TThing\\\"\";", text);
        Assert.Contains("return Inquiry.ExecuteAsync(new global::Inquiry.Commands.InquiryGeneratedCommand<byte>(_sqlDeleteAll_DeleteAllAsync", text);
        Assert.DoesNotContain("InquiryBatchCommand", text);
    }

    // #69: SQL Server uses TVPs for collection predicates — the SQL is constant and no per-element expansion is
    // needed, so the declared-length Size threading (#112) is superseded by the TVP binder.
    [Fact]
    public void DeleteAllOnSqlServerUsesTvpBinderNotExpansion()
    {
        const string source = """
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry.Entities;
            using Inquiry.Stores;

            namespace Demo;

            [InquiryTable("TThing")]
            public sealed class Thing
            {
                [InquiryKey("Code", Length = 64)]
                public string Code { get; set; } = string.Empty;
            }

            public partial class ThingStore : Inquiry.Stores.InquiryStore<Demo.Thing>
            {
                [InquiryDelete, InquiryWhere("Code", Compare.In)]
                public partial Task<int> DeleteAllAsync(IEnumerable<string> codes, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source, dialect: "SqlServer");
        AssertNoErrors(result);
        var tree = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("ThingStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var text = tree.GetText().ToString();

        Assert.Contains("[Code] IN (SELECT [Value] FROM @Code)", text);
        Assert.Contains("global::Inquiry.SqlServer.Parameters.InquiryTvpParameter.Bind(_c, \"@Code\", _args.Arg0", text);
        Assert.DoesNotContain("InquiryInExpansion", text);
    }

    [Theory]
    [InlineData("Sqlite")]
    [InlineData("SqlServer")]
    public void NullableDeleteAllCollectionIsNormalizedOnceWithoutNullabilityWarnings(string dialect)
    {
        const string source = """
            #nullable enable
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry.Entities;
            using Inquiry.Stores;

            namespace Demo;

            [InquiryTable("TThing")]
            public sealed class Thing
            {
                [InquiryKey]
                public long? Id { get; set; }
            }

            public partial class ThingStore : InquiryStore<Thing>
            {
                [InquiryDelete, InquiryWhere("Id", Compare.In)]
                public partial Task<int> DeleteAllAsync(IEnumerable<long?>? ids, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source, dialect: dialect);
        AssertNoErrors(result);
        Assert.DoesNotContain(result.Compilation.GetDiagnostics(), static diagnostic => diagnostic.Id == "CS8604");
        var text = Assert.Single(result.RunResult.GeneratedTrees,
            static tree => tree.FilePath.EndsWith("ThingStore.InquiryStore.g.cs", StringComparison.Ordinal)).GetText().ToString();

        Assert.Contains("_sqlDeleteWhere_DeleteAllAsync", text);
        if (dialect == "SqlServer")
        {
            Assert.Contains("?? throw new global::System.InvalidOperationException(\"SQL Server TVP descriptor resolution returned null.\")", text);
        }
    }

    [Fact]
    public void OracleDeleteWithInPredicateUsesJsonTableBinding()
    {
        const string source = """
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry;
            using Inquiry.Entities;
            using Inquiry.Stores;

            namespace Demo;

            [InquiryTable("TThing")]
            public sealed class Thing
            {
                [InquiryKey(IsGenerated = true)]
                public long Id { get; set; }

                [InquiryColumn("Name")]
                public string Name { get; set; } = string.Empty;
            }

            public partial class ThingStore : Inquiry.Stores.InquiryStore<Demo.Thing>
            {
                [InquiryDelete, InquiryWhere("Id", Compare.In)]
                public partial Task<int> DeleteAllAsync(IEnumerable<long> ids, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source, dialect: "Oracle");
        AssertNoErrors(result);
        var tree = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("ThingStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var text = tree.GetText().ToString();

        Assert.Contains("private const string _sqlDeleteWhere_DeleteAllAsync = \"DELETE FROM TThing WHERE Id IN (SELECT jt.val FROM JSON_TABLE(:iq1$Idxxxx$30d4cf864d6e68, '$[*]' COLUMNS(val NUMBER(19) PATH '$')) jt)\";", text);
        Assert.Contains("InquiryJsonArrayParameter.Bind(_c, \":iq1$Idxxxx$30d4cf864d6e68\", _args.Arg0);", text);
        Assert.DoesNotContain("ArrayBindCount", text);
    }

    [Fact]
    public void OracleStringDeleteWithInPredicateUsesJsonTableBinding()
    {
        const string source = """
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry.Entities;
            using Inquiry.Stores;

            namespace Demo;

            [InquiryTable("TThing")]
            public sealed class Thing
            {
                [InquiryKey(Length = 64)] public string Code { get; set; } = string.Empty;
            }

            public partial class ThingStore : InquiryStore<Thing>
            {
                [InquiryDelete, InquiryWhere("Code", Compare.In)]
                public partial Task<int> DeleteAllAsync(IEnumerable<string> codes, CancellationToken ct = default);
            }
            """;

        var result = RunGenerator(source, dialect: "Oracle");
        AssertNoErrors(result);
        var tree = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("ThingStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var text = tree.GetText().ToString();

        Assert.Contains("WHERE Code IN (SELECT jt.val FROM JSON_TABLE(:iq1$Codexx$", text);
        Assert.Contains("InquiryJsonArrayParameter.Bind(_c, \":iq1$Codexx$", text);
        Assert.DoesNotContain("ArrayBindSize", text);
    }

    [Fact]
    public void DeleteAllOnSoftDeleteEntityEmitsUpdateForm()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry;
            using Inquiry.Entities;
            using Inquiry.Stores;

            namespace Demo;

            [InquiryTable("TDoc")]
            public sealed class Doc
            {
                [InquiryKey(IsGenerated = true)]
                public long Id { get; set; }

                [InquiryColumn("IsDeleted"), InquirySoftDelete]
                public bool IsDeleted { get; set; }

                [InquiryColumn("TenantId"), InquiryGlobalFilter(ContextKey = "TenantId")]
                public long TenantId { get; set; }
            }

            public partial class DocStore : Inquiry.Stores.InquiryStore<Demo.Doc>
            {
                [InquiryDeleteAll]
                public partial Task<int> DeleteAllAsync(CancellationToken cancellationToken = default);

                [InquiryHardDeleteAll]
                public partial Task<int> PurgeAllAsync(CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        AssertNoErrors(result);
        var tree = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("DocStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var text = tree.GetText().ToString();

        Assert.Contains("_sqlDeleteAll_DeleteAllAsync = \"UPDATE \\\"TDoc\\\" SET \\\"IsDeleted\\\" = 1 WHERE \\\"IsDeleted\\\" = 0\";", text);
        Assert.Contains("_sqlDeleteAll_PurgeAllAsync = \"DELETE FROM \\\"TDoc\\\"\";", text);
        Assert.DoesNotContain("__BindGlobalFiltersWrite", text);
    }

    [Theory]
    [InlineData("_keys")]
    [InlineData("_c")]
    public void DeleteAllUserParameterCannotCollideWithGeneratedBinderNames(string parameterName)
    {
        var source = $$"""
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry.Entities;
            using Inquiry.Stores;

            namespace Demo;

            [InquiryTable("TThing")]
            public sealed class Thing
            {
                [InquiryKey]
                public long Id { get; set; }
            }

            public partial class ThingStore : InquiryStore<Thing>
            {
                [InquiryDelete, InquiryWhere("Id", Compare.In)]
                public partial Task<int> DeleteAllAsync(IEnumerable<long> {{parameterName}}, CancellationToken ct = default);
            }
            """;

        var result = RunGenerator(source);
        AssertNoErrors(result);
        var tree = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("ThingStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var text = tree.GetText().ToString();

        Assert.Contains("InquiryJsonArrayParameter.Bind(_c, \"@Id\", _args.Arg0);", text);
        Assert.DoesNotContain("var _keys =", text);
        Assert.DoesNotContain("var _c =", text);
    }
}
