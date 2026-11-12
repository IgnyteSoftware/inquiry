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

        var result = RunGenerator(source, dialect: "SqlServer");
        AssertNoErrors(result);
        var text = GetProcParamStore(result);

        Assert.Contains("_p0.DbType = global::System.Data.DbType.Int32;", text);
    }

    // #547: procedure inputs use the dialect's DbType, as entity binders do. ODP.NET rejects
    // DbType.Guid and DbType.DateTime2.
    [Theory]
    [InlineData("SqlServer", "Guid", "Guid")]
    [InlineData("SqlServer", "Guid?", "Guid")]
    [InlineData("SqlServer", "DateTime", "DateTime2")]
    [InlineData("SqlServer", "DateTime?", "DateTime2")]
    [InlineData("SqlServer", "bool", "Boolean")]
    [InlineData("SqlServer", "DateOnly", "Date")]
    [InlineData("SqlServer", "TimeOnly", "Time")]
    [InlineData("Oracle", "Guid", "Binary")]
    [InlineData("Oracle", "Guid?", "Binary")]
    [InlineData("Oracle", "DateTime", "DateTime")]
    [InlineData("Oracle", "DateTime?", "DateTime")]
    [InlineData("Oracle", "bool", "Int32")]
    [InlineData("Oracle", "DateOnly", "Date")]
    public void ProcParam_UsesTheDialectDbType(string dialect, string parameterType, string expectedDbType)
    {
        var source = ProcParamHeader + $$"""
                [InquiryStoredProcedure("usp_Test")]
                public partial Task<int> TestAsync({{parameterType}} value, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source, dialect: dialect);
        AssertNoErrors(result);
        var text = GetProcParamStore(result);

        Assert.Contains($"_p0.DbType = global::System.Data.DbType.{expectedDbType};", text);
    }

    [Theory]
    [InlineData("DateOnly", "_p0.Value = (object)value.ToDateTime(global::System.TimeOnly.MinValue);")]
    [InlineData("DateOnly?", "_p0.Value = value.HasValue ? (object)value.Value.ToDateTime(global::System.TimeOnly.MinValue) : global::System.DBNull.Value;")]
    [InlineData("TimeOnly", "_p0.Value = (object)value.ToTimeSpan();")]
    [InlineData("TimeOnly?", "_p0.Value = value.HasValue ? (object)value.Value.ToTimeSpan() : global::System.DBNull.Value;")]
    public void ProcParam_OracleBindsDateOnlyAndTimeOnlyAsEntityBindersDo(string parameterType, string expectedValue)
    {
        var source = ProcParamHeader + $$"""
                [InquiryStoredProcedure("usp_Test")]
                public partial Task<int> TestAsync({{parameterType}} value, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source, dialect: "Oracle");
        AssertNoErrors(result);
        var text = GetProcParamStore(result);

        Assert.Contains(expectedValue, text);
        if (parameterType.StartsWith("TimeOnly", StringComparison.Ordinal))
        {
            // Oracle sets no DbType for TimeOnly; ODP.NET infers INTERVAL DAY TO SECOND from the TimeSpan.
            Assert.DoesNotContain("_p0.DbType", text);
        }
    }

    [Fact]
    public void ProcParam_InfersDbTypeFromClrType_String()
    {
        var source = ProcParamHeader + """
                [InquiryStoredProcedure("usp_Test")]
                public partial Task<int> TestAsync(string name, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source, dialect: "SqlServer");
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

        var result = RunGenerator(source, dialect: "SqlServer");
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

        var result = RunGenerator(source, dialect: "SqlServer");
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

        var result = RunGenerator(source, dialect: "SqlServer");
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

        var result = RunGenerator(source, dialect: "SqlServer");
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

        var result = RunGenerator(source, dialect: "SqlServer");
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

        var result = RunGenerator(source, dialect: "SqlServer");
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

        var result = RunGenerator(source, dialect: "SqlServer");
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

        var result = RunGenerator(source, dialect: "SqlServer");
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

        var result = RunGenerator(source, dialect: "SqlServer");
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

        var result = RunGenerator(source, dialect: "SqlServer");
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

        var result = RunGenerator(source, dialect: "SqlServer");
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

        var result = RunGenerator(source, dialect: "SqlServer");
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

        var result = RunGenerator(source, dialect: "SqlServer");
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

        var result = RunGenerator(source, dialect: "SqlServer");
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

        var result = RunGenerator(source, dialect: "SqlServer");
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

        var result = RunGenerator(source, dialect: "Oracle");
        Assert.Contains(result.GeneratorDiagnostics, static d => d.Id == "INQ086");
        Assert.DoesNotContain(result.GeneratorDiagnostics, static d => d.Id == "INQ114");
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
