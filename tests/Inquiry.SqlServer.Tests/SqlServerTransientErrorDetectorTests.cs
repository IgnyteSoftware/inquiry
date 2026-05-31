using System.Reflection;
using Inquiry.SqlServer;
using Microsoft.Data.SqlClient;

namespace Inquiry.SqlServer.Tests;

public sealed class SqlServerTransientErrorDetectorTests
{
    private readonly SqlServerTransientErrorDetector _detector = new();

    [Theory]
    [InlineData(40197)]
    [InlineData(40501)]
    [InlineData(40613)]
    [InlineData(49918)]
    [InlineData(49919)]
    [InlineData(49920)]
    [InlineData(4060)]
    [InlineData(10928)]
    [InlineData(10929)]
    [InlineData(233)]
    [InlineData(-2)]
    public void TransientAzureNumbersAreTransient(int number)
    {
        Assert.True(_detector.IsTransient(CreateSqlException(number)));
    }

    [Theory]
    [InlineData(18456)] // login failed
    [InlineData(208)]   // invalid object name
    [InlineData(2627)]  // primary key violation
    public void NonTransientNumbersAreNotTransient(int number)
    {
        Assert.False(_detector.IsTransient(CreateSqlException(number)));
    }

    [Fact]
    public void NonSqlExceptionIsNotTransient()
    {
        Assert.False(_detector.IsTransient(new InvalidOperationException()));
    }

    /// <summary>
    /// <see cref="SqlException"/> / <see cref="SqlError"/> have no public constructors, so a single
    /// error with the requested number is synthesized through the internal factory members via
    /// reflection. The exact ctor signature varies across Microsoft.Data.SqlClient versions, so the
    /// matching ctor is discovered at runtime.
    /// </summary>
    private static SqlException CreateSqlException(int number)
    {
        var errorCtor = typeof(SqlError)
            .GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)
            .First(c => c.GetParameters() is { Length: > 0 } p && p[0].ParameterType == typeof(int));

        var error = (SqlError)errorCtor.Invoke(BuildArgs(errorCtor.GetParameters(), number));

        var collection = (SqlErrorCollection)Activator.CreateInstance(
            typeof(SqlErrorCollection),
            BindingFlags.NonPublic | BindingFlags.Instance,
            binder: null,
            args: null,
            culture: null)!;

        typeof(SqlErrorCollection)
            .GetMethod("Add", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(collection, new object[] { error });

        var create = typeof(SqlException)
            .GetMethod(
                "CreateException",
                BindingFlags.NonPublic | BindingFlags.Static,
                binder: null,
                types: new[] { typeof(SqlErrorCollection), typeof(string) },
                modifiers: null)!;

        return (SqlException)create.Invoke(null, new object?[] { collection, "11.0.0" })!;
    }

    private static object?[] BuildArgs(ParameterInfo[] parameters, int number)
    {
        var args = new object?[parameters.Length];
        for (var i = 0; i < parameters.Length; i++)
        {
            var p = parameters[i];
            args[i] = i == 0
                ? number
                : DefaultArg(p.ParameterType);
        }

        return args;
    }

    private static object? DefaultArg(Type type)
    {
        if (type == typeof(string))
        {
            return string.Empty;
        }

        if (type == typeof(byte))
        {
            return (byte)0;
        }

        if (type == typeof(int))
        {
            return 0;
        }

        if (type == typeof(uint))
        {
            return 0u;
        }

        if (type == typeof(Exception))
        {
            return null;
        }

        return type.IsValueType ? Activator.CreateInstance(type) : null;
    }
}
