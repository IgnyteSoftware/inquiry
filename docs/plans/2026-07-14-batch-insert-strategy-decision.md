# Batch insert strategy diagnostic decision

Issue: #180

Date: 2026-07-14

Scope: diagnostic strategy selection only; this is not release-grade benchmark evidence.

## Decision

- On the normal non-intercepted path, SQLite generated `InsertAll` uses one fixed row command, reuses
  its parameters, and requests one preparation when `PrepareStatements` is `Auto`, including
  `DEFAULT VALUES` entities with no bound columns. Active interceptors instead receive one lifecycle
  per physical row command; that path does not use the descriptor preparation hint.
- On the normal non-intercepted path, SQL Server generated `InsertAll` uses the existing end-to-end
  multi-row command below 250 rows only when both the configured and generated set-based parameter
  limits fit the chunk. It uses exact generated `DbBatch` commands otherwise. The outer chunk keeps
  the 1,000-row SQL Server bound instead of applying an aggregate parameter cap intended for one
  set-based command. If `DbBatch` is unavailable or unsupported, execution falls back to the reused
  row command. Active interceptors never use `DbBatch`: they receive one lifecycle for each selected
  physical chunk command or, on the row path, one lifecycle per item; the descriptor preparation hint
  is not used on those interceptor paths.
- SQL Server `DEFAULT VALUES` has no multi-row form. It therefore uses the fixed row descriptor:
  `DbBatch` without interceptors, per-row command lifecycles with active interceptors, and reused-command
  fallback when `DbBatch` is unavailable.
- PostgreSQL, MySQL, MariaDB, and Oracle retain their existing generated strategies.

The 250-row SQL Server cutoff was retained because all three independent hosts reproduced the same
shape: end-to-end multi-row SQL was effectively tied with the selected path at 128 rows, while the
selected `DbBatch` path beat end-to-end multi-row SQL at every measured tier from 250 through 1,000.
Microsoft.Data.SqlClient 7.0.1 sends each `DbBatchCommand` as a separate `sp_executesql` RPC, so SQL
Server's 2,100-parameter limit applies to each child RPC, not to the aggregate `DbBatch`. Generator,
runtime, and live ten-column tests pin this distinction.

## Environment and commands

- Windows 11 25H2 (10.0.26200.8737)
- AMD Ryzen 7 9800X3D, 8 physical / 16 logical cores
- .NET SDK 10.0.301; .NET runtime 10.0.9; BenchmarkDotNet 0.15.8
- Release, `net10.0`, `win-x64`, server/concurrent GC, high-performance power plan
- Rows: 1, 10, 100, 128, 250, 500, 750, 1,000
- Each measurement: one invocation, three warmups, 30 measured iterations
- Other build, test, and benchmark workers were paused for the full measurement window.

The release benchmark attributes remain `[Params(1, 10, 100, 1000)]`. Apply the checked diagnostic
patch before running the commands below, then reverse it afterward:

```powershell
Get-FileHash benchmarks/evidence/issue-180-diagnostic-rows.patch -Algorithm SHA256
git apply --check benchmarks/evidence/issue-180-diagnostic-rows.patch
git apply benchmarks/evidence/issue-180-diagnostic-rows.patch
# Run the benchmark commands below.
git apply -R benchmarks/evidence/issue-180-diagnostic-rows.patch
```

Expected SHA-256:
`6DE8212AF4494781DF9151C8118D84CB00CBE147E80961446BC6FAFC7F0A8A67`.

SQLite used one BenchmarkDotNet job with three process launches:

```powershell
dotnet run -c Release -f net10.0 -r win-x64 --project benchmarks/Inquiry.Benchmarks/Inquiry.Benchmarks.csproj --no-restore -- --filter "*BatchMutationStrategyBenchmarks*" --anyCategories Insert --iterationCount 30 --warmupCount 3 --launchCount 3 --artifacts "BenchmarkDotNet.Artifacts/issue-180/sqlite"
```

SQL Server used three separate fresh host invocations of this command, changing `run1` to `run2` and
`run3` in the artifact path:

```powershell
dotnet run -c Release -f net10.0 -r win-x64 --project benchmarks/Inquiry.Benchmarks.SqlServer/Inquiry.Benchmarks.SqlServer.csproj --no-restore -- --filter "*BatchMutationStrategyBenchmarks*" --anyCategories Insert --iterationCount 30 --warmupCount 3 --launchCount 1 --inProcess --artifacts "BenchmarkDotNet.Artifacts/issue-180/sqlserver-inprocess-run1"
```

The initially attempted SQL Server out-of-process job is excluded. Its deep generated worktree path
made `Microsoft.Data.SqlClient.SNI.dll` fail with Windows error `0x800700CE` (filename or extension too
long), so all 40 cases were `NA` and no measurement was produced. Docker itself connected and became
ready normally. A future authoritative release run must use a path-safe out-of-process runner.

## SQLite results

The values below are means aggregated by BenchmarkDotNet across three launches. Allocation is managed
allocation per operation.

| Rows | Selected row mean | Selected alloc | End-to-end multi-row mean | Multi-row alloc |
|---:|---:|---:|---:|---:|
| 1 | 4.086 ms | 3.13 KB | 4.095 ms | 3.19 KB |
| 10 | 4.053 ms | 7.14 KB | 4.022 ms | 8.25 KB |
| 100 | 4.184 ms | 44.41 KB | 4.185 ms | 56.84 KB |
| 128 | 4.265 ms | 56.00 KB | 4.349 ms | 73.78 KB |
| 250 | 4.346 ms | 106.52 KB | 4.958 ms | 143.14 KB |
| 500 | 4.780 ms | 210.03 KB | 6.645 ms | 308.05 KB |
| 750 | 5.043 ms | 313.55 KB | 9.884 ms | 455.18 KB |
| 1,000 | 5.483 ms | 417.06 KB | 13.567 ms | 554.96 KB |

The selected generated row path stayed near the direct reused/prepared floor and reduced both time and
allocation versus end-to-end multi-row construction as row counts increased.

## SQL Server results

Each host is a separate invocation. Values are `mean / managed allocation per operation`.

| Rows | Host | Selected | Native `DbBatch` | End-to-end multi-row |
|---:|---:|---:|---:|---:|
| 128 | 1 | 6.230 ms / 132.62 KB | 6.112 ms / 283.46 KB | 6.188 ms / 133.09 KB |
| 128 | 2 | 6.255 ms / 132.90 KB | 6.008 ms / 283.13 KB | 6.215 ms / 132.76 KB |
| 128 | 3 | 6.205 ms / 131.96 KB | 5.992 ms / 283.79 KB | 6.264 ms / 132.15 KB |
| 250 | 1 | 6.908 ms / 545.29 KB | 6.799 ms / 536.48 KB | 8.125 ms / 245.86 KB |
| 250 | 2 | 7.010 ms / 545.01 KB | 6.758 ms / 536.20 KB | 8.171 ms / 246.19 KB |
| 250 | 3 | 6.984 ms / 545.01 KB | 6.777 ms / 536.16 KB | 8.089 ms / 246.19 KB |
| 500 | 1 | 8.338 ms / 1,070.52 KB | 8.199 ms / 1,050.57 KB | 15.192 ms / 461.01 KB |
| 500 | 2 | 8.337 ms / 1,070.80 KB | 8.061 ms / 1,052.26 KB | 15.067 ms / 460.73 KB |
| 500 | 3 | 8.342 ms / 1,070.84 KB | 8.141 ms / 1,054.18 KB | 15.047 ms / 461.01 KB |
| 1,000 | 1 | 11.846 ms / 2,122.12 KB | 11.467 ms / 2,089.27 KB | 42.297 ms / 877.63 KB |
| 1,000 | 2 | 11.577 ms / 2,122.12 KB | 10.944 ms / 2,090.53 KB | 41.887 ms / 877.63 KB |
| 1,000 | 3 | 11.234 ms / 2,122.12 KB | 11.055 ms / 2,089.55 KB | 41.807 ms / 877.35 KB |

The selected path tracks the direct native `DbBatch` floor closely at and above 250 rows. Its higher
allocation than multi-row SQL is accepted for this cutoff because the measured latency advantage is
repeatable and grows from about 15% at 250 rows to about 3.6x at 1,000 rows.
