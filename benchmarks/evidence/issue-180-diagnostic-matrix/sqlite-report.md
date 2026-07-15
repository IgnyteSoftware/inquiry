```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.8737/25H2/2025Update/HudsonValley2)
Unknown processor
.NET SDK 10.0.301
  [Host] : .NET 10.0.9 (10.0.9, 10.0.926.27113), X64 RyuJIT x86-64-v4

Toolchain=InProcessEmitToolchain  InvocationCount=1  IterationCount=5
LaunchCount=1  UnrollFactor=1  WarmupCount=2

```
| Method                               | Categories | Rows | Mean      | Error      | StdDev    | Ratio | RatioSD | Allocated | Alloc Ratio |
|------------------------------------- |----------- |----- |----------:|-----------:|----------:|------:|--------:|----------:|------------:|
| **Inquiry_SelectedDeleteAll**            | **Delete**     | **1**    |  **4.571 ms** |  **0.6123 ms** | **0.1590 ms** |  **1.00** |    **0.04** |   **3.07 KB** |        **1.00** |
| Direct_ReusedPreparedDelete          | Delete     | 1    |  5.683 ms |  3.4193 ms | 0.8880 ms |  1.24 |    0.18 |   7.39 KB |        2.41 |
| Raw_PreSerializedJsonEachDeleteFloor | Delete     | 1    |  4.623 ms |  1.2046 ms | 0.1864 ms |  1.01 |    0.05 |   7.37 KB |        2.40 |
| Raw_EndToEndJsonEachDelete           | Delete     | 1    |  5.106 ms |  3.9170 ms | 0.6062 ms |  1.12 |    0.12 |   7.35 KB |        2.39 |
|                                      |            |      |           |            |           |       |         |           |             |
| **Inquiry_SelectedDeleteAll**            | **Delete**     | **10**   | **10.623 ms** |  **6.2492 ms** | **1.6229 ms** |  **1.02** |    **0.21** |   **8.55 KB** |        **1.00** |
| Direct_ReusedPreparedDelete          | Delete     | 10   |  7.582 ms |  4.1513 ms | 1.0781 ms |  0.73 |    0.14 |   9.66 KB |        1.13 |
| Raw_PreSerializedJsonEachDeleteFloor | Delete     | 10   |  6.076 ms |  1.8270 ms | 0.2827 ms |  0.58 |    0.09 |   7.59 KB |        0.89 |
| Raw_EndToEndJsonEachDelete           | Delete     | 10   |  9.943 ms | 10.6133 ms | 2.7562 ms |  0.95 |    0.28 |   7.66 KB |        0.90 |
|                                      |            |      |           |            |           |       |         |           |             |
| **Inquiry_SelectedDeleteAll**            | **Delete**     | **100**  |  **6.456 ms** |  **2.8810 ms** | **0.7482 ms** |  **1.01** |    **0.15** |  **12.28 KB** |        **1.00** |
| Direct_ReusedPreparedDelete          | Delete     | 100  |  6.110 ms |  1.0038 ms | 0.2607 ms |  0.96 |    0.11 |  33.52 KB |        2.73 |
| Raw_PreSerializedJsonEachDeleteFloor | Delete     | 100  |  6.522 ms |  3.9815 ms | 0.6161 ms |  1.02 |    0.14 |   7.27 KB |        0.59 |
| Raw_EndToEndJsonEachDelete           | Delete     | 100  |  6.211 ms |  0.4590 ms | 0.1192 ms |  0.97 |    0.11 |   8.19 KB |        0.67 |
|                                      |            |      |           |            |           |       |         |           |             |
| **Inquiry_SelectedDeleteAll**            | **Delete**     | **1000** |  **7.013 ms** |  **2.2636 ms** | **0.5878 ms** |  **1.01** |    **0.11** |  **73.36 KB** |        **1.00** |
| Direct_ReusedPreparedDelete          | Delete     | 1000 |  5.596 ms |  2.6592 ms | 0.4115 ms |  0.80 |    0.08 | 272.63 KB |        3.72 |
| Raw_PreSerializedJsonEachDeleteFloor | Delete     | 1000 |  4.877 ms |  0.5342 ms | 0.1387 ms |  0.70 |    0.06 |  11.14 KB |        0.15 |
| Raw_EndToEndJsonEachDelete           | Delete     | 1000 |  5.747 ms |  4.9434 ms | 1.2838 ms |  0.82 |    0.18 |  19.05 KB |        0.26 |
|                                      |            |      |           |            |           |       |         |           |             |
| **Inquiry_SelectedInsertAll**            | **Insert**     | **1**    |  **4.405 ms** |  **0.6725 ms** | **0.1747 ms** |  **1.00** |    **0.05** |   **8.08 KB** |        **1.00** |
| Direct_ReusedPreparedInsert          | Insert     | 1    |  5.573 ms |  4.3997 ms | 1.1426 ms |  1.27 |    0.24 |   7.58 KB |        0.94 |
| Raw_PrecomputedMultiRowInsertFloor   | Insert     | 1    |  5.042 ms |  2.7681 ms | 0.7189 ms |  1.15 |    0.16 |   7.65 KB |        0.95 |
| Raw_EndToEndMultiRowInsert           | Insert     | 1    |  5.207 ms |  5.2537 ms | 0.8130 ms |  1.18 |    0.17 |   8.45 KB |        1.05 |
|                                      |            |      |           |            |           |       |         |           |             |
| **Inquiry_SelectedInsertAll**            | **Insert**     | **10**   |  **6.221 ms** |  **3.9942 ms** | **1.0373 ms** |  **1.03** |    **0.25** |  **11.99 KB** |        **1.00** |
| Direct_ReusedPreparedInsert          | Insert     | 10   |  7.373 ms |  4.9914 ms | 1.2962 ms |  1.22 |    0.30 |  11.28 KB |        0.94 |
| Raw_PrecomputedMultiRowInsertFloor   | Insert     | 10   |  6.083 ms |  2.5883 ms | 0.6722 ms |  1.00 |    0.21 |  12.44 KB |        1.04 |
| Raw_EndToEndMultiRowInsert           | Insert     | 10   |  4.169 ms |  0.5736 ms | 0.1490 ms |  0.69 |    0.13 |  13.52 KB |        1.13 |
|                                      |            |      |           |            |           |       |         |           |             |
| **Inquiry_SelectedInsertAll**            | **Insert**     | **100**  |  **4.479 ms** |  **0.5454 ms** | **0.1416 ms** |  **1.00** |    **0.04** |  **45.04 KB** |        **1.00** |
| Direct_ReusedPreparedInsert          | Insert     | 100  |  4.284 ms |  0.6722 ms | 0.1746 ms |  0.96 |    0.05 |  43.63 KB |        0.97 |
| Raw_PrecomputedMultiRowInsertFloor   | Insert     | 100  |  4.611 ms |  0.5651 ms | 0.1468 ms |  1.03 |    0.04 |  54.49 KB |        1.21 |
| Raw_EndToEndMultiRowInsert           | Insert     | 100  |  4.617 ms |  0.4981 ms | 0.1294 ms |  1.03 |    0.04 |  61.83 KB |        1.37 |
|                                      |            |      |           |            |           |       |         |           |             |
| **Inquiry_SelectedInsertAll**            | **Insert**     | **1000** |  **7.203 ms** |  **3.0048 ms** | **0.7803 ms** |  **1.01** |    **0.14** | **375.51 KB** |        **1.00** |
| Direct_ReusedPreparedInsert          | Insert     | 1000 |  6.966 ms |  4.7261 ms | 1.2273 ms |  0.98 |    0.18 | 367.06 KB |        0.98 |
| Raw_PrecomputedMultiRowInsertFloor   | Insert     | 1000 | 15.435 ms |  9.2627 ms | 2.4055 ms |  2.16 |    0.37 |  478.9 KB |        1.28 |
| Raw_EndToEndMultiRowInsert           | Insert     | 1000 | 14.365 ms |  4.3804 ms | 1.1376 ms |  2.01 |    0.24 | 560.04 KB |        1.49 |
|                                      |            |      |           |            |           |       |         |           |             |
| **Inquiry_SelectedUpdateAll**            | **Update**     | **1**    |  **4.226 ms** |  **0.3644 ms** | **0.0946 ms** |  **1.00** |    **0.03** |   **8.36 KB** |        **1.00** |
| Direct_ReusedPreparedUpdate          | Update     | 1    |  4.345 ms |  0.5624 ms | 0.1461 ms |  1.03 |    0.04 |   7.91 KB |        0.95 |
|                                      |            |      |           |            |           |       |         |           |             |
| **Inquiry_SelectedUpdateAll**            | **Update**     | **10**   |  **4.230 ms** |  **0.7637 ms** | **0.1182 ms** |  **1.00** |    **0.04** |  **11.95 KB** |        **1.00** |
| Direct_ReusedPreparedUpdate          | Update     | 10   |  4.290 ms |  0.4088 ms | 0.0633 ms |  1.01 |    0.03 |  11.28 KB |        0.94 |
|                                      |            |      |           |            |           |       |         |           |             |
| **Inquiry_SelectedUpdateAll**            | **Update**     | **100**  |  **4.274 ms** |  **0.4859 ms** | **0.0752 ms** |  **1.00** |    **0.02** |  **44.99 KB** |        **1.00** |
| Direct_ReusedPreparedUpdate          | Update     | 100  |  4.458 ms |  0.8186 ms | 0.1267 ms |  1.04 |    0.03 |   43.3 KB |        0.96 |
|                                      |            |      |           |            |           |       |         |           |             |
| **Inquiry_SelectedUpdateAll**            | **Update**     | **1000** |  **5.084 ms** |  **0.4618 ms** | **0.1199 ms** |  **1.00** |    **0.03** | **375.46 KB** |        **1.00** |
| Direct_ReusedPreparedUpdate          | Update     | 1000 |  5.116 ms |  1.3410 ms | 0.2075 ms |  1.01 |    0.04 | 367.06 KB |        0.98 |
