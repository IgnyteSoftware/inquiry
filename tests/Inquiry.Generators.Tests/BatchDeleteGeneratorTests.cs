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
    public void DeleteAllEmitsKeyInClauseAndExpansionBinder()
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

        Assert.Contains("private const string _sqlDeleteAll = \"DELETE FROM \\\"TThing\\\" WHERE \\\"Id\\\" IN (@keys)\";", text);
        Assert.Contains("global::Inquiry.Parameters.InquiryInExpansion.Expand(_c, \"@keys\", ids, Inquiry.MaxParametersPerCommand, dbType: global::System.Data.DbType.Int64);", text);
        Assert.Contains("return Inquiry.ExecuteAsync(_cmd,", text);
    }

    // #112: a batch delete over a declared-length string key must thread the key's Size onto the expanded
    // key parameters on SQL Server, same as the predicate IN path (#102) — otherwise a DeleteAll over a
    // varchar key splits the plan cache by value length.
    [Fact]
    public void DeleteAllThreadsKeySizeOnSqlServerForDeclaredStringKey()
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

        // The expanded key parameters carry both the DbType and the declared Size.
        Assert.Contains("InquiryInExpansion.Expand(_c, \"@keys\", codes, Inquiry.MaxParametersPerCommand, dbType: global::System.Data.DbType.String, size: 64);", text);
    }

    [Fact]
    public void OracleDeleteAllUsesColonKeysSentinelAndExpansion()
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

        // Oracle: unquoted identifiers and the ':' bind sigil on the IN-expansion sentinel; the Expand call
        // passes the same ':keys' so its runtime text-rewrite finds the baked sentinel (FinalizeCommand
        // reconciles the per-element params under BindByName).
        Assert.Contains("private const string _sqlDeleteAll = \"DELETE FROM TThing WHERE Id IN (:keys)\";", text);
        Assert.Contains("global::Inquiry.Parameters.InquiryInExpansion.Expand(_c, \":keys\", ids, Inquiry.MaxParametersPerCommand, dbType: global::System.Data.DbType.Int64);", text);
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

        Assert.Contains("_sqlDeleteAll = \"UPDATE \\\"TDoc\\\" SET \\\"IsDeleted\\\" = 1 WHERE \\\"Id\\\" IN (@keys)\";", text);
    }
}
