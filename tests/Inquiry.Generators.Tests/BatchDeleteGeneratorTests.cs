using System;

namespace Inquiry.Generators.Tests;

/// <summary>
/// Batch-delete emission: <c>[InquiryDeleteAll]</c> emits a <c>DELETE … WHERE key IN (@keys)</c>
/// const plus a binder that expands the key collection via <c>InquiryInExpansion</c>; a soft-delete
/// entity instead emits the soft-delete UPDATE form.
/// </summary>
public sealed partial class InquiryGeneratorTests
{
    [Fact]
    public void DeleteAllEmitsKeyInClauseAndJsonArrayBinder()
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
                public partial Task<int> DeleteAllAsync(IEnumerable<long> ids, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        AssertNoErrors(result);
        var tree = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("ThingStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var text = tree.GetText().ToString();

        Assert.Contains("private const string _sqlDeleteAll = \"DELETE FROM \\\"TThing\\\" WHERE \\\"Id\\\" IN (SELECT value FROM json_each(@keys))\";", text);
        Assert.Contains("private static readonly global::Inquiry.Commands.InquiryBatchCommand<", text);
        Assert.Contains("_batch_DeleteAllAsync_", text);
        Assert.Contains("static _ => _sqlDeleteAll,", text);
        Assert.Contains("static (_c, _keys) =>", text);
        Assert.Contains("parametersPerItem: 0,", text);
        Assert.Contains("global::Inquiry.Parameters.InquiryJsonArrayParameter.Bind(_c, \"@keys\", _keys);", text);
        Assert.Contains("return Inquiry.ExecuteBatchAsync(_batch_DeleteAllAsync_", text);
    }

    // #69: SQL Server now uses TVPs for batch deletes — the SQL is constant and no per-element expansion is
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
                [InquiryDeleteAll]
                public partial Task<int> DeleteAllAsync(IEnumerable<string> codes, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source, dialect: "SqlServer");
        AssertNoErrors(result);
        var tree = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("ThingStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var text = tree.GetText().ToString();

        Assert.Contains("[Code] IN (SELECT [Value] FROM @keys)", text);
        Assert.Contains("global::Inquiry.SqlServer.Parameters.InquiryTvpParameter.Bind(_c, \"@keys\", _keys, \"[dbo].[Inquiry_Tvp_f2eaaa262a5392ae45922f38ea30b9ed4c414a6e6c502340e41458a5e1eded0f]\", _inquiryTvpDescriptor_f2eaaa262a5392ae45922f38ea30b9ed4c414a6e6c502340e41458a5e1eded0f);", text);
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
                [InquiryDeleteAll]
                public partial Task<int> DeleteAllAsync(IEnumerable<long?>? ids, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source, dialect: dialect);
        AssertNoErrors(result);
        Assert.DoesNotContain(result.Compilation.GetDiagnostics(), static diagnostic => diagnostic.Id == "CS8604");
        var text = Assert.Single(result.RunResult.GeneratedTrees,
            static tree => tree.FilePath.EndsWith("ThingStore.InquiryStore.g.cs", StringComparison.Ordinal)).GetText().ToString();

        Assert.Contains("((global::System.Collections.Generic.IEnumerable<long?>?)ids) ?? global::System.Array.Empty<long?>()", text);
        Assert.DoesNotContain("Enumerable.ToList", text);
        if (dialect == "SqlServer")
        {
            Assert.Contains("?? throw new global::System.InvalidOperationException(\"SQL Server TVP descriptor resolution returned null.\")", text);
        }
    }

    [Fact]
    public void OracleDeleteAllUsesSingleKeySqlWithArrayBinding()
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
                public partial Task<int> DeleteAllAsync(IEnumerable<long> ids, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source, dialect: "Oracle");
        AssertNoErrors(result);
        var tree = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("ThingStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var text = tree.GetText().ToString();

        // The JSON_TABLE constant remains available for query/transport consistency, but Oracle's
        // production batch path uses its faster native DML array binding over the fixed key statement.
        Assert.Contains("private const string _sqlDeleteAll = \"DELETE FROM TThing WHERE Id IN (SELECT jt.val FROM JSON_TABLE(:iq1$keysxx$d6859d157d8d31, '$[*]' COLUMNS(val NUMBER(19) PATH '$')) jt)\";", text);
        Assert.Contains("private const string _sqlDeleteAllItem = \"DELETE FROM TThing WHERE Id = :iq1$Idxxxx$30d4cf864d6e68\";", text);
        Assert.Contains("((global::Oracle.ManagedDataAccess.Client.OracleCommand)_cmd).ArrayBindCount = _keys.Count;", text);
        Assert.Contains("_p.ParameterName = \"iq1$Idxxxx$30d4cf864d6e68\";", text);
        Assert.Contains("_p.Value = _values;", text);
        Assert.DoesNotContain("InquiryJsonArrayParameter.Bind(_c, \":iq1$keysxx$d6859d157d8d31\", ids);", text);
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
            }

            public partial class DocStore : Inquiry.Stores.InquiryStore<Demo.Doc>
            {
                [InquiryDeleteAll]
                public partial Task<int> DeleteAllAsync(IEnumerable<long> ids, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        AssertNoErrors(result);
        var tree = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("DocStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var text = tree.GetText().ToString();

        Assert.Contains("_sqlDeleteAll = \"UPDATE \\\"TDoc\\\" SET \\\"IsDeleted\\\" = 1 WHERE \\\"Id\\\" IN (SELECT value FROM json_each(@keys))\";", text);
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
                [InquiryDeleteAll]
                public partial Task<int> DeleteAllAsync(IEnumerable<long> {{parameterName}}, CancellationToken ct = default);
            }
            """;

        var result = RunGenerator(source);
        AssertNoErrors(result);
        var tree = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("ThingStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var text = tree.GetText().ToString();

        Assert.Contains("InquiryJsonArrayParameter.Bind(_c, \"@keys\", _keys);", text);
        Assert.DoesNotContain("var _keys = _keys;", text);
        Assert.DoesNotContain("var _c = _keys;", text);
    }
}
