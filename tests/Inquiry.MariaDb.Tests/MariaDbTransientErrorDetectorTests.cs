using System.Reflection;
using Inquiry.MariaDb;
using MySqlConnector;

namespace Inquiry.MariaDb.Tests;

public sealed class MariaDbTransientErrorDetectorTests
{
    private readonly MariaDbTransientErrorDetector _detector = new();

    [Theory]
    [InlineData(1040)]  // Too many connections
    [InlineData(1042)]  // Can't get hostname
    [InlineData(1043)]  // Bad handshake
    [InlineData(2002)]  // Can't connect via socket
    [InlineData(2003)]  // Can't connect to server
    [InlineData(2006)]  // Server has gone away
    [InlineData(2013)]  // Lost connection during query
    public void TransientNumbersAreTransient(int number)
    {
        Assert.True(_detector.IsTransient(CreateMySqlException(number)));
    }

    [Theory]
    [InlineData(1045)]  // Access denied
    [InlineData(1049)]  // Unknown database
    [InlineData(1062)]  // Duplicate entry
    [InlineData(1146)]  // Table doesn't exist
    public void NonTransientNumbersAreNotTransient(int number)
    {
        Assert.False(_detector.IsTransient(CreateMySqlException(number)));
    }

    [Fact]
    public void NonMySqlExceptionIsNotTransient()
    {
        Assert.False(_detector.IsTransient(new InvalidOperationException()));
    }

    private static MySqlException CreateMySqlException(int number)
    {
        var ctor = typeof(MySqlException)
            .GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)
            .First(c => c.GetParameters() is { Length: >= 3 } p
                && p[0].ParameterType.IsEnum
                && p[1].ParameterType == typeof(string));

        var errorCodeType = ctor.GetParameters()[0].ParameterType;
        var errorCode = Enum.ToObject(errorCodeType, number);
        var args = BuildArgs(ctor.GetParameters(), errorCode);
        return (MySqlException)ctor.Invoke(args);
    }

    private static object?[] BuildArgs(ParameterInfo[] parameters, object errorCode)
    {
        var args = new object?[parameters.Length];
        for (var i = 0; i < parameters.Length; i++)
        {
            args[i] = i == 0
                ? errorCode
                : DefaultArg(parameters[i].ParameterType);
        }
        return args;
    }

    private static object? DefaultArg(Type type)
    {
        if (type == typeof(string)) return string.Empty;
        if (type == typeof(int)) return 0;
        if (type == typeof(Exception)) return null;
        return type.IsValueType ? Activator.CreateInstance(type) : null;
    }
}
