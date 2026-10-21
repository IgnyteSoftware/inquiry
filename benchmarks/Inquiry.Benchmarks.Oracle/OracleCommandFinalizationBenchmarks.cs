using System.Text;
using BenchmarkDotNet.Attributes;
using Inquiry.Oracle;
using Inquiry.Oracle.Shared;
using Oracle.ManagedDataAccess.Client;

namespace Inquiry.Benchmarks.Oracle;

[MemoryDiagnoser]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "BenchmarkDotNet disposes the command in the [GlobalCleanup] method.")]
public class OracleCommandFinalizationBenchmarks
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "S2068", Justification = "The benchmark never opens this connection; the password is a placeholder, not a credential.")]
    private readonly OracleInquiryConnectionFactory _factory =
        new("User Id=benchmark;Password=benchmark;Data Source=benchmark");
    private OracleCommand _command = null!;

    [Params(0, 1, 8)]
    public int ParameterCount { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        var parameterNames = Enumerable.Range(0, ParameterCount)
            .Select(static index => OracleBindName.Encode($"Value{index}"))
            .ToArray();

        var sql = new StringBuilder("SELECT 1 FROM dual");
        for (var index = 0; index < parameterNames.Length; index++)
        {
            sql.Append(index == 0 ? " WHERE " : " AND ")
                .Append('C').Append(index)
                .Append(" = :").Append(parameterNames[index]);
        }
        _command = new OracleCommand(sql.ToString());
        foreach (var parameterName in parameterNames)
        {
            _command.Parameters.Add(new OracleParameter(parameterName, 1));
        }
    }

    [Benchmark]
    public void FinalizeCommand() => _factory.FinalizeCommand(_command);

    [GlobalCleanup]
    public void GlobalCleanup() => _command.Dispose();
}
