using BenchmarkDotNet.Attributes;
using Inquiry.Oracle;
using Inquiry.Oracle.Shared;
using Oracle.ManagedDataAccess.Client;
using System.Text;

namespace Inquiry.Benchmarks.Oracle;

[MemoryDiagnoser]
public class OracleCommandFinalizationBenchmarks
{
    private readonly OracleInquiryConnectionFactory _factory =
        new("User Id=benchmark;Password=benchmark;Data Source=benchmark");
    private string[] _parameterNames = null!;
    private OracleCommand _command = null!;

    [Params(0, 1, 8)]
    public int ParameterCount { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        _parameterNames = Enumerable.Range(0, ParameterCount)
            .Select(static index => OracleBindName.Encode($"Value{index}"))
            .ToArray();

        var sql = new StringBuilder("SELECT 1 FROM dual");
        for (var index = 0; index < _parameterNames.Length; index++)
        {
            sql.Append(index == 0 ? " WHERE " : " AND ")
                .Append('C').Append(index)
                .Append(" = :").Append(_parameterNames[index]);
        }
        _command = new OracleCommand(sql.ToString());
        foreach (var parameterName in _parameterNames)
        {
            _command.Parameters.Add(new OracleParameter(parameterName, 1));
        }
    }

    [Benchmark]
    public void FinalizeCommand() => _factory.FinalizeCommand(_command);

    [GlobalCleanup]
    public void GlobalCleanup() => _command.Dispose();
}
