# Generator build and edit evidence

Issue #396 remains open. This work supplies repeatable measurements and a
[comparison research note](generator-benchmark-comparisons.md). It does not accept
a supported consumer scale, numeric release budget, or candidate disposition.

## Fixture and measurement boundaries

`benchmarks/Inquiry.Benchmarks.Generators` constructs SQLite consumers with 10,
25, 100, 500, 1,000, or 5,000 entities and the same number of stores. The
build evidence below uses 25, 100, and 500. Each entity has four mapped
properties and a global filter. Each store has one eager streaming query. One
parent has a collection relation to another entity. There are no DTOs or
projections in this fixture. It is intentionally sparse, not equivalent to a
relationship-rich production model.

The fixture emits two source files per entity plus two, so 52, 202, and 1,002
source files at 25, 100, and 500 entities. Setup validates
generator diagnostics and the resulting compilation, then compares each edited
primed-driver result with fresh generation. Validation is outside timing.

The five BenchmarkDotNet operations are:

- Fresh generation on a prepared compilation with a new driver.
- An unrelated constant edit.
- A mapped-column edit in `Model1`. `Model0` has a relation to `Model1`, so this
  edit also reaches a related model.
- The same mapped-column edit in `Model2`, which no relation reaches.
- A related foreign-key column and global-filter edit in `Model1`.

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

## Materializer and store outputs per model (#182)

Each entity, view, projection, and ad-hoc DTO materializer has its own
generator output, and so does each store. An edit re-emits only the
materializers whose model changed and the stores that read that model. A store
reads its own entity, that entity's relation children and junctions, the
projections its methods return, and its stored-procedure result-set entities.
Relation checks also run per entity. The schema and DI registration are the
only outputs that read the whole model. The schema re-runs after any entity
edit. Registration re-runs only when the set of registered types changes.

The table compares the same microbenchmark before and after the materializer
change, which came first. The store change was measured with the same
harness; those results are internal and are not recorded here. Both
runs used a different host from the #396 results above: Windows 11, 12th Gen
Intel Core i9-12900K, SDK 10.0.403, and .NET 10.0.14. Do not compare these
numbers with the #396 tables. Each number is the ShortRun mean of three
iterations. The latency samples are noisy, and the host was not quiet.
Allocation is the steadier signal.

| Entities | Operation | Before ms | After ms | Before MB | After MB |
| ---: | --- | ---: | ---: | ---: | ---: |
| 10 | Fresh generation | 2.77 | 2.67 | 3.4 | 3.4 |
| 10 | Unrelated edit | 0.91 | 0.85 | 0.9 | 0.9 |
| 10 | Isolated model edit | 3.41 | 2.65 | 3.4 | 3.1 |
| 100 | Fresh generation | 11.52 | 10.14 | 26.5 | 26.7 |
| 100 | Unrelated edit | 3.66 | 3.64 | 2.7 | 2.8 |
| 100 | Isolated model edit | 16.24 | 16.13 | 26.3 | 22.7 |
| 1,000 | Fresh generation | 213.71 | 158.31 | 256.4 | 258.0 |
| 1,000 | Unrelated edit | 32.50 | 24.65 | 20.6 | 20.7 |
| 1,000 | Isolated model edit | 152.27 | 142.59 | 255.3 | 218.1 |
| 5,000 | Fresh generation | 904.45 | 724.32 | 1,279.8 | 1,288.1 |
| 5,000 | Unrelated edit | 295.81 | 286.56 | 101.5 | 101.6 |
| 5,000 | Isolated model edit | 902.23 | 658.83 | 1,273.7 | 1,086.9 |

An isolated model edit allocates about 14% less from 100 entities up. That is
the materializer share of the work. The related and relation/filter edits fall
by the same amount. Fresh generation allocates under 1% more, for the extra
per-model pipeline steps.

These are local diagnostic numbers, not #396 acceptance evidence. The real
consumer build evidence above was not rerun for this change. Both reports,
with all 30 cases and their checksums, are in
[`benchmarks/evidence/issue-182-generator`](https://github.com/IgnyteSoftware/inquiry/tree/main/benchmarks/evidence/issue-182-generator).

## Reproduce

From the repository root:

```powershell
dotnet run -c Release -f net10.0 --project benchmarks/Inquiry.Benchmarks.Generators -- --filter "*GeneratorBenchmarks*" --job Short --exporters json --artifacts artifacts/generator-microbenchmarks
dotnet run -c Release -f net10.0 --project benchmarks/Inquiry.Benchmarks.Generators -- --build-evidence D:/scratch/inquiry-generator-build-evidence
```

Use a new directory for build evidence. The command refuses to overwrite an
existing path. It builds 25, 100 and 500 entities; pass a comma-separated list
after the directory, such as `10,100,1000,5000`, to build other sizes. It retains consumer sources and build logs there; archive them
with `build-evidence.json`. Both commands also support `-f net11.0`. Omit
`--job Short` for the default BenchmarkDotNet configuration. `--job Dry` checks
execution only. Require 30 populated statistics and memory records before
using an exported microbenchmark report; a zero process exit code alone is not
enough. Require 15 build samples for each size: three repetitions of five
scenarios, including the emitted-source control. The default three sizes give 45.

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
