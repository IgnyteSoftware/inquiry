namespace Inquiry.Oracle.Tests.Fixtures;

/// <summary>
/// xUnit fact that runs only when <see cref="OracleTestHarness.ConnectionStringEnvironmentVariable"/>
/// is set. Lets the Oracle integration suite live in the solution without forcing every developer (or
/// every CI job) to stand up an Oracle instance to build green.
/// </summary>
public sealed class OracleFactAttribute : FactAttribute
{
    public OracleFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(OracleTestHarness.ConnectionStringEnvironmentVariable)))
        {
            Skip = $"Set {OracleTestHarness.ConnectionStringEnvironmentVariable} to a connection string pointing at an Oracle database to run these tests.";
        }
    }
}
