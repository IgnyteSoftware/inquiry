namespace Inquiry.MySql.Tests.Fixtures;

/// <summary>
/// xUnit fact that runs only when <see cref="MySqlTestHarness.ConnectionStringEnvironmentVariable"/>
/// is set. Lets the MySQL/MariaDB integration suite live in the solution without forcing every
/// developer (or every CI job) to stand up a MySQL instance to build green.
/// </summary>
public sealed class MySqlFactAttribute : FactAttribute
{
    public MySqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(MySqlTestHarness.ConnectionStringEnvironmentVariable)))
        {
            Skip = $"Set {MySqlTestHarness.ConnectionStringEnvironmentVariable} to a connection string pointing at a MySQL/MariaDB admin database to run these tests.";
        }
    }
}
