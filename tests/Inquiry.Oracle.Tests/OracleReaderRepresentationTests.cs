using Inquiry.Oracle.Tests.Fixtures;
using Oracle.ManagedDataAccess.Client;
using System.Data;

namespace Inquiry.Oracle.Tests;

[Collection(OracleCollection.Name)]
public sealed class OracleReaderRepresentationTests
{
    private readonly OracleContainerFixture _fixture;
    public OracleReaderRepresentationTests(OracleContainerFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task CharacterizesProviderFieldTypes()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var connection = new OracleConnection(_fixture.AdminConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT CAST(1 AS NUMBER(3)), CAST(2 AS NUMBER(5)), CAST(3 AS NUMBER(10)),
                   CAST(4 AS NUMBER(19)), CAST(1 AS NUMBER(1)), CAST(1.25 AS NUMBER(10,2)),
                   CAST(1 AS BINARY_FLOAT), CAST(1 AS BINARY_DOUBLE), SYS_GUID(), HEXTORAW('0102'),
                   CAST(SYSDATE AS DATE), SYSTIMESTAMP, COUNT(*)
            FROM dual
            """;
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var actual = Enumerable.Range(0, reader.FieldCount)
            .Select(i => reader.GetFieldType(i).FullName!)
            .ToArray();
        Assert.Equal(
        [
            "System.Int16", "System.Int32", "System.Int64", "System.Decimal", "System.Int16",
            "System.Double", "System.Single", "System.Double", "System.Byte[]", "System.Byte[]",
            "System.DateTime", "System.DateTimeOffset", "System.Decimal",
        ], actual);

        Assert.Equal((short)1, reader.GetInt16(0));
        Assert.Equal(2, reader.GetInt32(1));
        Assert.Equal(3L, reader.GetInt64(2));
        Assert.Equal(4m, reader.GetDecimal(3));
        Assert.True(reader.GetBoolean(4));
        Assert.Equal(1.25m, reader.GetDecimal(5));
        Assert.Equal(1f, reader.GetFloat(6));
        Assert.Equal(1d, reader.GetDouble(7));
        Assert.NotEqual(Guid.Empty, reader.GetGuid(8));
        Assert.Equal(new byte[] { 1, 2 }, reader.GetFieldValue<byte[]>(9));
        _ = reader.GetDateTime(10);
        _ = reader.GetFieldValue<DateTimeOffset>(11);
        Assert.Equal(1L, reader.GetInt64(12));
    }

    [SkippableFact]
    public async Task CharacterizesTemporalParameterPairs()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);

        var rejected = new[]
        {
            await CaptureRejectionAsync("DateOnly+Date", new DateOnly(2024, 2, 29), DbType.Date),
            await CaptureRejectionAsync("TimeOnly+Time", new TimeOnly(12, 34, 56), DbType.Time),
            await CaptureRejectionAsync("TimeSpan+Time", TimeSpan.FromHours(12), DbType.Time),
            await CaptureRejectionAsync("TimeSpan+Object", TimeSpan.FromHours(12), DbType.Object),
        };
        Assert.Equal(
        [
            "DateOnly+Date: Execute: InvalidCastException: Unable to cast object of type 'System.DateOnly' to type 'System.IConvertible'.",
            "TimeOnly+Time: Execute: ArgumentException: ORA-50028: Invalid parameter binding (Parameter 'ParameterName')",
            "TimeSpan+Time: Execute: ArgumentException: ORA-50028: Invalid parameter binding (Parameter 'ParameterName')",
            "TimeSpan+Object: Execute: ArgumentException: ORA-50028: Invalid parameter binding",
        ], rejected);

        await ExecuteAsync(new DateTime(2024, 2, 29), DbType.Date);
        await ExecuteAsync(TimeSpan.FromTicks(452_961_234_570), null);
        await ExecuteAsync(new DateTimeOffset(2024, 2, 29, 12, 34, 56, TimeSpan.FromMinutes(-270)), DbType.DateTimeOffset);

        async Task ExecuteAsync(object value, DbType? dbType)
        {
            await using var connection = new OracleConnection(_fixture.AdminConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT :p FROM dual";
            var parameter = command.CreateParameter();
            parameter.ParameterName = "p";
            if (dbType.HasValue) parameter.DbType = dbType.Value;
            parameter.Value = value;
            command.Parameters.Add(parameter);
            await command.ExecuteScalarAsync();
        }

        async Task<string> CaptureRejectionAsync(string name, object value, DbType dbType)
        {
            await using var connection = new OracleConnection(_fixture.AdminConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT :p FROM dual";
            var parameter = command.CreateParameter();
            parameter.ParameterName = "p";
            try { parameter.DbType = dbType; }
            catch (Exception exception) { return $"{name}: DbType: {exception.GetType().Name}: {exception.Message}"; }
            try { parameter.Value = value; }
            catch (Exception exception) { return $"{name}: Value: {exception.GetType().Name}: {exception.Message}"; }
            command.Parameters.Add(parameter);
            try { await command.ExecuteScalarAsync(); }
            catch (Exception exception) { return $"{name}: Execute: {exception.GetType().Name}: {exception.Message}"; }
            return $"{name}: accepted unexpectedly";
        }
    }

}
