using System;

namespace Inquiry.Generators.Tests;

/// <summary>
/// Batch-insert emission: <c>[InquiryInsertAll]</c> emits a prefix const plus a runtime-built
/// multi-row VALUES clause, bound through the existing ExecuteAsync&lt;TArgs&gt; fast path.
/// </summary>
public sealed partial class InquiryGeneratorTests
{
    [Fact]
    public void SqliteInsertAllEmitsPreferredPreparedRowDescriptor()
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
                [InquiryKey]
                public long Id { get; set; }

                [InquiryColumn("Name")]
                public string Name { get; set; } = string.Empty;
            }

            public partial class ThingStore : Inquiry.Stores.InquiryStore<Demo.Thing>
            {
                [InquiryInsertAll]
                public partial Task<int> InsertAllAsync(IEnumerable<Thing> things, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        AssertNoErrors(result);
        var tree = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("ThingStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var text = tree.GetText().ToString();

        Assert.Contains("private const string _sqlInsert = \"INSERT INTO \\\"TThing\\\" (\\\"Id\\\", \\\"Name\\\") VALUES (@Id, @Name)\";", text);
        Assert.DoesNotContain("_sqlInsertAllPrefix", text);
        Assert.Contains("private static readonly global::Inquiry.Commands.InquiryBatchCommand<global::Demo.Thing> _batch_InsertAllAsync_", text);
        Assert.Contains("return Inquiry.ExecuteBatchAsync(_batch_InsertAllAsync_", text);
        Assert.Contains("_p0.ParameterName = \"@Id\";", text);
        Assert.Contains("_p1.ParameterName = \"@Name\";", text);
        Assert.Contains("preferPrepareOnce: true);", text);
    }

    [Fact]
    public void InsertAllConsumesLazyEnumerableOnceIntoReusableBoundedBuffer()
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
                [InquiryKey]
                public long Id { get; set; }

                [InquiryColumn("Name")]
                public string Name { get; set; } = string.Empty;
            }

            public partial class ThingStore : Inquiry.Stores.InquiryStore<Demo.Thing>
            {
                [InquiryInsertAll]
                public partial Task<int> InsertAllAsync(IEnumerable<Thing> things, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        AssertNoErrors(result);
        var tree = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("ThingStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var text = tree.GetText().ToString();

        Assert.DoesNotContain("global::System.Linq.Enumerable.ToList(things)", text);
        Assert.Contains("return Inquiry.ExecuteBatchAsync(_batch_InsertAllAsync_", text);
        Assert.DoesNotContain("var _enumerator", text);
        Assert.DoesNotContain("var _buffer", text);
        Assert.DoesNotContain("foreach (var _item in things)", text);
    }

    [Fact]
    public void InsertAllOmitsDatabaseGeneratedTokenColumn()
    {
        // A SQL Server rowversion (DatabaseGenerated token) is supplied by the DB — it must be absent
        // from both the prefix column list and the bound values, matching the single-row insert.
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
                [InquiryKey]
                public long Id { get; set; }

                [InquiryColumn("Name")]
                public string Name { get; set; } = string.Empty;

                [InquiryColumn("RowVer"), InquiryConcurrencyToken(DatabaseGenerated = true)]
                public byte[] RowVer { get; set; } = Array.Empty<byte>();
            }

            public partial class DocStore : Inquiry.Stores.InquiryStore<Demo.Doc>
            {
                [InquiryInsertAll]
                public partial Task<int> InsertAllAsync(IEnumerable<Doc> docs, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source, dialect: "SqlServer");
        AssertNoErrors(result);
        var tree = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("DocStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var text = tree.GetText().ToString();

        Assert.Contains("private const string _sqlInsertAllPrefix = \"INSERT INTO [TDoc] ([Id], [Name]) VALUES \";", text);
        Assert.DoesNotContain("RowVer", text);
    }

    [Theory]
    [InlineData("SqlServer", 1000)]
    [InlineData("PostgreSql", 32767)]
    [InlineData("MySql", 32767)]
    [InlineData("MariaDb", 32767)]
    public void InsertAllDescriptorCarriesDialectHardCap(string dialect, int expectedRows)
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
                [InquiryKey]
                public long Id { get; set; }

                [InquiryColumn]
                public int Qty { get; set; }
            }

            public partial class ThingStore : InquiryStore<Thing>
            {
                [InquiryInsertAll]
                public partial Task<int> InsertAllAsync(IEnumerable<Thing> things, CancellationToken ct = default);
            }
            """;

        var result = RunGenerator(source, dialect: dialect);
        AssertNoErrors(result);
        var tree = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("ThingStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var text = tree.GetText().ToString();

        Assert.Contains("parametersPerItem: 2,", text);
        Assert.Contains($"maxItemsPerCommand: {expectedRows}" +
            (dialect == "SqlServer" ? "," : ");"), text);
    }

    [Fact]
    public void SqlServerInsertAllEmitsAdaptiveBoundaryWithBothExactBinders()
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
                [InquiryKey] public long Id { get; set; }
                [InquiryColumn(Length = 100)] public string Name { get; set; } = string.Empty;
            }

            public partial class ThingStore : InquiryStore<Thing>
            {
                [InquiryInsertAll]
                public partial Task<int> InsertAllAsync(IEnumerable<Thing> things, CancellationToken ct = default);
            }
            """;

        var result = RunGenerator(source, dialect: "SqlServer");
        AssertNoErrors(result);
        var tree = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("ThingStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var text = tree.GetText().ToString();

        Assert.Contains("private const string _sqlInsert = \"INSERT INTO [TThing] ([Id], [Name]) VALUES (@Id, @Name)\";", text);
        Assert.Contains("private const string _sqlInsertAllPrefix = \"INSERT INTO [TThing] ([Id], [Name]) VALUES \";", text);
        Assert.Contains("_p0.ParameterName = \"@Id\";", text);
        Assert.Contains("_p.ParameterName = \"@p\" + _r + \"_1\";", text);
        Assert.Contains("static _items => _items.Count < 250,", text);
        Assert.Contains("parametersPerItem: 2,", text);
        Assert.Contains("maxItemsPerCommand: 1000,", text);
        Assert.Contains("setBasedMaxItemsPerCommand: 1000);", text);
    }

    [Fact]
    public void SqlServerWideInsertKeepsDbBatchAtOneThousandAndCapsOnlySetBasedSqlAtTwoHundredTen()
    {
        const string source = """
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry.Entities;
            using Inquiry.Stores;

            namespace Demo;

            [InquiryTable("TWide")]
            public sealed class Wide
            {
                [InquiryKey] public int C0 { get; set; }
                [InquiryColumn] public int C1 { get; set; }
                [InquiryColumn] public int C2 { get; set; }
                [InquiryColumn] public int C3 { get; set; }
                [InquiryColumn] public int C4 { get; set; }
                [InquiryColumn] public int C5 { get; set; }
                [InquiryColumn] public int C6 { get; set; }
                [InquiryColumn] public int C7 { get; set; }
                [InquiryColumn] public int C8 { get; set; }
                [InquiryColumn] public int C9 { get; set; }
            }

            public partial class WideStore : InquiryStore<Wide>
            {
                [InquiryInsertAll]
                public partial Task<int> InsertAllAsync(IEnumerable<Wide> items, CancellationToken ct = default);
            }
            """;

        var result = RunGenerator(source, dialect: "SqlServer");
        AssertNoErrors(result);
        var text = Assert.Single(result.RunResult.GeneratedTrees,
            static tree => tree.FilePath.EndsWith("WideStore.InquiryStore.g.cs", StringComparison.Ordinal))
            .GetText().ToString();

        Assert.Contains("static _items => _items.Count < 250,", text);
        Assert.Contains("parametersPerItem: 10,", text);
        Assert.Contains("maxItemsPerCommand: 1000,", text);
        Assert.Contains("setBasedMaxItemsPerCommand: 210);", text);
    }

    [Fact]
    public void OracleInsertAllUsesFixedSqlAndNativeArrayBinding()
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
                [InquiryKey]
                public long Id { get; set; }

                [InquiryColumn]
                public int Qty { get; set; }
            }

            public partial class ThingStore : InquiryStore<Thing>
            {
                [InquiryInsertAll]
                public partial Task<int> InsertAllAsync(IEnumerable<Thing> things, CancellationToken ct = default);
            }
            """;

        var result = RunGenerator(source, dialect: "Oracle");
        AssertNoErrors(result);
        var tree = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("ThingStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var text = tree.GetText().ToString();

        Assert.Contains("private const string _sqlInsert = \"INSERT INTO TThing (Id, Qty) VALUES (:", text);
        Assert.Contains("_sqlInsert,", text);
        Assert.Contains("bindChunk: static (_cmd, _items) =>", text);
        Assert.Contains("((global::Oracle.ManagedDataAccess.Client.OracleCommand)_cmd).ArrayBindCount = _items.Count;", text);
        Assert.Contains("var _values0 = new object?[_items.Count];", text);
        Assert.DoesNotContain("parametersPerItem:", text);
    }

    [Fact]
    public void OracleArrayBinderEmitsVariableElementSizesWithoutChangingFixedValueConversions()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry.Entities;
            using Inquiry.Stores;

            namespace Demo;

            [InquiryTable("TThing")]
            public sealed class Thing
            {
                [InquiryKey] public Guid Id { get; set; }
                [InquiryColumn(Length = 100)] public string Name { get; set; } = string.Empty;
                [InquiryColumn] public byte[] Payload { get; set; } = System.Array.Empty<byte>();
                [InquiryColumn] public bool Enabled { get; set; }
                [InquiryColumn] public TimeOnly Window { get; set; }
            }

            public partial class ThingStore : InquiryStore<Thing>
            {
                [InquiryInsertAll]
                public partial Task<int> InsertAllAsync(IEnumerable<Thing> things, CancellationToken ct = default);
            }
            """;

        var result = RunGenerator(source, dialect: "Oracle");
        AssertNoErrors(result);
        var tree = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("ThingStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var text = tree.GetText().ToString();

        Assert.Contains("var _sizes1 = new int[_items.Count];", text);
        Assert.Contains("_sizes1[_i] = _values1[_i] is string _value1 ? _value1.Length : 0;", text);
        Assert.Contains("((global::Oracle.ManagedDataAccess.Client.OracleParameter)_p1).ArrayBindSize = _sizes1;", text);
        Assert.Contains("var _sizes2 = new int[_items.Count];", text);
        Assert.Contains("_sizes2[_i] = _values2[_i] is byte[] _value2 ? _value2.Length : 0;", text);
        Assert.Contains("((global::Oracle.ManagedDataAccess.Client.OracleParameter)_p2).ArrayBindSize = _sizes2;", text);
        Assert.DoesNotContain("var _sizes0", text);
        Assert.DoesNotContain("var _sizes3", text);
        Assert.Contains("_p0.DbType = global::System.Data.DbType.Binary;", text);
        Assert.Contains("_p3.DbType = global::System.Data.DbType.Int32;", text);
        Assert.Contains("((global::Oracle.ManagedDataAccess.Client.OracleParameter)_p4).OracleDbType = global::Oracle.ManagedDataAccess.Client.OracleDbType.IntervalDS;", text);
        Assert.Contains("_values4[_i] = (object)_it.Window.ToTimeSpan();", text);
    }

    [Theory]
    [InlineData("Sqlite")]
    [InlineData("SqlServer")]
    [InlineData("PostgreSql")]
    [InlineData("MySql")]
    [InlineData("MariaDb")]
    [InlineData("Oracle")]
    public void DefaultOnlyInsertAllUsesFixedSingleRowStatement(string dialect)
    {
        const string source = """
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry.Entities;
            using Inquiry.Stores;

            namespace Demo;

            [InquiryTable("TDefault")]
            public sealed class DefaultItem
            {
                [InquiryKey(IsGenerated = true)]
                public long Id { get; set; }
            }

            public partial class DefaultStore : InquiryStore<DefaultItem>
            {
                [InquiryInsertAll]
                public partial Task<int> InsertAllAsync(IEnumerable<DefaultItem> items, CancellationToken ct = default);
            }
            """;

        var result = RunGenerator(source, dialect: dialect);
        AssertNoErrors(result);
        var tree = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("DefaultStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var text = tree.GetText().ToString();
        var descriptor = BatchDescriptor(text, "InsertAllAsync");

        Assert.Contains("_sqlInsert,", descriptor);
        Assert.Contains("static (_, _it) =>", descriptor);
        Assert.DoesNotContain("static _count =>", descriptor);
        if (dialect == "Sqlite")
        {
            Assert.Contains("bindChunk: null,", descriptor);
            Assert.Contains("preferPrepareOnce: true);", descriptor);
        }
        else
        {
            Assert.DoesNotContain("bindChunk:", descriptor);
            Assert.DoesNotContain("preferPrepareOnce", descriptor);
        }
    }

    [Fact]
    public void InsertAllOverloadsUseDistinctSignatureStableDescriptors()
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
                [InquiryKey]
                public long Id { get; set; }

                [InquiryColumn]
                public string Name { get; set; } = string.Empty;
            }

            public partial class ThingStore : InquiryStore<Thing>
            {
                [InquiryInsertAll]
                public partial Task<int> SaveAll(IEnumerable<Thing> items, CancellationToken ct = default);

                [InquiryInsertAll]
                public partial Task<int> SaveAll(IReadOnlyList<Thing> items, CancellationToken ct = default);
            }
            """;

        var result = RunGenerator(source);
        AssertNoErrors(result);
        var tree = Assert.Single(result.RunResult.GeneratedTrees, static t => t.FilePath.EndsWith("ThingStore.InquiryStore.g.cs", StringComparison.Ordinal));
        var text = tree.GetText().ToString();
        var matches = global::System.Text.RegularExpressions.Regex.Matches(
            text,
            @"private static readonly global::Inquiry\.Commands\.InquiryBatchCommand<global::Demo\.Thing> (_batch_SaveAll_[0-9a-f]{16}) = new\(");

        Assert.Equal(2, matches.Count);
        Assert.NotEqual(matches[0].Groups[1].Value, matches[1].Groups[1].Value);
        Assert.Equal(2, global::System.Text.RegularExpressions.Regex.Matches(text, matches[0].Groups[1].Value).Count);
        Assert.Equal(2, global::System.Text.RegularExpressions.Regex.Matches(text, matches[1].Groups[1].Value).Count);
    }

    [Fact]
    public void NullableBatchMutationCollectionsPreserveNullAsEmptyWithoutReenumeration()
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
                public long Id { get; set; }

                [InquiryColumn]
                public string Name { get; set; } = string.Empty;
            }

            public partial class ThingStore : InquiryStore<Thing>
            {
                [InquiryInsertAll]
                public partial Task<int> InsertAllAsync(IEnumerable<Thing>? items, CancellationToken ct = default);

                [InquiryUpdateAll]
                public partial Task<int> UpdateAllAsync(IReadOnlyList<Thing>? items, CancellationToken ct = default);

                [InquiryBulkInsert]
                public partial Task<long> BulkInsertAsync(List<Thing>? items, CancellationToken ct = default);
            }
            """;

        var result = RunGenerator(source);
        AssertNoErrors(result);
        Assert.DoesNotContain(result.Compilation.GetDiagnostics(), static diagnostic => diagnostic.Id == "CS8604");
        var text = Assert.Single(result.RunResult.GeneratedTrees,
            static tree => tree.FilePath.EndsWith("ThingStore.InquiryStore.g.cs", StringComparison.Ordinal)).GetText().ToString();

        Assert.Equal(3, global::System.Text.RegularExpressions.Regex.Matches(
            text,
            @"\(\(global::System\.Collections\.Generic\.IEnumerable<global::Demo\.Thing>\?\)items\) \?\? global::System\.Array\.Empty<global::Demo\.Thing>\(\)").Count);
        Assert.DoesNotContain("Enumerable.ToList", text);
    }
}
