using System;

namespace Inquiry.Generators.Tests;

/// <summary>
/// <c>[InquiryParameter(IsInputOutput = true)]</c> stamps <c>ParameterDirection.InputOutput</c>
/// on the input parameter and reads the modified value back as the <c>Task&lt;T&gt;</c> result.
/// Mutually exclusive with <c>OutputParameter</c> and <c>ReturnsValue</c>; at most one per method.
/// </summary>
public sealed partial class InquiryGeneratorTests
{
    private const string InOutHeader = """
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
        }

        public partial class InOutStore : InquiryStore<Item>
        {
        """;

    private static string GetInOutStore(GeneratorTestResult result)
    {
        var tree = Assert.Single(
            result.RunResult.GeneratedTrees,
            static t => t.FilePath.EndsWith("InOutStore.InquiryStore.g.cs", StringComparison.Ordinal));
        return tree.GetText().ToString();
    }

    [Fact]
    public void InOut_IntParameterEmitsInputOutputDirection()
    {
        var source = InOutHeader + """
                [InquiryStoredProcedure("usp_Increment")]
                public partial Task<int> IncrementAsync([InquiryParameter(IsInputOutput = true)] int counter, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        AssertNoErrors(result);
        var text = GetInOutStore(result);

        Assert.Contains("_p0.ParameterName = \"@counter\";", text);
        Assert.Contains("_p0.DbType = global::System.Data.DbType.Int32;", text);
        Assert.Contains("_p0.Direction = global::System.Data.ParameterDirection.InputOutput;", text);
        Assert.Contains("_p0.Value = (object?)counter ?? global::System.DBNull.Value;", text);
        Assert.DoesNotContain("_p1", text);
        Assert.Contains("Inquiry.ExecuteProcedureScalarAsync<int,", text);
        Assert.Contains("\"@counter\"", text);
    }

    [Fact]
    public void InOut_StringParameterAutoSetsMaxSize()
    {
        var source = InOutHeader + """
                [InquiryStoredProcedure("usp_Transform")]
                public partial Task<string> TransformAsync([InquiryParameter(IsInputOutput = true)] string value, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        AssertNoErrors(result);
        var text = GetInOutStore(result);

        Assert.Contains("_p0.Direction = global::System.Data.ParameterDirection.InputOutput;", text);
        Assert.Contains("_p0.Size = -1;", text);
        Assert.Contains("_p0.DbType = global::System.Data.DbType.String;", text);
    }

    [Fact]
    public void InOut_StringParameterDeclaredLengthOverridesMaxSize()
    {
        var source = InOutHeader + """
                [InquiryStoredProcedure("usp_Transform")]
                public partial Task<string> TransformAsync([InquiryParameter(IsInputOutput = true, Length = 50)] string value, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        AssertNoErrors(result);
        var text = GetInOutStore(result);

        Assert.Contains("_p0.Direction = global::System.Data.ParameterDirection.InputOutput;", text);
        Assert.Contains("_p0.Size = 50;", text);
    }

    [Fact]
    public void InOut_DecimalParameterAutoSetsPrecisionScale()
    {
        var source = InOutHeader + """
                [InquiryStoredProcedure("usp_Calculate")]
                public partial Task<decimal> CalculateAsync([InquiryParameter(IsInputOutput = true)] decimal amount, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        AssertNoErrors(result);
        var text = GetInOutStore(result);

        Assert.Contains("_p0.Direction = global::System.Data.ParameterDirection.InputOutput;", text);
        Assert.Contains("_p0.Precision = 38;", text);
        Assert.Contains("_p0.Scale = 10;", text);
    }

    [Fact]
    public void InOut_DecimalParameterDeclaredPrecisionScaleOverridesDefaults()
    {
        var source = InOutHeader + """
                [InquiryStoredProcedure("usp_Calculate")]
                public partial Task<decimal> CalculateAsync([InquiryParameter(IsInputOutput = true, Precision = 18, Scale = 2)] decimal amount, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        AssertNoErrors(result);
        var text = GetInOutStore(result);

        Assert.Contains("_p0.Direction = global::System.Data.ParameterDirection.InputOutput;", text);
        Assert.Contains("_p0.Precision = 18;", text);
        Assert.Contains("_p0.Scale = 2;", text);
    }

    [Fact]
    public void InOut_WithOtherInputParameters()
    {
        var source = InOutHeader + """
                [InquiryStoredProcedure("usp_AddTax")]
                public partial Task<decimal> AddTaxAsync(int regionId, [InquiryParameter(IsInputOutput = true)] decimal total, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        AssertNoErrors(result);
        var text = GetInOutStore(result);

        Assert.Contains("_p0.ParameterName = \"@regionId\";", text);
        Assert.Contains("_p0.DbType = global::System.Data.DbType.Int32;", text);
        Assert.DoesNotContain("_p0.Direction", text);

        Assert.Contains("_p1.ParameterName = \"@total\";", text);
        Assert.Contains("_p1.Direction = global::System.Data.ParameterDirection.InputOutput;", text);
        Assert.Contains("_p1.Precision = 38;", text);
        Assert.Contains("_p1.Scale = 10;", text);

        Assert.DoesNotContain("_p2", text);
        Assert.Contains("\"@total\"", text);
    }

    [Fact]
    public void InOut_NoSeparateOutputParameterEmitted()
    {
        var source = InOutHeader + """
                [InquiryStoredProcedure("usp_Inc")]
                public partial Task<int> IncAsync([InquiryParameter(IsInputOutput = true)] int val, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        AssertNoErrors(result);
        var text = GetInOutStore(result);

        Assert.DoesNotContain("ParameterDirection.Output", text);
        Assert.DoesNotContain("ParameterDirection.ReturnValue", text);
        Assert.Contains("ParameterDirection.InputOutput", text);
    }

    [Fact]
    public void InOut_MutuallyExclusiveWithOutputParameter()
    {
        var source = InOutHeader + """
                [InquiryStoredProcedure("usp_Bad", OutputParameter = "Total")]
                public partial Task<int> BadAsync([InquiryParameter(IsInputOutput = true)] int val, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        Assert.Contains(result.RunResult.Diagnostics,
            d => d.Id == "INQ051" && d.GetMessage().Contains("IsInputOutput"));
    }

    [Fact]
    public void InOut_MutuallyExclusiveWithReturnsValue()
    {
        var source = InOutHeader + """
                [InquiryStoredProcedure("usp_Bad", ReturnsValue = true)]
                public partial Task<int> BadAsync([InquiryParameter(IsInputOutput = true)] int val, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        Assert.Contains(result.RunResult.Diagnostics,
            d => d.Id == "INQ051" && d.GetMessage().Contains("IsInputOutput"));
    }

    [Fact]
    public void InOut_ByteArrayAutoSetsMaxSize()
    {
        var source = InOutHeader + """
                [InquiryStoredProcedure("usp_Transform")]
                public partial Task<byte[]> TransformAsync([InquiryParameter(IsInputOutput = true)] byte[] data, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        AssertNoErrors(result);
        var text = GetInOutStore(result);

        Assert.Contains("_p0.Direction = global::System.Data.ParameterDirection.InputOutput;", text);
        Assert.Contains("_p0.Size = -1;", text);
        Assert.Contains("_p0.DbType = global::System.Data.DbType.Binary;", text);
    }

    [Fact]
    public void InOut_MultipleInOutParametersRejected()
    {
        var source = InOutHeader + """
                [InquiryStoredProcedure("usp_Bad")]
                public partial Task<int> BadAsync(
                    [InquiryParameter(IsInputOutput = true)] int a,
                    [InquiryParameter(IsInputOutput = true)] int b,
                    CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        Assert.Contains(result.RunResult.Diagnostics,
            d => d.Id == "INQ051" && d.GetMessage().Contains("at most one"));
    }

    [Fact]
    public void InOut_TypeMismatchWithReturnTypeRejected()
    {
        var source = InOutHeader + """
                [InquiryStoredProcedure("usp_Bad")]
                public partial Task<string> BadAsync([InquiryParameter(IsInputOutput = true)] int val, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        Assert.Contains(result.RunResult.Diagnostics,
            d => d.Id == "INQ051" && d.GetMessage().Contains("must match"));
    }

    [Fact]
    public void InOut_NullableIntParameter()
    {
        var source = InOutHeader + """
                [InquiryStoredProcedure("usp_Inc")]
                public partial Task<int?> IncAsync([InquiryParameter(IsInputOutput = true)] int? val, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        AssertNoErrors(result);
        var text = GetInOutStore(result);

        Assert.Contains("_p0.Direction = global::System.Data.ParameterDirection.InputOutput;", text);
        Assert.Contains("_p0.DbType = global::System.Data.DbType.Int32;", text);
        Assert.Contains("ExecuteProcedureScalarAsync<int?,", text);
    }

    [Fact]
    public void InOut_EnumParameterCastsToUnderlyingType()
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

            public partial class InOutStore : InquiryStore<Item>
            {
                [InquiryStoredProcedure("usp_Toggle")]
                public partial Task<Status> ToggleAsync([InquiryParameter(IsInputOutput = true)] Status status, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        AssertNoErrors(result);
        var text = GetInOutStore(result);

        Assert.Contains("_p0.Direction = global::System.Data.ParameterDirection.InputOutput;", text);
        Assert.Contains("_p0.DbType = global::System.Data.DbType.Int32;", text);
        Assert.Contains("_p0.Value = (object)(int)status;", text);
    }

    [Fact]
    public void InOut_DeclaredLengthNotDuplicatedInGeneratedCode()
    {
        var source = InOutHeader + """
                [InquiryStoredProcedure("usp_Transform")]
                public partial Task<string> TransformAsync([InquiryParameter(IsInputOutput = true, Length = 50)] string value, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source);
        AssertNoErrors(result);
        var text = GetInOutStore(result);

        var count = 0;
        var idx = 0;
        while ((idx = text.IndexOf("_p0.Size = 50;", idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx++;
        }
        Assert.Equal(1, count);
    }
}
