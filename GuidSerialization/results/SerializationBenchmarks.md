```

BenchmarkDotNet v0.15.8, Windows 10 (10.0.19045.6466/22H2/2022Update)
Intel Core i7-6700K CPU 4.00GHz (Skylake), 1 CPU, 8 logical and 4 physical cores
.NET SDK 10.0.300
  [Host]     : .NET 10.0.8 (10.0.8, 10.0.826.23019), X64 RyuJIT x86-64-v3
  Job-AGBPQC : .NET 10.0.8 (10.0.8, 10.0.826.23019), X64 RyuJIT x86-64-v3

IterationCount=15  WarmupCount=3  

```
| Method             | Categories  | N    | Mean         | Error        | StdDev       | Ratio | RatioSD | Gen0    | Gen1   | Allocated | Alloc Ratio |
|------------------- |------------ |----- |-------------:|-------------:|-------------:|------:|--------:|--------:|-------:|----------:|------------:|
| **De_Native**          | **Deserialize** | **1**    |     **505.6 ns** |     **44.50 ns** |     **41.63 ns** |  **1.01** |    **0.12** |  **0.0515** |      **-** |     **216 B** |        **1.00** |
| De_NativeBytes     | Deserialize | 1    |     612.7 ns |     45.55 ns |     42.61 ns |  1.22 |    0.14 |  0.0534 |      - |     224 B |        1.04 |
| De_BigEndianBytes  | Deserialize | 1    |     520.8 ns |     92.50 ns |     86.52 ns |  1.04 |    0.19 |  0.0534 |      - |     224 B |        1.04 |
| De_String          | Deserialize | 1    |     700.8 ns |     54.41 ns |     50.89 ns |  1.40 |    0.16 |  0.0668 |      - |     280 B |        1.30 |
| De_MyGuidStruct    | Deserialize | 1    |     549.7 ns |     60.18 ns |     56.29 ns |  1.10 |    0.15 |  0.0515 |      - |     216 B |        1.00 |
| De_MyGuidClass     | Deserialize | 1    |     690.5 ns |    122.10 ns |    114.21 ns |  1.38 |    0.26 |  0.0515 |      - |     216 B |        1.00 |
|                    |             |      |              |              |              |       |         |         |        |           |             |
| **De_Native**          | **Deserialize** | **1000** |  **75,250.1 ns** | **16,258.95 ns** | **15,208.64 ns** |  **1.05** |    **0.32** |  **7.5684** |      **-** |   **32136 B** |        **1.00** |
| De_NativeBytes     | Deserialize | 1000 |  84,210.0 ns |  8,445.11 ns |  7,899.56 ns |  1.17 |    0.29 | 15.2588 | 1.8311 |   64136 B |        2.00 |
| De_BigEndianBytes  | Deserialize | 1000 |  79,214.8 ns | 14,747.19 ns | 13,794.53 ns |  1.10 |    0.31 | 15.2588 | 1.8311 |   64136 B |        2.00 |
| De_String          | Deserialize | 1000 | 155,945.0 ns | 21,935.88 ns | 20,518.84 ns |  2.17 |    0.57 | 28.5645 |      - |  120136 B |        3.74 |
| De_MyGuidStruct    | Deserialize | 1000 |  94,265.3 ns |  5,830.43 ns |  5,168.52 ns |  1.31 |    0.30 |  7.5684 |      - |   32136 B |        1.00 |
| De_MyGuidClass     | Deserialize | 1000 | 107,367.8 ns | 15,329.51 ns | 14,339.23 ns |  1.49 |    0.39 | 13.3057 | 0.8545 |   56136 B |        1.75 |
|                    |             |      |              |              |              |       |         |         |        |           |             |
| **Ser_Native**         | **Serialize**   | **1**    |     **287.8 ns** |     **51.02 ns** |     **47.73 ns** |  **1.03** |    **0.26** |  **0.0305** |      **-** |     **128 B** |        **1.00** |
| Ser_NativeBytes    | Serialize   | 1    |     288.3 ns |     38.37 ns |     35.89 ns |  1.03 |    0.24 |  0.0381 |      - |     160 B |        1.25 |
| Ser_BigEndianBytes | Serialize   | 1    |     289.9 ns |     22.57 ns |     21.11 ns |  1.04 |    0.21 |  0.0381 |      - |     160 B |        1.25 |
| Ser_String         | Serialize   | 1    |     318.5 ns |     31.73 ns |     29.68 ns |  1.14 |    0.24 |  0.0515 |      - |     216 B |        1.69 |
| Ser_MyGuidStruct   | Serialize   | 1    |     365.2 ns |     82.66 ns |     77.32 ns |  1.31 |    0.37 |  0.0305 |      - |     128 B |        1.00 |
| Ser_MyGuidClass    | Serialize   | 1    |     608.1 ns |     92.84 ns |     86.84 ns |  2.18 |    0.52 |  0.0362 |      - |     152 B |        1.19 |
|                    |             |      |              |              |              |       |         |         |        |           |             |
| **Ser_Native**         | **Serialize**   | **1000** |  **81,272.6 ns** | **17,980.24 ns** | **16,818.73 ns** |  **1.04** |    **0.30** |  **3.7842** |      **-** |   **16112 B** |        **1.00** |
| Ser_NativeBytes    | Serialize   | 1000 |  43,589.6 ns |  8,902.08 ns |  8,327.01 ns |  0.56 |    0.15 | 11.4746 |      - |   48112 B |        2.99 |
| Ser_BigEndianBytes | Serialize   | 1000 |  47,966.1 ns |  8,221.03 ns |  7,689.96 ns |  0.61 |    0.16 | 11.4746 |      - |   48112 B |        2.99 |
| Ser_String         | Serialize   | 1000 |  88,657.3 ns |  7,722.42 ns |  7,223.55 ns |  1.14 |    0.24 | 24.7803 | 3.7842 |  104112 B |        6.46 |
| Ser_MyGuidStruct   | Serialize   | 1000 | 135,448.4 ns | 27,392.66 ns | 25,623.11 ns |  1.73 |    0.47 |  3.6621 |      - |   16112 B |        1.00 |
| Ser_MyGuidClass    | Serialize   | 1000 | 288,338.6 ns | 44,076.88 ns | 41,229.55 ns |  3.69 |    0.90 | 40.5273 |      - |  171296 B |       10.63 |
