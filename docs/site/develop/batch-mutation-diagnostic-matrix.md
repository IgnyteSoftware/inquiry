# Batch mutation diagnostic matrix

Issue: #180

Date: 2026-07-14

Scope: retained diagnostic strategy evidence. This matrix is intentionally **not** authoritative
release evidence; issue #87 owns the clean-host, checked-job, multi-run release gate.

## Outcome

The checked benchmark surfaces now have measured selected and comparison paths for SQLite, SQL
Server, PostgreSQL, MySQL, MariaDB, and Oracle across insert, update, and delete at 1, 10, 100, and
1,000 rows. That is all 72 selected provider/operation/cardinality cells, plus 228 comparison cells.
No report contains an `NA` result.

The measurements support the strategies recorded in
`benchmarks/Inquiry.Benchmarks.Contracts/Evidence/selected-batch-strategy-v1.json`. In particular:

- SQLite's reused generated row path remains close to its direct prepared-command control and avoids
  the allocation growth of end-to-end multi-row SQL at 1,000 rows.
- SQL Server's runtime reported usable `DbBatch`; the selected adaptive path tracks the measured
  native and set-based controls. The more intensive cutoff experiment is recorded in the
  batch-insert strategy decision note, retained in git history.
- PostgreSQL's selected generated path was measured against direct reused commands, native
  `NpgsqlBatch`, array-key delete, and set-based controls.
- MySQL and MariaDB both measured usable `DbBatch` paths and the selected generated paths against
  reused, native, JSON-table, expanded-key, and derived-table controls.
- Oracle's selected generated array-binding path stays near the direct array-binding floor. At 1,000
  rows it measured 5.034 ms versus 570.937 ms for reused insert commands and 209.166 ms for the
  pre-#180 generated `INSERT ... SELECT` control; update measured 23.328 ms versus 563.159 ms for
  reused commands; delete measured 35.656 ms versus 586.252 ms for reused commands. The legacy
  JSON-table delete control was effectively tied at 1,000 rows (35.073 ms), and was slower at 10 and
  100 rows. This exception is retained rather than hidden: native array binding is the clear insert
  and update win and a competitive, single generated mutation mechanism for delete.

These are end-to-end strategy comparisons, so selected Inquiry methods include generated chunk
reading and pipeline dispatch that direct-driver floors intentionally exclude. A selected method is
not expected to beat every lower-level floor in every noisy diagnostic cell.

## Reproduction

Environment:

- Windows 11 25H2, build 10.0.26200.8737
- AMD Ryzen 7 9800X3D, 8 physical / 16 logical cores
- .NET SDK 10.0.301, .NET runtime 10.0.9, BenchmarkDotNet 0.15.8
- Release, `net10.0`, `win-x64`, in-process toolchain
- one launch, two warmup iterations, five measured iterations, one invocation per iteration
- containers: SQL Server 2022 CU14, PostgreSQL 16 Alpine, MySQL 8.0, MariaDB 11.4, and Oracle XE 21

For each project below, restore the Windows assets and run the same bounded diagnostic job:

```powershell
dotnet restore <project> -r win-x64
dotnet run -c Release -f net10.0 -r win-x64 --project <project> --no-restore -- --filter "*BatchMutationStrategyBenchmarks*" --iterationCount 5 --warmupCount 2 --launchCount 1 --inProcess --artifacts "BenchmarkDotNet.Artifacts/issue-180-matrix/<provider>"
```

| Provider | Project | Reported benchmarks |
|---|---|---:|
| SQLite | `benchmarks/Inquiry.Benchmarks/Inquiry.Benchmarks.csproj` | 40 |
| SQL Server | `benchmarks/Inquiry.Benchmarks.SqlServer/Inquiry.Benchmarks.SqlServer.csproj` | 52 |
| PostgreSQL | `benchmarks/Inquiry.Benchmarks.PostgreSql/Inquiry.Benchmarks.PostgreSql.csproj` | 40 |
| MySQL | `benchmarks/Inquiry.Benchmarks.MySql/Inquiry.Benchmarks.MySql.csproj` | 56 |
| MariaDB | `benchmarks/Inquiry.Benchmarks.MariaDb/Inquiry.Benchmarks.MariaDb.csproj` | 56 |
| Oracle | `benchmarks/Inquiry.Benchmarks.Oracle/Inquiry.Benchmarks.Oracle.csproj` | 56 |

The human-readable and machine-readable reports are retained under
`benchmarks/evidence/issue-180-diagnostic-matrix/`. Verify their bytes with:

```powershell
Get-Content benchmarks/evidence/issue-180-diagnostic-matrix/SHA256SUMS
Get-ChildItem benchmarks/evidence/issue-180-diagnostic-matrix/*-report.* | Get-FileHash -Algorithm SHA256
```

## Selected-cell results

The full reports contain every comparison. This table makes the 72 selected cells explicit.

| Provider | Operation | Rows | Mean | Managed allocation |
|---|---|---:|---:|---:|
| sqlite | Delete | 1 | 4.571 ms | 3.07 KB |
| sqlite | Delete | 10 | 10.623 ms | 8.55 KB |
| sqlite | Delete | 100 | 6.456 ms | 12.28 KB |
| sqlite | Delete | 1000 | 7.013 ms | 73.36 KB |
| sqlite | Insert | 1 | 4.405 ms | 8.08 KB |
| sqlite | Insert | 10 | 6.221 ms | 11.99 KB |
| sqlite | Insert | 100 | 4.479 ms | 45.04 KB |
| sqlite | Insert | 1000 | 7.203 ms | 375.51 KB |
| sqlite | Update | 1 | 4.226 ms | 8.36 KB |
| sqlite | Update | 10 | 4.230 ms | 11.95 KB |
| sqlite | Update | 100 | 4.274 ms | 44.99 KB |
| sqlite | Update | 1000 | 5.084 ms | 375.46 KB |
| sqlserver | Delete | 1 | 6.244 ms | 19.7 KB |
| sqlserver | Delete | 10 | 6.182 ms | 28.56 KB |
| sqlserver | Delete | 100 | 6.358 ms | 58.72 KB |
| sqlserver | Delete | 1000 | 9.589 ms | 361.09 KB |
| sqlserver | Insert | 1 | 5.467 ms | 22.46 KB |
| sqlserver | Insert | 10 | 5.019 ms | 31.13 KB |
| sqlserver | Insert | 100 | 6.266 ms | 111.52 KB |
| sqlserver | Insert | 1000 | 12.971 ms | 2123.46 KB |
| sqlserver | Update | 1 | 5.429 ms | 23.46 KB |
| sqlserver | Update | 10 | 5.407 ms | 42.4 KB |
| sqlserver | Update | 100 | 6.110 ms | 231.25 KB |
| sqlserver | Update | 1000 | 12.744 ms | 2114.83 KB |
| postgresql | Delete | 1 | 1.412 ms | 4.66 KB |
| postgresql | Delete | 10 | 1.538 ms | 10.54 KB |
| postgresql | Delete | 100 | 1.719 ms | 10.52 KB |
| postgresql | Delete | 1000 | 2.812 ms | 18.2 KB |
| postgresql | Insert | 1 | 1.450 ms | 10.91 KB |
| postgresql | Insert | 10 | 1.521 ms | 18.23 KB |
| postgresql | Insert | 100 | 1.950 ms | 95.68 KB |
| postgresql | Insert | 1000 | 4.774 ms | 983.28 KB |
| postgresql | Update | 1 | 1.364 ms | 10.48 KB |
| postgresql | Update | 10 | 1.462 ms | 19.47 KB |
| postgresql | Update | 100 | 2.436 ms | 114.37 KB |
| postgresql | Update | 1000 | 12.781 ms | 1053.47 KB |
| mysql | Delete | 1 | 8.822 ms | 43.17 KB |
| mysql | Delete | 10 | 9.003 ms | 48.48 KB |
| mysql | Delete | 100 | 10.899 ms | 52.52 KB |
| mysql | Delete | 1000 | 13.223 ms | 114.2 KB |
| mysql | Insert | 1 | 10.066 ms | 49.12 KB |
| mysql | Insert | 10 | 19.871 ms | 57.01 KB |
| mysql | Insert | 100 | 27.418 ms | 142.62 KB |
| mysql | Insert | 1000 | 52.907 ms | 1070.73 KB |
| mysql | Update | 1 | 21.489 ms | 48.57 KB |
| mysql | Update | 10 | 21.839 ms | 60.13 KB |
| mysql | Update | 100 | 28.783 ms | 155.27 KB |
| mysql | Update | 1000 | 48.832 ms | 1155.27 KB |
| mariadb | Delete | 1 | 3.648 ms | 42.81 KB |
| mariadb | Delete | 10 | 3.828 ms | 48.73 KB |
| mariadb | Delete | 100 | 5.475 ms | 52.8 KB |
| mariadb | Delete | 1000 | 7.497 ms | 114.24 KB |
| mariadb | Insert | 1 | 5.232 ms | 48.83 KB |
| mariadb | Insert | 10 | 3.403 ms | 57.02 KB |
| mariadb | Insert | 100 | 5.344 ms | 141.93 KB |
| mariadb | Insert | 1000 | 13.430 ms | 1070.77 KB |
| mariadb | Update | 1 | 3.624 ms | 49.29 KB |
| mariadb | Update | 10 | 4.460 ms | 58.79 KB |
| mariadb | Update | 100 | 5.725 ms | 153.02 KB |
| mariadb | Update | 1000 | 15.185 ms | 1154.55 KB |
| oracle | Delete | 1 | 3.083 ms | 16.87 KB |
| oracle | Delete | 10 | 3.371 ms | 18.12 KB |
| oracle | Delete | 100 | 6.639 ms | 34.63 KB |
| oracle | Delete | 1000 | 35.656 ms | 193.71 KB |
| oracle | Insert | 1 | 2.763 ms | 17.72 KB |
| oracle | Insert | 10 | 2.917 ms | 19.66 KB |
| oracle | Insert | 100 | 3.123 ms | 42.73 KB |
| oracle | Insert | 1000 | 5.034 ms | 273 KB |
| oracle | Update | 1 | 3.115 ms | 17.11 KB |
| oracle | Update | 10 | 3.149 ms | 19.99 KB |
| oracle | Update | 100 | 4.215 ms | 43.02 KB |
| oracle | Update | 1000 | 23.328 ms | 273.28 KB |

## Limitations and release boundary

This was a bounded diagnostic run on a developer workstation. Five samples are useful for strategy
confirmation, but several cells have wide confidence intervals, container startup and host activity
were not isolated, processor affinity was not pinned, and the in-process toolchain is not the checked
release job. Therefore these reports must not be copied into an `accepted` selected-strategy cell or
described as release-authoritative. Issue #87 must replace the pending manifest cells with validated,
content-addressed evidence from its clean-host workflow.
