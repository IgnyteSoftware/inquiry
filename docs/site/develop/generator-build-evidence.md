# Generator build and edit evidence

Issue #396 remains open. This work supplies repeatable measurements and a
[comparison research note](generator-benchmark-comparisons.md). It does not accept
a supported consumer scale, numeric release budget, or candidate disposition.

## Fixture and measurement boundaries

`benchmarks/Inquiry.Benchmarks.Generators` constructs SQLite consumers with 25,
100, or 500 entities and the same number of stores. Each entity has four mapped
properties and a global filter. Each store has one eager streaming query. One
parent has a collection relation to another entity. There are no DTOs or
projections in this fixture. It is intentionally sparse, not equivalent to a
relationship-rich production model.

The fixture emits 52, 202, and 1,002 source files respectively. Setup validates
generator diagnostics and the resulting compilation, then compares each edited
primed-driver result with fresh generation. Validation is outside timing.

The four BenchmarkDotNet operations are:

- Fresh generation on a prepared compilation with a new driver.
- An unrelated constant edit.
- A mapped-column edit in one model.
- A related foreign-key column and global-filter edit in that model.

Every edit invocation starts from the same unchanged primed driver. Reassigning
the driver after an invocation would measure repeated unchanged-input cache hits
instead of the requested edit. Fresh generation is warm-process generation: it
does not include parsing the original compilation, process launch, or cold JIT.
The microbenchmark uses Roslyn 4.14.0, pinned for both the benchmark project and
BenchmarkDotNet's generated project. Product analyzer dependencies are unchanged.

The separate `--build-evidence` command creates actual consumer projects in a new
directory. It restores outside measurement and records Release `dotnet build
--no-restore` wall time with compiler-server reuse disabled. Three independent
project directories per entity count each supply a clean build and three edited
builds. Edits reset to a compiled baseline outside timing. Each measured build
must succeed, and its generated files must equal fresh-driver output byte for
byte after text decoding. Compiler-input ordering matches the SDK file ordering.

Each repetition also compiles a control project with the identical generated
source captured in advance and no analyzer. This clean-build control includes
compiling generated C#, but excludes generation. It is not an external ORM
comparison or an edited-control measurement. Build processes are fresh; OS and
filesystem caches are not flushed. These command-line builds do not retain IDE
generator state and do not measure editor responsiveness.

## Local diagnostic results

The retained reports use Windows 11, AMD Ryzen 7 9800X3D, SDK 10.0.400, and .NET
10.0.11. Other development work ran on the host. Twelve ShortRun cases produced
three measured iterations each, with allocations. Large confidence intervals,
especially at 500 entities, prohibit a scalability pass or precise regression
claim. The raw build report records independent wall-clock samples and asset
hashes. It is not a clean-host candidate report.

| Entities | Fresh generation ms | Unrelated edit ms | Model edit ms | Relation/filter edit ms | Source files |
| --- | ---: | ---: | ---: | ---: | ---: |
| 25 | 2.75 | 0.89 | 4.29 | 3.58 | 52 |
| 100 | 6.95 | 1.98 | 5.64 | 6.70 | 202 |
| 500 | 100.31 | 6.99 | 25.72 | 103.49 | 1,002 |

At 500 entities, fresh generation allocated 115,800,482 bytes, the unrelated edit
9,873,755 bytes, the model edit 115,127,414 bytes, and the relation/filter edit
115,231,091 bytes. Allocation and latency deserve separate review. These numbers
do not prove which generator stage causes the work; larger restructuring remains
in #182.

The real-build report contains 45 samples. Median wall times in milliseconds from
three independent project directories are below. The control always ran before
its paired generated build; order and host noise limit causal interpretation.

| Entities | Emitted-source control | Clean build | Unrelated edit build | Model edit build | Relation/filter edit build |
| --- | ---: | ---: | ---: | ---: | ---: |
| 25 | 1,470.81 | 1,592.09 | 1,556.72 | 1,574.17 | 1,576.85 |
| 100 | 1,674.97 | 1,884.28 | 1,828.15 | 1,852.70 | 1,830.83 |
| 500 | 2,669.78 | 3,597.04 | 3,486.43 | 3,555.61 | 3,432.16 |

Raw reports and SHA-256 checksums are in
[`benchmarks/evidence/issue-396-generator`](https://github.com/IgnyteSoftware/inquiry/tree/main/benchmarks/evidence/issue-396-generator).

## Reproduce

From the repository root:

```powershell
dotnet run -c Release -f net10.0 --project benchmarks/Inquiry.Benchmarks.Generators -- --filter "*GeneratorBenchmarks*" --job Short --exporters json --artifacts artifacts/generator-microbenchmarks
dotnet run -c Release -f net10.0 --project benchmarks/Inquiry.Benchmarks.Generators -- --build-evidence D:/scratch/inquiry-generator-build-evidence
```

Use a new directory for build evidence. The command refuses to overwrite an
existing path. It retains consumer sources and build logs there; archive them
with `build-evidence.json`. Both commands also support `-f net8.0`. Omit
`--job Short` for the default BenchmarkDotNet configuration. `--job Dry` checks
execution only. Require twelve populated statistics and memory records before
using an exported microbenchmark report; a zero process exit code alone is not
enough. Require 45 build samples: three sizes, three repetitions, and five
scenarios including the emitted-source control.

## Remaining release evidence

- Approve the consumer scale and shape. The research proposes 100 entities as
  the primary workload, with 25 small and 500 stress cases; that is not accepted.
- Collect matched Dapper AOT command/materializer comparisons and a
  relationship-rich consumer on a quiet reference host. Entity counts alone do
  not equalize ORM generation work.
- Propose absolute and baseline-regression limits from those measurements, then
  obtain acceptance. No portable industry latency limit was found.
- Verify the candidate against accepted limits with retained raw evidence.
  Capture actual editor behavior separately before making any IDE latency claim.

Diagnostic results and research do not close #396 or waive #393.
