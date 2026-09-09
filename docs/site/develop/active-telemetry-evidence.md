# Active telemetry evidence

Issue #397 adds measurements for active tracing, metrics, and both together. The
baseline registers telemetry but attaches no listeners. All modes use the same
DI-resolved `DefaultInquiry`, SQLite database, commands, and materializer.

## Workloads and verification

Each query reads ten integer rows. The stream reaches the end of enumeration. The
grid reads both ten-row result sets and disposes the reader. The batch updates ten
rows through SQLite's sequential fallback, toggling a value so database size stays
fixed. Statement preparation is disabled in every mode.

Before timing, setup checks each workload's result and counts stopped activities
and duration measurements. Each query, stream, and grid produces one observation.
The batch produces eleven: one batch observation and ten command observations.
Inactive channels must produce zero observations. Setup then disposes the counting
listeners and attaches no-op callbacks with the same source, instrument, and
sampling configuration. Tracing samples all data and records every activity;
metrics subscribe to `db.client.operation.duration`.

No exporter, tag dictionary, aggregation, logging sink, or counter update runs in
the measured callbacks. Measurements include the database operation and Inquiry's
listener dispatch. They do not estimate OpenTelemetry exporter or collector cost.

## Local measurements

The attached BenchmarkDotNet 0.15.8 ShortRun report contains all sixteen cases,
three warmups and three measured iterations per case. The host used Windows 11,
AMD Ryzen 7 9800X3D, SDK 10.0.400, and .NET 10.0.11. This is a developer-host
diagnostic run, not an isolated release-candidate comparison. Other development
work ran on this host. Wide confidence intervals prevent precise latency-ranking
claims, especially for the batch.

| Workload | Inactive µs / bytes | Tracing µs / bytes | Metrics µs / bytes | Both µs / bytes |
| --- | ---: | ---: | ---: | ---: |
| Buffered query | 15.04 / 2,208 | 15.11 / 3,001 | 16.21 / 2,385 | 15.25 / 3,001 |
| Consumed stream | 14.46 / 2,112 | 15.50 / 2,905 | 15.16 / 2,289 | 15.01 / 2,905 |
| Consumed grid | 17.52 / 3,256 | 17.67 / 3,792 | 17.86 / 3,256 | 18.10 / 3,792 |
| Ten-command batch | 20.34 / 8,848 | 37.45 / 23,746 | 33.94 / 17,138 | 35.92 / 23,746 |

These cases cover SQLite runtime operations. They do not cover native `DbBatch`
providers, generated-command variants, disabled telemetry registration, or a
production exporter. They supply the active-listener measurements requested by
#397; they do not waive #393 or establish a general 1.0 performance pass.

The raw report and SHA-256 checksum are in
[`benchmarks/evidence/issue-397-active-telemetry`](https://github.com/IgnyteSoftware/inquiry/tree/main/benchmarks/evidence/issue-397-active-telemetry).

## Reproduce

From the repository root:

```powershell
dotnet run -c Release -f net10.0 --project benchmarks/Inquiry.Benchmarks -- --filter "*ActiveTelemetryBenchmarks*" --job Short --exporters json --artifacts artifacts/active-telemetry
```

Omit `--job Short` for a longer BenchmarkDotNet run. Use `--job Dry` only to check
fixture execution. A Dry result is not an overhead measurement. Require sixteen
non-null statistics and memory results in the exported JSON; BenchmarkDotNet can
return success even when a benchmark process fails.
