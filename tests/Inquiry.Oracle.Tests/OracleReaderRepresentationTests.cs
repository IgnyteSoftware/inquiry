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
        Assert.StartsWith("DateOnly+Date: Execute: InvalidCastException:", rejected[0]);
        Assert.Contains("System.DateOnly", rejected[0]);
        Assert.StartsWith("TimeOnly+Time: Execute: ArgumentException: ORA-50028:", rejected[1]);
        Assert.StartsWith("TimeSpan+Time: Execute: ArgumentException: ORA-50028:", rejected[2]);
        Assert.StartsWith("TimeSpan+Object: Execute: ArgumentException: ORA-50028:", rejected[3]);

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

    [SkippableFact]
    public async Task CharacterizesGuidAndBooleanParameterPairs()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.SkipReason);
        await using var harness = await OracleTestHarness.CreateFromDdlAsync(
            _fixture.AdminConnectionString,
            "CREATE TABLE ParameterPairs (Id NUMBER(10) PRIMARY KEY, Token RAW(16), Enabled NUMBER(1))",
            "paramrepr");
        await using var connection = new OracleConnection(harness.ConnectionString);
        await connection.OpenAsync();

        var token = new Guid("00112233-4455-6677-8899-aabbccddeeff");

        var inferredGuid = await CaptureRejectionAsync(101, "Token", token, null);
        Assert.Equal("Value", inferredGuid.Stage);
        Assert.NotNull(inferredGuid.Error);

        var explicitGuid = await CaptureRejectionAsync(102, "Token", token, DbType.Guid);
        Assert.Equal("DbType", explicitGuid.Stage);
        Assert.NotNull(explicitGuid.Error);

        var inferredBoolean = await CaptureRejectionAsync(103, "Enabled", false, null);
        Assert.Equal("Execute", inferredBoolean.Stage);
        Assert.Contains("ORA-00932", inferredBoolean.Error!.Message);

        var explicitBoolean = await CaptureRejectionAsync(104, "Enabled", true, DbType.Boolean);
        Assert.Equal("Execute", explicitBoolean.Stage);
        Assert.Contains("ORA-00932", explicitBoolean.Error!.Message);

        await InsertAcceptedPairAsync(1, false, metadataBeforeValue: true);
        await InsertAcceptedPairAsync(2, true, metadataBeforeValue: false);

        await using var select = connection.CreateCommand();
        select.CommandText = "SELECT Token, RAWTOHEX(Token), Enabled FROM ParameterPairs ORDER BY Id";
        await using var reader = await select.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync());
        Assert.Equal(token, reader.GetGuid(0));
        Assert.Equal("33221100554477668899AABBCCDDEEFF", reader.GetString(1));
        Assert.Equal((short)0, reader.GetInt16(2));
        Assert.False(reader.GetBoolean(2));

        Assert.True(await reader.ReadAsync());
        Assert.Equal(token, reader.GetGuid(0));
        Assert.Equal("33221100554477668899AABBCCDDEEFF", reader.GetString(1));
        Assert.Equal((short)1, reader.GetInt16(2));
        Assert.True(reader.GetBoolean(2));
        Assert.False(await reader.ReadAsync());

        async Task<(string Stage, Exception? Error)> CaptureRejectionAsync(
            int id,
            string column,
            object value,
            DbType? dbType)
        {
            await using var command = connection.CreateCommand();
            command.BindByName = true;
            command.CommandText = $"INSERT INTO ParameterPairs (Id, {column}) VALUES ({id}, :p)";
            var parameter = command.CreateParameter();
            parameter.ParameterName = "p";

            if (dbType.HasValue)
            {
                try { parameter.DbType = dbType.Value; }
                catch (Exception exception) { return ("DbType", exception); }
            }

            try { parameter.Value = value; }
            catch (Exception exception) { return ("Value", exception); }

            command.Parameters.Add(parameter);
            try { await command.ExecuteNonQueryAsync(); }
            catch (Exception exception) { return ("Execute", exception); }
            return ("Accepted", null);
        }

        async Task InsertAcceptedPairAsync(int id, bool enabled, bool metadataBeforeValue)
        {
            await using var command = connection.CreateCommand();
            command.BindByName = true;
            command.CommandText = $"INSERT INTO ParameterPairs (Id, Token, Enabled) VALUES ({id}, :token, :enabled)";

            var tokenParameter = command.CreateParameter();
            tokenParameter.ParameterName = "token";
            tokenParameter.DbType = DbType.Binary;
            tokenParameter.Value = token;
            Assert.Equal(token, Assert.IsType<Guid>(tokenParameter.Value));
            command.Parameters.Add(tokenParameter);

            var enabledParameter = command.CreateParameter();
            enabledParameter.ParameterName = "enabled";
            if (metadataBeforeValue) enabledParameter.DbType = DbType.Int32;
            enabledParameter.Value = enabled;
            if (!metadataBeforeValue) enabledParameter.DbType = DbType.Int32;
            Assert.Equal(enabled, Assert.IsType<bool>(enabledParameter.Value));
            command.Parameters.Add(enabledParameter);

            Assert.Equal(1, await command.ExecuteNonQueryAsync());
        }
    }

}
