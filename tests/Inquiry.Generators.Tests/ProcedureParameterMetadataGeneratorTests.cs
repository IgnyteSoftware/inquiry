using System;

namespace Inquiry.Generators.Tests;

public sealed partial class InquiryGeneratorTests
{
    private const string ProcParamHeader = """
        using System;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using Inquiry;
        using Inquiry.Entities;
        using Inquiry.Stores;

        namespace Demo;

        [InquiryTable("Item")]
        public sealed class Item
        {
            [InquiryKey]
            public long Id { get; set; }

            [InquiryColumn("Name")]
            public string Name { get; set; } = string.Empty;
        }

        public partial class ProcParamStore : InquiryStore<Item>
        {
        """;

    private static string GetProcParamStore(GeneratorTestResult result)
    {
        var tree = Assert.Single(
            result.RunResult.GeneratedTrees,
            static t => t.FilePath.EndsWith("ProcParamStore.InquiryStore.g.cs", StringComparison.Ordinal));
        return tree.GetText().ToString();
    }

    [Fact]
    public void ProcParam_InfersDbTypeFromClrType_Int()
    {
        var source = ProcParamHeader + """
                [InquiryStoredProcedure("usp_Test")]
                public partial Task<int> TestAsync(int id, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        AssertNoErrors(result);
        var text = GetProcParamStore(result);

        Assert.Contains("_p0.DbType = global::System.Data.DbType.Int32;", text);
    }

    [Fact]
    public void ProcParam_InfersDbTypeFromClrType_String()
    {
        var source = ProcParamHeader + """
                [InquiryStoredProcedure("usp_Test")]
                public partial Task<int> TestAsync(string name, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        AssertNoErrors(result);
        var text = GetProcParamStore(result);

        Assert.Contains("_p0.DbType = global::System.Data.DbType.String;", text);
    }

    [Fact]
    public void ProcParam_InfersDbTypeFromClrType_Decimal()
    {
        var source = ProcParamHeader + """
                [InquiryStoredProcedure("usp_Test")]
                public partial Task<int> TestAsync(decimal amount, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        AssertNoErrors(result);
        var text = GetProcParamStore(result);

        Assert.Contains("_p0.DbType = global::System.Data.DbType.Decimal;", text);
    }

    [Fact]
    public void ProcParam_InfersDbTypeFromClrType_Long()
    {
        var source = ProcParamHeader + """
                [InquiryStoredProcedure("usp_Test")]
                public partial Task<int> TestAsync(long id, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        AssertNoErrors(result);
        var text = GetProcParamStore(result);

        Assert.Contains("_p0.DbType = global::System.Data.DbType.Int64;", text);
    }

    [Fact]
    public void ProcParam_InfersDbTypeFromClrType_Guid()
    {
        var source = ProcParamHeader + """
                [InquiryStoredProcedure("usp_Test")]
                public partial Task<int> TestAsync(Guid id, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        AssertNoErrors(result);
        var text = GetProcParamStore(result);

        Assert.Contains("_p0.DbType = global::System.Data.DbType.Guid;", text);
    }

    [Fact]
    public void ProcParam_InfersDbTypeFromClrType_DateTime()
    {
        var source = ProcParamHeader + """
                [InquiryStoredProcedure("usp_Test")]
                public partial Task<int> TestAsync(DateTime when, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        AssertNoErrors(result);
        var text = GetProcParamStore(result);

        Assert.Contains("_p0.DbType = global::System.Data.DbType.DateTime2;", text);
    }

    [Fact]
    public void ProcParam_InfersDbTypeFromClrType_Bool()
    {
        var source = ProcParamHeader + """
                [InquiryStoredProcedure("usp_Test")]
                public partial Task<int> TestAsync(bool flag, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        AssertNoErrors(result);
        var text = GetProcParamStore(result);

        Assert.Contains("_p0.DbType = global::System.Data.DbType.Boolean;", text);
    }

    [Fact]
    public void ProcParam_NullablePreservesDbType()
    {
        var source = ProcParamHeader + """
                [InquiryStoredProcedure("usp_Test")]
                public partial Task<int> TestAsync(int? id, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        AssertNoErrors(result);
        var text = GetProcParamStore(result);

        Assert.Contains("_p0.DbType = global::System.Data.DbType.Int32;", text);
    }

    [Fact]
    public void ProcParam_AnsiStringFromAttribute()
    {
        var source = ProcParamHeader + """
                [InquiryStoredProcedure("usp_Test")]
                public partial Task<int> TestAsync([InquiryParameter(IsUnicode = false)] string name, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        AssertNoErrors(result);
        var text = GetProcParamStore(result);

        Assert.Contains("_p0.DbType = global::System.Data.DbType.AnsiString;", text);
    }

    [Fact]
    public void ProcParam_LengthEmitsSize()
    {
        var source = ProcParamHeader + """
                [InquiryStoredProcedure("usp_Test")]
                public partial Task<int> TestAsync([InquiryParameter(Length = 100)] string name, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        AssertNoErrors(result);
        var text = GetProcParamStore(result);

        Assert.Contains("_p0.DbType = global::System.Data.DbType.String;", text);
        Assert.Contains("_p0.Size = 100;", text);
    }

    [Fact]
    public void ProcParam_PrecisionScaleEmitted()
    {
        var source = ProcParamHeader + """
                [InquiryStoredProcedure("usp_Test")]
                public partial Task<int> TestAsync([InquiryParameter(Precision = 18, Scale = 2)] decimal amount, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        AssertNoErrors(result);
        var text = GetProcParamStore(result);

        Assert.Contains("_p0.DbType = global::System.Data.DbType.Decimal;", text);
        Assert.Contains("_p0.Precision = 18;", text);
        Assert.Contains("_p0.Scale = 2;", text);
    }

    [Fact]
    public void ProcParam_PrecisionOnlyDoesNotEmitScaleZero()
    {
        var source = ProcParamHeader + """
                [InquiryStoredProcedure("usp_Test")]
                public partial Task<int> TestAsync([InquiryParameter(Precision = 18)] decimal amount, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        AssertNoErrors(result);
        var text = GetProcParamStore(result);

        Assert.Contains("_p0.Precision = 18;", text);
        Assert.DoesNotContain("_p0.Scale", text);
    }

    [Fact]
    public void ProcParam_MultipleParametersAllGetDbType()
    {
        var source = ProcParamHeader + """
                [InquiryStoredProcedure("usp_Test")]
                public partial Task<int> TestAsync(
                    [InquiryParameter(Length = 50, IsUnicode = false)] string category,
                    [InquiryParameter(Precision = 10, Scale = 4)] decimal price,
                    int quantity,
                    CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        AssertNoErrors(result);
        var text = GetProcParamStore(result);

        Assert.Contains("_p0.DbType = global::System.Data.DbType.AnsiString;", text);
        Assert.Contains("_p0.Size = 50;", text);
        Assert.Contains("_p1.DbType = global::System.Data.DbType.Decimal;", text);
        Assert.Contains("_p1.Precision = 10;", text);
        Assert.Contains("_p1.Scale = 4;", text);
        Assert.Contains("_p2.DbType = global::System.Data.DbType.Int32;", text);
    }

    [Fact]
    public void ProcParam_BinaryLengthEmitsSize()
    {
        var source = ProcParamHeader + """
                [InquiryStoredProcedure("usp_Test")]
                public partial Task<int> TestAsync([InquiryParameter(Length = 256)] byte[] data, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        AssertNoErrors(result);
        var text = GetProcParamStore(result);

        Assert.Contains("_p0.DbType = global::System.Data.DbType.Binary;", text);
        Assert.Contains("_p0.Size = 256;", text);
    }

    [Fact]
    public void ProcParam_DbTypeBeforeValue()
    {
        var source = ProcParamHeader + """
                [InquiryStoredProcedure("usp_Test")]
                public partial Task<int> TestAsync(int id, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        AssertNoErrors(result);
        var text = GetProcParamStore(result);

        var dbTypeIdx = text.IndexOf("_p0.DbType =", StringComparison.Ordinal);
        var valueIdx = text.IndexOf("_p0.Value =", StringComparison.Ordinal);
        Assert.True(dbTypeIdx >= 0, "DbType should be emitted");
        Assert.True(dbTypeIdx < valueIdx, "DbType should appear before Value");
    }

    [Fact]
    public void ProcParam_OracleDialectStillEmitsDbType()
    {
        var source = ProcParamHeader + """
                [InquiryStoredProcedure("usp_Test")]
                public partial Task<int> TestAsync(string name, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source, dialect: "Oracle");
        AssertNoErrors(result);
        var text = GetProcParamStore(result);

        Assert.Contains("_p0.DbType = global::System.Data.DbType.String;", text);
    }

    [Fact]
    public void ProcParam_EnumCastsToUnderlyingInt()
    {
        var source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry;
            using Inquiry.Entities;
            using Inquiry.Stores;

            namespace Demo;

            public enum Status { Active, Inactive }

            [InquiryTable("Item")]
            public sealed class Item
            {
                [InquiryKey]
                public long Id { get; set; }
            }

            public partial class ProcParamStore : InquiryStore<Item>
            {
                [InquiryStoredProcedure("usp_Test")]
                public partial Task<int> TestAsync(Status status, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        AssertNoErrors(result);
        var text = GetProcParamStore(result);

        Assert.Contains("_p0.DbType = global::System.Data.DbType.Int32;", text);
        Assert.Contains("_p0.Value = (object)(int)status;", text);
    }

    [Fact]
    public void ProcParam_NullableEnumCastsToUnderlyingInt()
    {
        var source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Inquiry;
            using Inquiry.Entities;
            using Inquiry.Stores;

            namespace Demo;

            public enum Status { Active, Inactive }

            [InquiryTable("Item")]
            public sealed class Item
            {
                [InquiryKey]
                public long Id { get; set; }
            }

            public partial class ProcParamStore : InquiryStore<Item>
            {
                [InquiryStoredProcedure("usp_Test")]
                public partial Task<int> TestAsync(Status? status, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        AssertNoErrors(result);
        var text = GetProcParamStore(result);

        Assert.Contains("_p0.DbType = global::System.Data.DbType.Int32;", text);
        Assert.Contains("status.HasValue ? (object)(int)status.Value : global::System.DBNull.Value", text);
    }

    [Fact]
    public void ProcParam_CollectionWithTvpTypeNameEmitsBindCall()
    {
        var source = ProcParamHeader + """
                [InquiryStoredProcedure("usp_BulkLookup")]
                public partial Task<int> BulkLookupAsync(
                    [InquiryParameter(TvpTypeName = "[dbo].[IntList]")] IEnumerable<int> ids,
                    CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source, dialect: "SqlServer");
        AssertNoErrors(result);
        var text = GetProcParamStore(result);

        Assert.Contains("InquiryTvpParameter.Bind(_c,", text);
        Assert.Contains("[dbo].[IntList]", text);
        Assert.Contains("_sprocTvp_BulkLookupAsync_ids", text);
    }

    [Fact]
    public void ProcParam_CollectionWithoutTvpTypeNameReportsINQ086()
    {
        var source = ProcParamHeader + """
                [InquiryStoredProcedure("usp_BulkLookup")]
                public partial Task<int> BulkLookupAsync(IEnumerable<int> ids, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source, dialect: "SqlServer");
        Assert.Contains(result.GeneratorDiagnostics, static d => d.Id == "INQ086");
    }

    [Fact]
    public void ProcParam_CollectionTvpOnNonSqlServerReportsINQ086()
    {
        var source = ProcParamHeader + """
                [InquiryStoredProcedure("usp_BulkLookup")]
                public partial Task<int> BulkLookupAsync(
                    [InquiryParameter(TvpTypeName = "[dbo].[IntList]")] IEnumerable<int> ids,
                    CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source, dialect: "Sqlite");
        Assert.Contains(result.GeneratorDiagnostics, static d => d.Id == "INQ086");
    }

    [Fact]
    public void ProcParam_CollectionTvpWithScalarParamsEmitsMixed()
    {
        var source = ProcParamHeader + """
                [InquiryStoredProcedure("usp_ProcessBatch")]
                public partial Task<int> ProcessBatchAsync(
                    string category,
                    [InquiryParameter(TvpTypeName = "[dbo].[IntList]")] IEnumerable<int> ids,
                    int threshold,
                    CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source, dialect: "SqlServer");
        AssertNoErrors(result);
        var text = GetProcParamStore(result);

        Assert.Contains("_p0.DbType = global::System.Data.DbType.String;", text);
        Assert.Contains("InquiryTvpParameter.Bind(_c,", text);
        Assert.Contains("_p2.DbType = global::System.Data.DbType.Int32;", text);
    }

    [Fact]
    public void ProcParam_CollectionTvpUnsupportedElementTypeReportsINQ086()
    {
        var source = ProcParamHeader + """
                [InquiryStoredProcedure("usp_Test")]
                public partial Task<int> TestAsync(
                    [InquiryParameter(TvpTypeName = "[dbo].[TimeSpanList]")] IEnumerable<TimeSpan> spans,
                    CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source, dialect: "SqlServer");
        Assert.Contains(result.GeneratorDiagnostics, static d => d.Id == "INQ086");
    }
}
