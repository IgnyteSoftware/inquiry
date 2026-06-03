using Oracle.ManagedDataAccess.Client;

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
    public void UserAuthoredDeclareBlockWithoutRcDoesNotGainAnyParameter()
    {
        var factory = new OracleInquiryConnectionFactory("User Id=x;Password=x;Data Source=x");
        using var command = new OracleCommand("DECLARE x NUMBER; BEGIN SELECT COUNT(*) INTO x FROM T; END;");

        factory.FinalizeCommand(command);

        Assert.Empty(command.Parameters.Cast<OracleParameter>());
    }
}
