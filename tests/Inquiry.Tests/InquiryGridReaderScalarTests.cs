using Microsoft.Data.Sqlite;

namespace Inquiry.Tests;

public sealed class InquiryGridReaderScalarTests
{
    [Fact]
    public async Task ReadScalarReturnsValueAndAdvancesToNextResultSet()
    {
        await using var connection = CreateConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 42; SELECT 'hello';";
        await using var reader = await command.ExecuteReaderAsync();

        var grid = new InquiryGridReader(reader, command, ownedConnection: null, lease: null);

        var first = await grid.ReadScalarAsync<int>();
        var second = await grid.ReadScalarAsync<string>();

        Assert.Equal(42, first);
        Assert.Equal("hello", second);
    }

    [Fact]
    public async Task ReadScalarReturnsDefaultWhenResultSetIsEmpty()
    {
        await using var connection = CreateConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 WHERE 0 = 1; SELECT 99;";
        await using var reader = await command.ExecuteReaderAsync();

        var grid = new InquiryGridReader(reader, command, ownedConnection: null, lease: null);

        var empty = await grid.ReadScalarAsync<int>();
        var afterEmpty = await grid.ReadScalarAsync<int>();

        Assert.Equal(0, empty);
        Assert.Equal(99, afterEmpty);
    }

    [Fact]
    public async Task ReadScalarReturnsDefaultForNullValue()
    {
        await using var connection = CreateConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT NULL;";
        await using var reader = await command.ExecuteReaderAsync();

        var grid = new InquiryGridReader(reader, command, ownedConnection: null, lease: null);

        var result = await grid.ReadScalarAsync<int?>();

        Assert.Null(result);
    }

    [Fact]
    public async Task ReadScalarCoercesLongToInt()
    {
        await using var connection = CreateConnection();
        await using var command = connection.CreateCommand();
        // SQLite COUNT returns long; verify coercion to int.
        command.CommandText = "SELECT COUNT(*) FROM (SELECT 1 UNION ALL SELECT 2);";
        await using var reader = await command.ExecuteReaderAsync();

        var grid = new InquiryGridReader(reader, command, ownedConnection: null, lease: null);

        var result = await grid.ReadScalarAsync<int>();

        Assert.Equal(2, result);
    }

    [Fact]
    public async Task ReadScalarThrowsAfterAllResultSetsConsumed()
    {
        await using var connection = CreateConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1;";
        await using var reader = await command.ExecuteReaderAsync();

        var grid = new InquiryGridReader(reader, command, ownedConnection: null, lease: null);

        await grid.ReadScalarAsync<int>();

        await Assert.ThrowsAsync<InvalidOperationException>(() => grid.ReadScalarAsync<int>());
    }

    [Fact]
    public async Task ReadScalarReturnsFirstRowWhenMultipleRowsExist()
    {
        await using var connection = CreateConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 10 UNION ALL SELECT 20 UNION ALL SELECT 30;";
        await using var reader = await command.ExecuteReaderAsync();

        var grid = new InquiryGridReader(reader, command, ownedConnection: null, lease: null);

        var result = await grid.ReadScalarAsync<int>();

        Assert.Equal(10, result);
    }

    [Fact]
    public async Task ReadScalarThrowsObjectDisposedAfterDispose()
    {
        await using var connection = CreateConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1;";
        await using var reader = await command.ExecuteReaderAsync();

        var grid = new InquiryGridReader(reader, command, ownedConnection: null, lease: null);
        await grid.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => grid.ReadScalarAsync<int>());
    }

    private static SqliteConnection CreateConnection()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        return connection;
    }
}
