using System.Reflection;
using Inquiry.Oracle;
using Oracle.ManagedDataAccess.Client;

namespace Inquiry.Oracle.Tests;

public sealed class OracleTransientErrorDetectorTests
{
    private readonly OracleTransientErrorDetector _detector = new();

    [Theory]
    [InlineData(1033)]   // initialization or shutdown in progress
    [InlineData(1034)]   // Oracle not available
    [InlineData(1089)]   // immediate shutdown in progress
    [InlineData(3113)]   // end-of-file on communication channel
    [InlineData(3114)]   // not connected to ORACLE
    [InlineData(3135)]   // connection lost contact
    [InlineData(12170)]  // TNS connect timeout
    [InlineData(12505)]  // TNS listener does not know of SID
    [InlineData(12541)]  // TNS no listener
    public void TransientNumbersAreTransient(int number)
    {
        Assert.True(_detector.IsTransient(CreateOracleException(number)));
    }

    [Theory]
    [InlineData(1017)]   // invalid username/password
    [InlineData(942)]    // table or view does not exist
    [InlineData(1)]      // unique constraint violated
    public void NonTransientNumbersAreNotTransient(int number)
    {
        Assert.False(_detector.IsTransient(CreateOracleException(number)));
    }

    [Fact]
    public void NonOracleExceptionIsNotTransient()
    {
        Assert.False(_detector.IsTransient(new InvalidOperationException()));
    }

    /// <summary>
    /// <see cref="OracleException"/> has no public constructors, so a single error with the
    /// requested number is synthesized through internal members via reflection.
    /// </summary>
    private static OracleException CreateOracleException(int number)
    {
        var ctors = typeof(OracleException)
            .GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance);

        // Try to find a ctor that takes (int, string) or similar — the exact signature
        // varies across ODP.NET versions. Fall back to the first ctor we can satisfy.
        var ctor = ctors
            .Where(c => c.GetParameters().Length >= 1 && c.GetParameters()[0].ParameterType == typeof(int))
            .OrderBy(c => c.GetParameters().Length)
            .FirstOrDefault()
            ?? ctors.OrderBy(c => c.GetParameters().Length).First();

        var parameters = ctor.GetParameters();
        var args = new object?[parameters.Length];
        for (var i = 0; i < parameters.Length; i++)
        {
            var p = parameters[i];
            if (i == 0 && p.ParameterType == typeof(int))
            {
                args[i] = number;
            }
            else
            {
                args[i] = DefaultArg(p.ParameterType);
            }
        }

        var exception = (OracleException)ctor.Invoke(args);

        // If the ctor didn't accept the error number as its first int param, force-set Number
        // via reflection as a fallback.
        if (exception.Number != number)
        {
            var numberField = typeof(OracleException)
                .GetField("m_errorNumber", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? typeof(OracleException)
                    .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                    .FirstOrDefault(f => f.FieldType == typeof(int));
            numberField?.SetValue(exception, number);
        }

        return exception;
    }

    private static object? DefaultArg(Type type)
    {
        if (type == typeof(string)) return string.Empty;
        if (type == typeof(int)) return 0;
        if (type == typeof(byte)) return (byte)0;
        if (type == typeof(Exception)) return null;
        return type.IsValueType ? Activator.CreateInstance(type) : null;
    }
}
