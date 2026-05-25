namespace Inquiry.SqlServer.Tests.Fixtures;

/// <summary>
/// xUnit fact that runs only when <see cref="SqlServerTestHarness.ConnectionStringEnvironmentVariable"/>
/// is set. Lets the SQL Server integration suite live in the solution without forcing every
/// developer (or every CI job) to stand up a SQL Server instance to build green.
/// </summary>
public sealed class SqlServerFactAttribute : FactAttribute
{
    public SqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(SqlServerTestHarness.ConnectionStringEnvironmentVariable)))
        {
            Skip = $"Set {SqlServerTestHarness.ConnectionStringEnvironmentVariable} to a connection string pointing at a SQL Server admin database (e.g. master) to run these tests.";
        }
    }
}
