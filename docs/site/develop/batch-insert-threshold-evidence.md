# SQL Server batch insert threshold evidence

Issue: #393. Run date: 9 September 2026.

## Outcome

All 39 diagnostic benchmark cases completed with affected-row checks and allocation measurements:
12 two-column comparisons at 249/250/251 rows, and 27 ten-column comparisons at
199/200/201, 209/210/211, and 249/250/251 rows. The ten-column cases also verified each persisted
value after every iteration, outside the measured method.

This is developer-workstation evidence, not release-authoritative evidence. No selected-strategy
manifest cell was promoted and no production strategy was changed. #393 remains open until the
remaining candidate evidence is collected or a maintainer explicitly accepts a release disposition.

## What was measured

The two-column benchmark delegates to the existing selected, reused prepared, native DbBatch, and
end-to-end multi-row insert paths. Input construction and database reset stay outside the measurement.

The ten-column entity contains an integer key and nine integer columns. Every column varies by row.
Both direct paths bind all ten item values per row, matching the selected path's payload. Comparisons
open a connection and transaction, execute the inserts, and commit. The generated path includes
Inquiry's normal dispatch and chunk handling; the direct paths omit those layers.

This measures width in mapped columns and parameter count, not large strings, LOBs, or row-size limits.
Inquiry's default parameter budget is 2,000, so 199/200/201 rows straddle the effective ten-column
set-based boundary. The 209/210/211 cases surround SQL Server's 2,100-parameter ceiling, but already
exceed Inquiry's default budget. The 249/250/251 cases surround the adaptive insert row threshold.
The fixture retains default Inquiry options.

| Shape | Rows | Selected mean | Selected allocated bytes |
| --- | ---: | ---: | ---: |
| Two columns | 249 | 9.925 ms | 243208 |
| Two columns | 250 | 6.867 ms | 557896 |
| Two columns | 251 | 6.703 ms | 559688 |
| Ten columns | 199 | 39.989 ms | 877496 |
| Ten columns | 200 | 41.569 ms | 883808 |
| Ten columns | 201 | 6.766 ms | 931768 |
| Ten columns | 209 | 6.950 ms | 967736 |
| Ten columns | 210 | 7.041 ms | 971896 |
| Ten columns | 211 | 7.099 ms | 975384 |
| Ten columns | 249 | 7.414 ms | 1146232 |
| Ten columns | 250 | 7.057 ms | 1152408 |
| Ten columns | 251 | 7.959 ms | 1156904 |

The full reports retain every comparison, distribution, and allocation count. The two-column selected
path allocated more at 250 rows than at 249. The ten-column selected path's mean fell between
200 and 201 rows. These observations warrant controlled repetition; they do not establish an optimal
threshold or justify a production change on their own. Several confidence intervals are wide.

## Reproduction and retained bytes

Measured source: [afec7fd4c18c07a29e52526cef6cc2f47c79d54b](https://github.com/IgnyteSoftware/inquiry/commit/afec7fd4c18c07a29e52526cef6cc2f47c79d54b).

Environment: Windows 11 25H2 build 26200.9168, AMD Ryzen 7 9800X3D, .NET SDK 10.0.400,
.NET runtime 10.0.11, BenchmarkDotNet 0.15.8, Microsoft.Data.SqlClient 7.0.2, Docker Desktop,
and the repository-pinned SQL Server 2022 CU14 image. Runtime capability probing reported
`SqlConnection.CanCreateBatch = true`.

The diagnostic job uses Release/net10.0/win-x64, an in-process toolchain, one launch, two warmups,
five measured iterations, and one invocation per iteration. BenchmarkDotNet's normal outlier handling
remains enabled; retained JSON includes raw measurements as well as summary statistics.

```powershell
dotnet build benchmarks/Inquiry.Benchmarks.SqlServer/Inquiry.Benchmarks.SqlServer.csproj -c Release -r win-x64
dotnet run --project benchmarks/Inquiry.Benchmarks.SqlServer/Inquiry.Benchmarks.SqlServer.csproj -c Release -f net10.0 -r win-x64 --no-build -- --filter "*BatchInsertThresholdBenchmarks*" --iterationCount 5 --warmupCount 2 --launchCount 1 --inProcess --exporters json --artifacts BenchmarkDotNet.Artifacts/issue-393-complete
```

Retained full BenchmarkDotNet JSON:

- [Two-column report](https://github.com/IgnyteSoftware/inquiry/blob/main/benchmarks/evidence/issue-393-thresholds/narrow-report.json)
- [Ten-column report](https://github.com/IgnyteSoftware/inquiry/blob/main/benchmarks/evidence/issue-393-thresholds/wide-report.json)
- [SHA256SUMS](https://github.com/IgnyteSoftware/inquiry/blob/main/benchmarks/evidence/issue-393-thresholds/SHA256SUMS)

The retained files preserve report content with one final LF. Their directory pins LF checkout endings
so SHA-256 checks remain valid across hosts.

```powershell
Get-Content benchmarks/evidence/issue-393-thresholds/SHA256SUMS
Get-ChildItem benchmarks/evidence/issue-393-thresholds/*-report.json | Get-FileHash -Algorithm SHA256
```

## Correctness evidence and remaining gates

The existing nine SQL Server batch-chunking integration cases passed on net8.0, net9.0, and
net10.0 against the pinned full-text CU14 image, with required Docker and zero skips. They cover
affected-row outcomes, rollback after failure/cancellation, ambient transaction ownership, and the
adaptive/wide insert paths. These correctness runs used the same production source as the benchmark.
They do not prove fault-path parity for every direct comparison benchmark.

The [earlier six-provider diagnostic matrix](batch-mutation-diagnostic-matrix.md) retains all
72 selected and 228 comparison measurements at 1/10/100/1,000 rows. Those July reports are historical
diagnostic evidence and have not been rerun here.

Remaining disposition for #393:

- Keep all 72 selected-strategy manifest cells pending. Collect validated, content-addressed selected
  and comparison evidence for the exact candidate, using the checked jobs and required frameworks.
- Recheck affected-row and transaction parity for every provider/comparison pair. This run adds
  SQL Server insert evidence only; it does not refresh all-provider insert/update/delete evidence.
- Repeat threshold and wide-column measurements on the clean candidate host. Large-payload rows,
  other provider thresholds, and hosts without native DbBatch support remain unmeasured here.
- Obtain an explicit maintainer decision before accepting diagnostic evidence in place of the
  release-grade gate. No such acceptance is recorded by this change.
