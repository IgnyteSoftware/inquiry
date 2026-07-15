using Oracle.ManagedDataAccess.Client;
using Inquiry.Oracle.Shared;

namespace Inquiry.Oracle.Tests;

/// <summary>
/// Audit P2 #7: the Oracle factory previously detected "this is a generator-emitted returning block
/// that needs an OUT ref-cursor parameter" by sniffing the leading keyword of the command text —
/// "BEGIN" or "DECLARE". Any user-authored ad-hoc PL/SQL that happens to start with those tokens
/// (e.g. <c>BEGIN INSERT ... END;</c> or a DECLARE with locals) was wrongly classified, gaining a
/// stray <c>rc</c> OUT parameter that broke execution.
///
/// The factory now also requires the literal bind reference <c>:rc</c> in the command text. The
/// generator's returning SQL always contains <c>OPEN :rc FOR SELECT ...</c>, so it's reliably
/// detected; user PL/SQL that doesn't reference the synthetic <c>:rc</c> bind is left alone.
/// </summary>
public sealed class OracleRefCursorDetectionTests
{
    [Fact]
    public void GeneratedLeadingUnderscoreBindNamesAreMappedWithoutCollapsing()
    {
        var factory = new OracleInquiryConnectionFactory("User Id=x;Password=x;Data Source=x");
        var offset = OracleBindName.Encode("offset");
        var leading = OracleBindName.Encode("_offset");
        var doubleLeading = OracleBindName.Encode("__offset");
        using var command = new OracleCommand(
            $"SELECT * FROM T WHERE OffsetValue = :{offset} AND LeadingOffset = :{leading} AND DoubleLeadingOffset = :{doubleLeading}");
        command.Parameters.Add(new OracleParameter(offset, 1));
        command.Parameters.Add(new OracleParameter(leading, 2));
        command.Parameters.Add(new OracleParameter(doubleLeading, 3));
        var originalText = command.CommandText;

        factory.FinalizeCommand(command);

        var names = command.Parameters.Cast<OracleParameter>().Select(static p => p.ParameterName).ToArray();
        Assert.Contains(offset, names);
        Assert.Contains(leading, names);
        Assert.Contains(doubleLeading, names);
        Assert.Equal(3, names.Distinct().Count());
        Assert.Same(originalText, command.CommandText);
    }

    [Fact]
    public void UserAuthoredRawBindTokensAreSafelyRewrittenOutsideQuotedTextAndComments()
    {
        var factory = new OracleInquiryConnectionFactory("User Id=x;Password=x;Data Source=x");
        using var command = new OracleCommand(
            "SELECT @p1, :p10, '@p1', \"@p1\", q'[ :p10 ]', q'{@p1}', owner.table@p1 " +
            "FROM dual -- @p1\n/* :p10 */ WHERE x = :p1 AND y = @p10");
        command.Parameters.Add(new OracleParameter("@p1", 3));
        command.Parameters.Add(new OracleParameter(":p10", 4));

        factory.FinalizeCommand(command);

        var p1 = OracleBindName.Encode("p1");
        var p10 = OracleBindName.Encode("p10");
        Assert.Equal($"SELECT :{p1}, :{p10}, '@p1', \"@p1\", q'[ :p10 ]', q'{{@p1}}', owner.table@p1 " +
            $"FROM dual -- @p1\n/* :p10 */ WHERE x = :{p1} AND y = :{p10}", command.CommandText);
        Assert.Equal(new[] { p1, p10 }, command.Parameters.Cast<OracleParameter>().Select(static p => p.ParameterName));
    }

    [Fact]
    public void EncoderHandlesReservedLongAndCollidingLogicalNames()
    {
        var names = new[] { "Prior", "_Prior", "__Prior", new string('A', 80) + "x", new string('A', 80) + "y" }
            .Select(OracleBindName.Encode)
            .ToArray();

        Assert.All(names, name => Assert.InRange(name.Length, 1, 30));
        Assert.All(names, name => Assert.StartsWith("iq1$", name, StringComparison.Ordinal));
        Assert.Equal(names.Length, names.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void EncoderDoesNotTreatRawEncodedOrBatchShapedLogicalNamesAsAlreadySafe()
    {
        var encoded = OracleBindName.Encode("Key");
        var batchShaped = "iq1$b12_34";

        Assert.True(OracleBindName.IsEncoded(encoded));
        Assert.True(OracleBindName.IsEncoded(batchShaped));
        Assert.NotEqual(encoded, OracleBindName.Encode(encoded));
        Assert.NotEqual(batchShaped, OracleBindName.Encode(batchShaped));
    }

    [Fact]
    public void RawTokenIsRewrittenEvenWhenItsEncodedTargetAlreadyAppearsInSql()
    {
        var factory = new OracleInquiryConnectionFactory("User Id=x;Password=x;Data Source=x");
        var encoded = OracleBindName.Encode("Key");
        using var command = new OracleCommand($"SELECT :Key, :{encoded} FROM dual");
        command.Parameters.Add(new OracleParameter("@Key", 1));

        factory.FinalizeCommand(command);

        Assert.Equal($"SELECT :{encoded}, :{encoded} FROM dual", command.CommandText);
        Assert.Equal(encoded, Assert.Single(command.Parameters.Cast<OracleParameter>()).ParameterName);
    }

    [Fact]
    public void EncodedNamespaceAndCaseInsensitiveCollectionCollisionsAreRejected()
    {
        var factory = new OracleInquiryConnectionFactory("User Id=x;Password=x;Data Source=x");
        var encoded = OracleBindName.Encode("Key");
        using var encodedCollision = new OracleCommand($"SELECT :Key, :{encoded} FROM dual");
        encodedCollision.Parameters.Add(new OracleParameter("@Key", 1));
        encodedCollision.Parameters.Add(new OracleParameter(encoded, 2));
        Assert.Throws<InvalidOperationException>(() => factory.FinalizeCommand(encodedCollision));

        using var caseCollision = new OracleCommand("SELECT :Name, :name FROM dual");
        caseCollision.Parameters.Add(new OracleParameter("@Name", 1));
        caseCollision.Parameters.Add(new OracleParameter("@name", 2));
        Assert.Throws<InvalidOperationException>(() => factory.FinalizeCommand(caseCollision));
    }

    [Fact]
    public void LexerRewritesCompleteUnicodeTokensButSkipsQuotedTextCommentsAndDatabaseLinks()
    {
        var factory = new OracleInquiryConnectionFactory("User Id=x;Password=x;Data Source=x");
        const string unicode = "naïve中𐐀é";
        using var command = new OracleCommand(
            $"SELECT :{unicode}, '{unicode}', q'[ :{unicode} ]', nq'{{@{unicode}}}', schema.table@remote, " +
            $"\"schema\".\"table\"@remote, \"schema\".\"table\"@\"remote\" FROM dual -- :{unicode}\n" +
            $"WHERE value = @{unicode} /* :{unicode} */");
        command.Parameters.Add(new OracleParameter("@" + unicode, 1));

        factory.FinalizeCommand(command);

        var safe = OracleBindName.Encode(unicode);
        Assert.Equal(
            $"SELECT :{safe}, '{unicode}', q'[ :{unicode} ]', nq'{{@{unicode}}}', schema.table@remote, " +
            $"\"schema\".\"table\"@remote, \"schema\".\"table\"@\"remote\" FROM dual -- :{unicode}\n" +
            $"WHERE value = :{safe} /* :{unicode} */",
            command.CommandText);
    }

    [Fact]
    public void StoredProcedureFormalParameterNamesOnlyLoseTransportSigils()
    {
        var factory = new OracleInquiryConnectionFactory("User Id=x;Password=x;Data Source=x");
        using var command = new OracleCommand("MY_PACKAGE.DO_WORK") { CommandType = System.Data.CommandType.StoredProcedure };
        command.Parameters.Add(new OracleParameter("@Prior", 1));
        command.Parameters.Add(new OracleParameter(":Total", DBNull.Value) { Direction = System.Data.ParameterDirection.Output });

        factory.FinalizeCommand(command);

        Assert.Equal(new[] { "Prior", "Total" }, command.Parameters.Cast<OracleParameter>().Select(static p => p.ParameterName));
        Assert.Equal("MY_PACKAGE.DO_WORK", command.CommandText);
    }

    [Fact]
    public void GeneratorEmittedBeginBlockWithRcGainsRefCursorOut()
    {
        var factory = new OracleInquiryConnectionFactory("User Id=x;Password=x;Data Source=x");
        using var command = new OracleCommand("BEGIN INSERT INTO T (x) VALUES (1); OPEN :rc FOR SELECT * FROM T WHERE x = 1; END;");

        factory.FinalizeCommand(command);

        Assert.Contains(command.Parameters.Cast<OracleParameter>(),
            p => p.ParameterName == "rc" && p.OracleDbType == OracleDbType.RefCursor && p.Direction == System.Data.ParameterDirection.Output);
    }

    [Fact]
    public void GeneratorEmittedDeclareBlockWithRcGainsRefCursorOut()
    {
        var factory = new OracleInquiryConnectionFactory("User Id=x;Password=x;Data Source=x");
        using var command = new OracleCommand("DECLARE v_key T.id%TYPE; BEGIN INSERT INTO T (x) VALUES (1) RETURNING id INTO v_key; OPEN :rc FOR SELECT * FROM T WHERE id = v_key; END;");

        factory.FinalizeCommand(command);

        Assert.Contains(command.Parameters.Cast<OracleParameter>(),
            p => p.ParameterName == "rc" && p.OracleDbType == OracleDbType.RefCursor);
    }

    [Fact]
    public void UserAuthoredBeginBlockWithoutRcDoesNotGainAnyParameter()
    {
        var factory = new OracleInquiryConnectionFactory("User Id=x;Password=x;Data Source=x");
        using var command = new OracleCommand("BEGIN INSERT INTO T (x) VALUES (1); END;");

        factory.FinalizeCommand(command);

        Assert.Empty(command.Parameters.Cast<OracleParameter>());
    }

    [Fact]
    public void EagerGridReturnResultBlockDoesNotGainRefCursorOut()
    {
        // The eager-load grid command (#70) is a DECLARE block too, but it hands its cursors to the
        // client via DBMS_SQL.RETURN_RESULT and never references :rc — it must not gain the OUT
        // ref-cursor parameter that returning blocks get.
        var factory = new OracleInquiryConnectionFactory("User Id=x;Password=x;Data Source=x");
        using var command = new OracleCommand(
            "DECLARE c SYS_REFCURSOR; BEGIN OPEN c FOR SELECT Id FROM Orders WHERE Id = :Id; DBMS_SQL.RETURN_RESULT(c); OPEN c FOR SELECT ProductId FROM OrderProduct WHERE OrderId = :Id; DBMS_SQL.RETURN_RESULT(c); END;");
        command.Parameters.Add(new OracleParameter("@Id", 1));

        factory.FinalizeCommand(command);

        var parameter = Assert.Single(command.Parameters.Cast<OracleParameter>());
        Assert.Equal(OracleBindName.Encode("Id"), parameter.ParameterName);
    }

    [Fact]
    public void UserAuthoredDeclareBlockWithoutRcDoesNotGainAnyParameter()
    {
        var factory = new OracleInquiryConnectionFactory("User Id=x;Password=x;Data Source=x");
        using var command = new OracleCommand("DECLARE x NUMBER; BEGIN SELECT COUNT(*) INTO x FROM T; END;");

        factory.FinalizeCommand(command);

        Assert.Empty(command.Parameters.Cast<OracleParameter>());
    }
}
