using System;

namespace Inquiry.Generators.Tests;

/// <summary>
/// Oracle stored procedures that return entity result sets are wrapped in an anonymous PL/SQL
/// block that declares local <c>SYS_REFCURSOR</c> variables, passes them to the procedure, and
/// hands them to the client via <c>DBMS_SQL.RETURN_RESULT</c>. Non-result-set procedures (scalar
/// output, execute-only) keep <c>CommandType.StoredProcedure</c> unchanged.
/// </summary>
public sealed partial class InquiryGeneratorTests
{
    private const string OracleRefCursorHeader = """
        using System;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using Inquiry;
        using Inquiry.Entities;
        using Inquiry.Stores;

        namespace Demo;

        [InquiryTable("Employee")]
        public sealed class Employee
        {
            [InquiryKey]
            public long Id { get; set; }

            [InquiryColumn("Name")]
            public string Name { get; set; } = string.Empty;
        }

        [InquiryTable("Department")]
        public sealed class Department
        {
            [InquiryKey]
            public long Id { get; set; }

            [InquiryColumn("DeptName")]
            public string DeptName { get; set; } = string.Empty;
        }

        """;

    private static string GetOracleRefCursorStore(GeneratorTestResult result, string storeName = "OracleRefCursorStore")
    {
        var tree = Assert.Single(
            result.RunResult.GeneratedTrees,
            t => t.FilePath.EndsWith($"{storeName}.InquiryStore.g.cs", StringComparison.Ordinal));
        return tree.GetText().ToString();
    }

    [Fact]
    public void Oracle_EntityReturnWrapsInPlSqlRefCursorBlock()
    {
        var source = OracleRefCursorHeader + """
            public partial class OracleRefCursorStore : InquiryStore<Employee>
            {
                [InquiryStoredProcedure("GET_EMPLOYEE")]
                public partial Task<Employee?> GetEmployeeAsync(long employeeId, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source, dialect: "Oracle");
        AssertNoErrors(result);
        var text = GetOracleRefCursorStore(result);

        Assert.Contains("DECLARE c0 SYS_REFCURSOR;", text);
        Assert.Contains("BEGIN GET_EMPLOYEE(", text);
        Assert.Contains("DBMS_SQL.RETURN_RESULT(c0);", text);
        Assert.Contains("END;", text);
        Assert.Contains("CommandType.Text", text);
        Assert.DoesNotContain("CommandType.StoredProcedure", text);
    }

    [Fact]
    public void Oracle_AsyncEnumerableReturnWrapsInRefCursorBlock()
    {
        var source = OracleRefCursorHeader + """
            public partial class OracleRefCursorStore : InquiryStore<Employee>
            {
                [InquiryStoredProcedure("GET_ALL_EMPLOYEES")]
                public partial IAsyncEnumerable<Employee> GetAllEmployeesAsync(CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source, dialect: "Oracle");
        AssertNoErrors(result);
        var text = GetOracleRefCursorStore(result);

        Assert.Contains("DECLARE c0 SYS_REFCURSOR;", text);
        Assert.Contains("BEGIN GET_ALL_EMPLOYEES(c0);", text);
        Assert.Contains("DBMS_SQL.RETURN_RESULT(c0);", text);
        Assert.Contains("CommandType.Text", text);
    }

    [Fact]
    public void Oracle_MultiResultWrapsWithMultipleCursors()
    {
        var source = OracleRefCursorHeader + """
            public partial class OracleRefCursorStore : InquiryStore<Employee>
            {
                [InquiryStoredProcedure("GET_EMP_AND_DEPT")]
                public partial Task<(IReadOnlyList<Employee>, IReadOnlyList<Department>)> GetEmpAndDeptAsync(
                    CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source, dialect: "Oracle");
        AssertNoErrors(result);
        var text = GetOracleRefCursorStore(result);

        Assert.Contains("DECLARE c0 SYS_REFCURSOR; c1 SYS_REFCURSOR;", text);
        Assert.Contains("BEGIN GET_EMP_AND_DEPT(c0, c1);", text);
        Assert.Contains("DBMS_SQL.RETURN_RESULT(c0);", text);
        Assert.Contains("DBMS_SQL.RETURN_RESULT(c1);", text);
        Assert.Contains("CommandType.Text", text);
        Assert.Contains("QueryMultipleAsync<", text);
    }

    [Fact]
    public void Oracle_InputParametersUseEncodedBindNames()
    {
        var source = OracleRefCursorHeader + """
            public partial class OracleRefCursorStore : InquiryStore<Employee>
            {
                [InquiryStoredProcedure("GET_BY_DEPT")]
                public partial IAsyncEnumerable<Employee> GetByDeptAsync(long departmentId, string status, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source, dialect: "Oracle");
        AssertNoErrors(result);
        var text = GetOracleRefCursorStore(result);

        Assert.Contains("iq1$", text);
        Assert.Contains("DECLARE c0 SYS_REFCURSOR;", text);
        Assert.Contains(", c0);", text);
        Assert.Contains("DBMS_SQL.RETURN_RESULT(c0);", text);
        Assert.Contains("CommandType.Text", text);
    }

    [Fact]
    public void Oracle_MultiResultWithInputParametersEncodesAll()
    {
        var source = OracleRefCursorHeader + """
            public partial class OracleRefCursorStore : InquiryStore<Employee>
            {
                [InquiryStoredProcedure("GET_DETAILS")]
                public partial Task<(IReadOnlyList<Employee>, IReadOnlyList<Department>)> GetDetailsAsync(
                    long regionId, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source, dialect: "Oracle");
        AssertNoErrors(result);
        var text = GetOracleRefCursorStore(result);

        Assert.Contains("DECLARE c0 SYS_REFCURSOR; c1 SYS_REFCURSOR;", text);
        Assert.Contains("iq1$", text);
        Assert.Contains(", c0, c1);", text);
        Assert.Contains("DBMS_SQL.RETURN_RESULT(c0); DBMS_SQL.RETURN_RESULT(c1);", text);
    }

    [Fact]
    public void Oracle_ScalarOutputDoesNotWrap()
    {
        var source = OracleRefCursorHeader + """
            public partial class OracleRefCursorStore : InquiryStore<Employee>
            {
                [InquiryStoredProcedure("ADD_VALUES", OutputParameter = "Total")]
                public partial Task<int> AddAsync(int leftValue, int rightValue, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source, dialect: "Oracle");
        AssertNoErrors(result);
        var text = GetOracleRefCursorStore(result);

        Assert.Contains("CommandType.StoredProcedure", text);
        Assert.DoesNotContain("DBMS_SQL", text);
        Assert.DoesNotContain("SYS_REFCURSOR", text);
        Assert.DoesNotContain("CommandType.Text", text);
    }

    [Fact]
    public void Oracle_ExecuteOnlyDoesNotWrap()
    {
        var source = OracleRefCursorHeader + """
            public partial class OracleRefCursorStore : InquiryStore<Employee>
            {
                [InquiryStoredProcedure("UPDATE_STATUS")]
                public partial Task<int> UpdateStatusAsync(long employeeId, string status, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source, dialect: "Oracle");
        AssertNoErrors(result);
        var text = GetOracleRefCursorStore(result);

        Assert.Contains("CommandType.StoredProcedure", text);
        Assert.DoesNotContain("DBMS_SQL", text);
        Assert.DoesNotContain("SYS_REFCURSOR", text);
    }

    [Fact]
    public void Oracle_NoParameterEntityReturnWrapsCorrectly()
    {
        var source = OracleRefCursorHeader + """
            public partial class OracleRefCursorStore : InquiryStore<Employee>
            {
                [InquiryStoredProcedure("GET_ALL")]
                public partial Task<Employee?> GetFirstAsync(CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source, dialect: "Oracle");
        AssertNoErrors(result);
        var text = GetOracleRefCursorStore(result);

        Assert.Contains("DECLARE c0 SYS_REFCURSOR; BEGIN GET_ALL(c0); DBMS_SQL.RETURN_RESULT(c0); END;", text);
        Assert.Contains("CommandType.Text", text);
    }

    [Fact]
    public void Oracle_NonOracleDialectDoesNotWrap()
    {
        var source = OracleRefCursorHeader + """
            public partial class OracleRefCursorStore : InquiryStore<Employee>
            {
                [InquiryStoredProcedure("usp_GetEmployee")]
                public partial Task<Employee?> GetEmployeeAsync(long employeeId, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source, dialect: "SqlServer");
        AssertNoErrors(result);
        var text = GetOracleRefCursorStore(result);

        Assert.Contains("CommandType.StoredProcedure", text);
        Assert.DoesNotContain("DBMS_SQL", text);
        Assert.DoesNotContain("SYS_REFCURSOR", text);
    }

    [Fact]
    public void Oracle_RefCursorBlockDoesNotContainRcBindToken()
    {
        var source = OracleRefCursorHeader + """
            public partial class OracleRefCursorStore : InquiryStore<Employee>
            {
                [InquiryStoredProcedure("GET_EMPLOYEE")]
                public partial Task<Employee?> GetEmployeeAsync(long employeeId, CancellationToken cancellationToken = default);
            }
            """;

        var result = RunGenerator(source, dialect: "Oracle");
        AssertNoErrors(result);
        var text = GetOracleRefCursorStore(result);

        Assert.DoesNotContain(":rc", text);
    }
}
