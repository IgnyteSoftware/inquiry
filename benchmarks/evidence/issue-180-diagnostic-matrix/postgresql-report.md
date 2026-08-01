```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.8737/25H2/2025Update/HudsonValley2)
AMD Ryzen 7 9800X3D 4.70GHz, 1 CPU, 16 logical and 8 physical cores
.NET SDK 10.0.301
  [Host] : .NET 10.0.9 (10.0.9, 10.0.926.27113), X64 RyuJIT x86-64-v4

Toolchain=InProcessEmitToolchain  InvocationCount=1  IterationCount=5
LaunchCount=1  UnrollFactor=1  WarmupCount=2

```
| Method                             | Categories | Rows | Mean       | Error      | StdDev     | Ratio  | RatioSD | Allocated  | Alloc Ratio |
|----------------------------------- |----------- |----- |-----------:|-----------:|-----------:|-------:|--------:|-----------:|------------:|
| **Inquiry_SelectedDeleteAll**          | **Delete**     | **1**    |   **1.412 ms** |  **0.3787 ms** |  **0.0983 ms** |   **1.00** |    **0.09** |    **4.66 KB** |        **1.00** |
| Direct_ReusedPreparedDelete        | Delete     | 1    |   1.400 ms |  0.4195 ms |  0.1089 ms |   1.00 |    0.09 |    9.02 KB |        1.93 |
| Raw_AnyArrayDelete                 | Delete     | 1    |   1.367 ms |  0.1892 ms |  0.0491 ms |   0.97 |    0.07 |    9.48 KB |        2.03 |
|                                    |            |      |            |            |            |        |         |            |             |
| **Inquiry_SelectedDeleteAll**          | **Delete**     | **10**   |   **1.538 ms** |  **0.3975 ms** |  **0.1032 ms** |   **1.00** |    **0.09** |   **10.54 KB** |        **1.00** |
| Direct_ReusedPreparedDelete        | Delete     | 10   |   6.982 ms |  1.0965 ms |  0.2848 ms |   4.56 |    0.33 |   18.02 KB |        1.71 |
| Raw_AnyArrayDelete                 | Delete     | 10   |   1.420 ms |  0.1802 ms |  0.0279 ms |   0.93 |    0.06 |    9.52 KB |        0.90 |
|                                    |            |      |            |            |            |        |         |            |             |
| **Inquiry_SelectedDeleteAll**          | **Delete**     | **100**  |   **1.719 ms** |  **0.4059 ms** |  **0.1054 ms** |   **1.00** |    **0.08** |   **10.52 KB** |        **1.00** |
| Direct_ReusedPreparedDelete        | Delete     | 100  |  56.972 ms |  3.4520 ms |  0.8965 ms |  33.23 |    1.89 |   77.24 KB |        7.34 |
| Raw_AnyArrayDelete                 | Delete     | 100  |   1.479 ms |  0.2717 ms |  0.0706 ms |   0.86 |    0.06 |    7.98 KB |        0.76 |
|                                    |            |      |            |            |            |        |         |            |             |
| **Inquiry_SelectedDeleteAll**          | **Delete**     | **1000** |   **2.812 ms** |  **0.6224 ms** |  **0.0963 ms** |   **1.00** |    **0.04** |    **18.2 KB** |        **1.00** |
| Direct_ReusedPreparedDelete        | Delete     | 1000 | 562.303 ms | 41.0629 ms |  6.3545 ms | 200.14 |    6.58 |  702.95 KB |       38.63 |
| Raw_AnyArrayDelete                 | Delete     | 1000 |   2.299 ms |  1.3680 ms |  0.3553 ms |   0.82 |    0.12 |    8.54 KB |        0.47 |
|                                    |            |      |            |            |            |        |         |            |             |
| **Inquiry_SelectedInsertAll**          | **Insert**     | **1**    |   **1.450 ms** |  **0.3923 ms** |  **0.0607 ms** |   **1.00** |    **0.05** |   **10.91 KB** |        **1.00** |
| Direct_ReusedPreparedInsert        | Insert     | 1    |   1.224 ms |  0.3142 ms |  0.0816 ms |   0.84 |    0.06 |    8.95 KB |        0.82 |
| Raw_PrecomputedMultiRowInsertFloor | Insert     | 1    |   1.294 ms |  0.1853 ms |  0.0481 ms |   0.89 |    0.05 |    9.33 KB |        0.86 |
| Raw_EndToEndMultiRowInsert         | Insert     | 1    |   1.239 ms |  0.1088 ms |  0.0282 ms |   0.86 |    0.04 |   10.09 KB |        0.93 |
|                                    |            |      |            |            |            |        |         |            |             |
| **Inquiry_SelectedInsertAll**          | **Insert**     | **10**   |   **1.521 ms** |  **0.3440 ms** |  **0.0893 ms** |   **1.00** |    **0.08** |   **18.23 KB** |        **1.00** |
| Direct_ReusedPreparedInsert        | Insert     | 10   |   6.388 ms |  1.1825 ms |  0.3071 ms |   4.21 |    0.30 |   15.27 KB |        0.84 |
| Raw_PrecomputedMultiRowInsertFloor | Insert     | 10   |   1.330 ms |  0.2342 ms |  0.0608 ms |   0.88 |    0.06 |   16.14 KB |        0.89 |
| Raw_EndToEndMultiRowInsert         | Insert     | 10   |   1.265 ms |  0.1611 ms |  0.0418 ms |   0.83 |    0.05 |   16.23 KB |        0.89 |
|                                    |            |      |            |            |            |        |         |            |             |
| **Inquiry_SelectedInsertAll**          | **Insert**     | **100**  |   **1.950 ms** |  **0.4681 ms** |  **0.1216 ms** |   **1.00** |    **0.08** |   **95.68 KB** |        **1.00** |
| Direct_ReusedPreparedInsert        | Insert     | 100  |  53.772 ms |  5.4384 ms |  1.4123 ms |  27.66 |    1.75 |   78.44 KB |        0.82 |
| Raw_PrecomputedMultiRowInsertFloor | Insert     | 100  |   1.809 ms |  0.6130 ms |  0.1592 ms |   0.93 |    0.09 |   81.67 KB |        0.85 |
| Raw_EndToEndMultiRowInsert         | Insert     | 100  |   1.727 ms |  0.4420 ms |  0.1148 ms |   0.89 |    0.08 |   89.71 KB |        0.94 |
|                                    |            |      |            |            |            |        |         |            |             |
| **Inquiry_SelectedInsertAll**          | **Insert**     | **1000** |   **4.774 ms** |  **0.8525 ms** |  **0.1319 ms** |   **1.00** |    **0.03** |  **983.28 KB** |        **1.00** |
| Direct_ReusedPreparedInsert        | Insert     | 1000 | 561.546 ms | 39.1111 ms | 10.1570 ms | 117.69 |    3.49 |  703.59 KB |        0.72 |
| Raw_PrecomputedMultiRowInsertFloor | Insert     | 1000 |   4.474 ms |  1.5503 ms |  0.4026 ms |   0.94 |    0.08 |  710.11 KB |        0.72 |
| Raw_EndToEndMultiRowInsert         | Insert     | 1000 |   4.169 ms |  0.3818 ms |  0.0992 ms |   0.87 |    0.03 |  793.99 KB |        0.81 |
|                                    |            |      |            |            |            |        |         |            |             |
| **Inquiry_SelectedUpdateAll**          | **Update**     | **1**    |   **1.364 ms** |  **0.1997 ms** |  **0.0519 ms** |   **1.00** |    **0.05** |   **10.48 KB** |        **1.00** |
| Direct_ReusedPreparedUpdate        | Update     | 1    |   1.286 ms |  0.0802 ms |  0.0124 ms |   0.94 |    0.03 |    9.59 KB |        0.92 |
| Native_NpgsqlBatchUpdate           | Update     | 1    |   1.295 ms |  0.1735 ms |  0.0451 ms |   0.95 |    0.05 |    9.39 KB |        0.90 |
|                                    |            |      |            |            |            |        |         |            |             |
| **Inquiry_SelectedUpdateAll**          | **Update**     | **10**   |   **1.462 ms** |  **0.3328 ms** |  **0.0864 ms** |   **1.00** |    **0.08** |   **19.47 KB** |        **1.00** |
| Direct_ReusedPreparedUpdate        | Update     | 10   |   6.238 ms |  0.1805 ms |  0.0279 ms |   4.28 |    0.23 |   15.55 KB |        0.80 |
| Native_NpgsqlBatchUpdate           | Update     | 10   |   1.449 ms |  0.2538 ms |  0.0393 ms |   0.99 |    0.06 |    18.8 KB |        0.97 |
|                                    |            |      |            |            |            |        |         |            |             |
| **Inquiry_SelectedUpdateAll**          | **Update**     | **100**  |   **2.436 ms** |  **0.2775 ms** |  **0.0721 ms** |   **1.00** |    **0.04** |  **114.37 KB** |        **1.00** |
| Direct_ReusedPreparedUpdate        | Update     | 100  |  56.934 ms |  3.9227 ms |  1.0187 ms |  23.38 |    0.74 |   78.15 KB |        0.68 |
| Native_NpgsqlBatchUpdate           | Update     | 100  |   3.397 ms |  0.8118 ms |  0.2108 ms |   1.40 |    0.09 |   110.6 KB |        0.97 |
|                                    |            |      |            |            |            |        |         |            |             |
| **Inquiry_SelectedUpdateAll**          | **Update**     | **1000** |  **12.781 ms** |  **2.3496 ms** |  **0.6102 ms** |   **1.00** |    **0.06** | **1053.47 KB** |        **1.00** |
| Direct_ReusedPreparedUpdate        | Update     | 1000 | 560.538 ms |  6.7048 ms |  1.7412 ms |  43.94 |    1.90 |  704.03 KB |        0.67 |
| Native_NpgsqlBatchUpdate           | Update     | 1000 |  20.625 ms |  4.8956 ms |  0.7576 ms |   1.62 |    0.09 | 1028.34 KB |        0.98 |
