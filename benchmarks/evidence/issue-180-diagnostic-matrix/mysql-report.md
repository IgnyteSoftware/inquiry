```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.8737/25H2/2025Update/HudsonValley2)
AMD Ryzen 7 9800X3D 4.70GHz, 1 CPU, 16 logical and 8 physical cores
.NET SDK 10.0.301
  [Host] : .NET 10.0.9 (10.0.9, 10.0.926.27113), X64 RyuJIT x86-64-v4

Toolchain=InProcessEmitToolchain  InvocationCount=1  IterationCount=5
LaunchCount=1  UnrollFactor=1  WarmupCount=2

```
| Method                      | Categories  | Rows | Mean       | Error     | StdDev     | Median     | Ratio | RatioSD | Allocated  | Alloc Ratio |
|---------------------------- |------------ |----- |-----------:|----------:|-----------:|-----------:|------:|--------:|-----------:|------------:|
| **Inquiry_SelectedDeleteAll**   | **BatchDelete** | **1**    |   **8.822 ms** |  **2.928 ms** |  **0.4531 ms** |   **9.003 ms** |  **1.00** |    **0.07** |   **43.17 KB** |        **1.00** |
| Direct_ReusedPreparedDelete | BatchDelete | 1    |   9.223 ms |  1.843 ms |  0.4787 ms |   9.260 ms |  1.05 |    0.07 |   48.97 KB |        1.13 |
| Native_DbBatchDelete        | BatchDelete | 1    |   8.769 ms |  4.766 ms |  1.2378 ms |   8.940 ms |  1.00 |    0.14 |   46.16 KB |        1.07 |
| Raw_ExpandedInDeleteControl | BatchDelete | 1    |   8.353 ms |  2.053 ms |  0.5330 ms |   8.176 ms |  0.95 |    0.07 |   47.27 KB |        1.09 |
| Raw_JsonTableDeleteControl  | BatchDelete | 1    |   8.345 ms |  2.495 ms |  0.6479 ms |   7.952 ms |  0.95 |    0.08 |   46.59 KB |        1.08 |
|                             |             |      |            |           |            |            |       |         |            |             |
| **Inquiry_SelectedDeleteAll**   | **BatchDelete** | **10**   |   **9.003 ms** |  **1.635 ms** |  **0.4247 ms** |   **9.210 ms** |  **1.00** |    **0.06** |   **48.48 KB** |        **1.00** |
| Direct_ReusedPreparedDelete | BatchDelete | 10   |  13.604 ms |  2.696 ms |  0.4172 ms |  13.751 ms |  1.51 |    0.08 |   63.43 KB |        1.31 |
| Native_DbBatchDelete        | BatchDelete | 10   |   9.766 ms |  3.226 ms |  0.8378 ms |   9.420 ms |  1.09 |    0.10 |   60.56 KB |        1.25 |
| Raw_ExpandedInDeleteControl | BatchDelete | 10   |   9.166 ms |  2.097 ms |  0.3246 ms |   9.110 ms |  1.02 |    0.06 |   49.97 KB |        1.03 |
| Raw_JsonTableDeleteControl  | BatchDelete | 10   |   8.711 ms |  4.760 ms |  1.2362 ms |   8.416 ms |  0.97 |    0.13 |   46.59 KB |        0.96 |
|                             |             |      |            |           |            |            |       |         |            |             |
| **Inquiry_SelectedDeleteAll**   | **BatchDelete** | **100**  |  **10.899 ms** |  **5.170 ms** |  **0.8001 ms** |  **10.718 ms** |  **1.00** |    **0.09** |   **52.52 KB** |        **1.00** |
| Direct_ReusedPreparedDelete | BatchDelete | 100  |  69.473 ms |  9.906 ms |  1.5330 ms |  69.949 ms |  6.40 |    0.43 |  218.35 KB |        4.16 |
| Native_DbBatchDelete        | BatchDelete | 100  |  14.828 ms | 13.147 ms |  2.0346 ms |  15.595 ms |  1.37 |    0.19 |  250.74 KB |        4.77 |
| Raw_ExpandedInDeleteControl | BatchDelete | 100  |  10.077 ms |  7.225 ms |  1.1181 ms |  10.109 ms |  0.93 |    0.11 |   81.97 KB |        1.56 |
| Raw_JsonTableDeleteControl  | BatchDelete | 100  |  10.729 ms |  4.882 ms |  1.2680 ms |  11.072 ms |  0.99 |    0.12 |   50.84 KB |        0.97 |
|                             |             |      |            |           |            |            |       |         |            |             |
| **Inquiry_SelectedDeleteAll**   | **BatchDelete** | **1000** |  **13.223 ms** |  **5.289 ms** |  **0.8185 ms** |  **13.113 ms** |  **1.00** |    **0.08** |   **114.2 KB** |        **1.00** |
| Direct_ReusedPreparedDelete | BatchDelete | 1000 | 613.270 ms | 33.939 ms |  8.8137 ms | 612.719 ms | 46.51 |    2.61 | 1757.58 KB |       15.39 |
| Native_DbBatchDelete        | BatchDelete | 1000 |  60.198 ms | 33.043 ms |  8.5813 ms |  61.352 ms |  4.57 |    0.65 | 1297.73 KB |       11.36 |
| Raw_ExpandedInDeleteControl | BatchDelete | 1000 |  12.010 ms |  3.250 ms |  0.8440 ms |  11.910 ms |  0.91 |    0.08 |  419.09 KB |        3.67 |
| Raw_JsonTableDeleteControl  | BatchDelete | 1000 |  12.427 ms |  5.801 ms |  0.8977 ms |  12.642 ms |  0.94 |    0.08 |  112.23 KB |        0.98 |
|                             |             |      |            |           |            |            |       |         |            |             |
| **Inquiry_SelectedInsertAll**   | **BatchInsert** | **1**    |  **10.066 ms** |  **5.549 ms** |  **1.4411 ms** |   **9.931 ms** |  **1.02** |    **0.19** |   **49.12 KB** |        **1.00** |
| Direct_ReusedPreparedInsert | BatchInsert | 1    |  10.509 ms |  5.002 ms |  1.2990 ms |  11.055 ms |  1.06 |    0.18 |   47.97 KB |        0.98 |
| Native_DbBatchInsert        | BatchInsert | 1    |  19.072 ms | 10.200 ms |  2.6489 ms |  20.136 ms |  1.93 |    0.35 |    46.7 KB |        0.95 |
| Raw_MultiRowInsertControl   | BatchInsert | 1    |  17.240 ms |  9.405 ms |  2.4424 ms |  18.036 ms |  1.74 |    0.32 |   47.43 KB |        0.97 |
|                             |             |      |            |           |            |            |       |         |            |             |
| **Inquiry_SelectedInsertAll**   | **BatchInsert** | **10**   |  **19.871 ms** | **10.242 ms** |  **1.5849 ms** |  **19.833 ms** |  **1.00** |    **0.10** |   **57.01 KB** |        **1.00** |
| Direct_ReusedPreparedInsert | BatchInsert | 10   |  28.105 ms | 16.926 ms |  4.3957 ms |  26.697 ms |  1.42 |    0.23 |   61.55 KB |        1.08 |
| Native_DbBatchInsert        | BatchInsert | 10   |  22.776 ms | 11.367 ms |  1.7590 ms |  22.639 ms |  1.15 |    0.11 |    59.7 KB |        1.05 |
| Raw_MultiRowInsertControl   | BatchInsert | 10   |  19.972 ms | 20.621 ms |  3.1911 ms |  19.634 ms |  1.01 |    0.16 |   54.98 KB |        0.96 |
|                             |             |      |            |           |            |            |       |         |            |             |
| **Inquiry_SelectedInsertAll**   | **BatchInsert** | **100**  |  **27.418 ms** | **40.890 ms** | **10.6191 ms** |  **21.219 ms** |  **1.10** |    **0.51** |  **142.62 KB** |        **1.00** |
| Direct_ReusedPreparedInsert | BatchInsert | 100  |  80.005 ms | 14.138 ms |  3.6716 ms |  81.793 ms |  3.22 |    0.91 |  219.76 KB |        1.54 |
| Native_DbBatchInsert        | BatchInsert | 100  |  35.304 ms | 54.728 ms | 14.2127 ms |  29.738 ms |  1.42 |    0.67 |  218.27 KB |        1.53 |
| Raw_MultiRowInsertControl   | BatchInsert | 100  |  28.364 ms | 39.001 ms | 10.1285 ms |  21.320 ms |  1.14 |    0.50 |  127.88 KB |        0.90 |
|                             |             |      |            |           |            |            |       |         |            |             |
| **Inquiry_SelectedInsertAll**   | **BatchInsert** | **1000** |  **52.907 ms** | **13.637 ms** |  **2.1103 ms** |  **53.179 ms** |  **1.00** |    **0.05** | **1070.73 KB** |        **1.00** |
| Direct_ReusedPreparedInsert | BatchInsert | 1000 | 631.082 ms | 16.532 ms |  2.5583 ms | 632.004 ms | 11.94 |    0.43 | 1766.02 KB |        1.65 |
| Native_DbBatchInsert        | BatchInsert | 1000 |  64.762 ms | 56.696 ms |  8.7738 ms |  64.481 ms |  1.23 |    0.16 | 1511.62 KB |        1.41 |
| Raw_MultiRowInsertControl   | BatchInsert | 1000 |  58.473 ms | 35.515 ms |  5.4959 ms |  58.070 ms |  1.11 |    0.10 |  852.27 KB |        0.80 |
|                             |             |      |            |           |            |            |       |         |            |             |
| **Inquiry_SelectedUpdateAll**   | **BatchUpdate** | **1**    |  **21.489 ms** | **13.718 ms** |  **3.5625 ms** |  **21.719 ms** |  **1.02** |    **0.22** |   **48.57 KB** |        **1.00** |
| Direct_ReusedPreparedUpdate | BatchUpdate | 1    |  21.923 ms |  7.360 ms |  1.9113 ms |  21.816 ms |  1.04 |    0.18 |   48.73 KB |        1.00 |
| Native_DbBatchUpdate        | BatchUpdate | 1    |  20.016 ms |  7.759 ms |  2.0149 ms |  20.007 ms |  0.95 |    0.17 |   47.38 KB |        0.98 |
| Raw_CaseUpdateControl       | BatchUpdate | 1    |  24.762 ms | 19.680 ms |  5.1108 ms |  22.746 ms |  1.18 |    0.29 |   48.13 KB |        0.99 |
| Raw_DerivedTableJoinControl | BatchUpdate | 1    |  19.975 ms |  6.392 ms |  1.6599 ms |  20.212 ms |  0.95 |    0.16 |   48.16 KB |        0.99 |
|                             |             |      |            |           |            |            |       |         |            |             |
| **Inquiry_SelectedUpdateAll**   | **BatchUpdate** | **10**   |  **21.839 ms** |  **3.688 ms** |  **0.5708 ms** |  **21.944 ms** |  **1.00** |    **0.03** |   **60.13 KB** |        **1.00** |
| Direct_ReusedPreparedUpdate | BatchUpdate | 10   |  27.403 ms | 11.667 ms |  3.0299 ms |  28.771 ms |  1.26 |    0.13 |   65.52 KB |        1.09 |
| Native_DbBatchUpdate        | BatchUpdate | 10   |  20.642 ms |  4.893 ms |  0.7571 ms |  20.641 ms |  0.95 |    0.04 |   63.13 KB |        1.05 |
| Raw_CaseUpdateControl       | BatchUpdate | 10   |  21.102 ms |  4.792 ms |  0.7416 ms |  21.281 ms |  0.97 |    0.04 |   57.02 KB |        0.95 |
| Raw_DerivedTableJoinControl | BatchUpdate | 10   |  20.191 ms | 15.862 ms |  2.4546 ms |  20.173 ms |  0.93 |    0.10 |   58.55 KB |        0.97 |
|                             |             |      |            |           |            |            |       |         |            |             |
| **Inquiry_SelectedUpdateAll**   | **BatchUpdate** | **100**  |  **28.783 ms** | **41.441 ms** | **10.7621 ms** |  **22.133 ms** |  **1.10** |    **0.49** |  **155.27 KB** |        **1.00** |
| Direct_ReusedPreparedUpdate | BatchUpdate | 100  |  83.904 ms | 10.475 ms |  2.7202 ms |  84.192 ms |  3.20 |    0.89 |  225.08 KB |        1.45 |
| Native_DbBatchUpdate        | BatchUpdate | 100  |  28.592 ms |  8.424 ms |  1.3036 ms |  28.604 ms |  1.09 |    0.31 |  248.88 KB |        1.60 |
| Raw_CaseUpdateControl       | BatchUpdate | 100  |  21.357 ms | 11.543 ms |  1.7863 ms |  20.665 ms |  0.81 |    0.24 |   141.2 KB |        0.91 |
| Raw_DerivedTableJoinControl | BatchUpdate | 100  |  20.925 ms | 13.011 ms |  2.0135 ms |  20.331 ms |  0.80 |    0.23 |  142.41 KB |        0.92 |
|                             |             |      |            |           |            |            |       |         |            |             |
| **Inquiry_SelectedUpdateAll**   | **BatchUpdate** | **1000** |  **48.832 ms** | **21.514 ms** |  **3.3293 ms** |  **48.519 ms** |  **1.00** |    **0.09** | **1155.27 KB** |        **1.00** |
| Direct_ReusedPreparedUpdate | BatchUpdate | 1000 | 643.542 ms | 25.734 ms |  6.6831 ms | 643.775 ms | 13.22 |    0.80 | 1828.56 KB |        1.58 |
| Native_DbBatchUpdate        | BatchUpdate | 1000 | 134.874 ms | 11.160 ms |  2.8982 ms | 136.007 ms |  2.77 |    0.17 | 1964.86 KB |        1.70 |
| Raw_CaseUpdateControl       | BatchUpdate | 1000 |  48.875 ms | 27.565 ms |  7.1586 ms |  47.639 ms |  1.00 |    0.15 |  978.63 KB |        0.85 |
| Raw_DerivedTableJoinControl | BatchUpdate | 1000 |  47.650 ms | 26.368 ms |  6.8477 ms |  46.512 ms |  0.98 |    0.14 |  957.66 KB |        0.83 |
