using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;

namespace Inquiry.Benchmarks;

/// <summary>
/// Adds the JIT disassembly report only when <c>INQUIRY_BENCH_DISASM=1</c> is set.
/// </summary>
/// <remarks>
/// The disassembler reads the running benchmark process through the runtime's DAC. On Linux with
/// .NET 8 the DAC can segfault the BenchmarkDotNet host on an address it was given to validate, which
/// stops the whole suite (#516). Measurement runs therefore leave it off.
/// </remarks>
internal sealed class OptInDisassemblyConfig : ManualConfig
{
    public OptInDisassemblyConfig()
    {
        if (Environment.GetEnvironmentVariable("INQUIRY_BENCH_DISASM") == "1")
        {
            AddDiagnoser(new DisassemblyDiagnoser(new DisassemblyDiagnoserConfig(maxDepth: 4, printSource: true)));
        }
    }
}
