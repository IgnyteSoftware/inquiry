namespace Inquiry.PostgreSql.Tests.Fixtures;

/// <summary>
/// xUnit fact that runs only when <see cref="PostgreSqlTestHarness.ConnectionStringEnvironmentVariable"/>
/// is set. Lets the PostgreSQL integration suite live in the solution without forcing every
/// developer (or every CI job) to stand up a PostgreSQL instance to build green.
/// </summary>
public sealed class PostgreSqlFactAttribute : FactAttribute
{
    public PostgreSqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(PostgreSqlTestHarness.ConnectionStringEnvironmentVariable)))
        {
            Skip = $"Set {PostgreSqlTestHarness.ConnectionStringEnvironmentVariable} to a connection string pointing at a PostgreSQL admin database (e.g. postgres) to run these tests.";
        }
    }
}
