```

BenchmarkDotNet v0.15.8, Windows 10 (10.0.19045.6466/22H2/2022Update)
Intel Core i7-6700K CPU 4.00GHz (Skylake), 1 CPU, 8 logical and 4 physical cores
.NET SDK 10.0.300
  [Host]     : .NET 10.0.8 (10.0.8, 10.0.826.23019), X64 RyuJIT x86-64-v3
  Job-AGBPQC : .NET 10.0.8 (10.0.8, 10.0.826.23019), X64 RyuJIT x86-64-v3

IterationCount=15  WarmupCount=3  

```
| Method                     | Categories  | N    | Mean       | Error      | StdDev     | Ratio | RatioSD | Gen0    | Gen1   | Allocated | Alloc Ratio |
|--------------------------- |------------ |----- |-----------:|-----------:|-----------:|------:|--------:|--------:|-------:|----------:|------------:|
| Convert_UnitsNanos         | Convert     | 1000 |  19.800 μs |  1.1803 μs |  1.1040 μs |     ? |       ? |       - |      - |         - |           ? |
| Convert_FixedDecimal       | Convert     | 1000 |  11.824 μs |  0.8062 μs |  0.7541 μs |     ? |       ? |       - |      - |         - |           ? |
| Convert_Decimal64          | Convert     | 1000 |   8.406 μs |  1.0085 μs |  0.9433 μs |     ? |       ? |       - |      - |         - |           ? |
| Convert_PackedDecimal64    | Convert     | 1000 |  12.061 μs |  1.1879 μs |  1.1112 μs |     ? |       ? |       - |      - |         - |           ? |
| Convert_Decimal128         | Convert     | 1000 |   9.395 μs |  1.0967 μs |  0.9722 μs |     ? |       ? |       - |      - |         - |           ? |
|                            |             |      |            |            |            |       |         |         |        |           |             |
| De_Native                  | Deserialize | 1000 | 115.996 μs | 13.2475 μs | 12.3917 μs |  1.01 |    0.16 |  7.5684 |      - |   32136 B |        1.00 |
| De_BclFast                 | Deserialize | 1000 | 110.844 μs | 14.2984 μs | 13.3747 μs |  0.97 |    0.16 |  7.5684 |      - |   32136 B |        1.00 |
| De_String                  | Deserialize | 1000 | 201.617 μs | 24.8168 μs | 23.2136 μs |  1.76 |    0.28 | 17.0898 |      - |   72048 B |        2.24 |
| De_L300                    | Deserialize | 1000 | 129.604 μs | 18.2474 μs | 17.0686 μs |  1.13 |    0.20 |  7.5684 |      - |   32136 B |        1.00 |
| De_Utf8                    | Deserialize | 1000 | 170.780 μs | 30.8098 μs | 28.8195 μs |  1.49 |    0.30 |  7.5684 |      - |   32136 B |        1.00 |
| De_UnitsNanos              | Deserialize | 1000 | 116.995 μs | 13.4564 μs | 12.5871 μs |  1.02 |    0.16 |  7.5684 |      - |   32136 B |        1.00 |
| De_FixedDecimal            | Deserialize | 1000 |  55.658 μs |  3.2437 μs |  2.8754 μs |  0.49 |    0.06 |  5.7373 |      - |   24136 B |        0.75 |
| De_FixedDecimal_Packed     | Deserialize | 1000 |  38.917 μs |  2.9289 μs |  2.7397 μs |  0.34 |    0.05 |  5.7373 |      - |   24072 B |        0.75 |
| De_Decimal64               | Deserialize | 1000 |  32.328 μs |  4.0368 μs |  3.7760 μs |  0.28 |    0.05 |  5.7373 |      - |   24136 B |        0.75 |
| De_Decimal64_Packed        | Deserialize | 1000 |  20.443 μs |  2.1123 μs |  1.9758 μs |  0.18 |    0.03 |  5.7373 |      - |   24072 B |        0.75 |
| De_PackedDecimal64         | Deserialize | 1000 |  50.801 μs |  3.9387 μs |  3.6843 μs |  0.44 |    0.06 |  5.7373 |      - |   24136 B |        0.75 |
| De_PackedDecimal64_Packed  | Deserialize | 1000 |  35.310 μs |  3.4332 μs |  3.2114 μs |  0.31 |    0.04 |  5.7373 |      - |   24072 B |        0.75 |
| De_Decimal128              | Deserialize | 1000 |  61.802 μs |  7.6402 μs |  7.1466 μs |  0.54 |    0.09 |  7.5684 |      - |   32136 B |        1.00 |
| De_Decimal128_Packed       | Deserialize | 1000 |  29.029 μs |  3.5411 μs |  3.3124 μs |  0.25 |    0.04 |  7.6599 |      - |   32072 B |        1.00 |
|                            |             |      |            |            |            |       |         |         |        |           |             |
| Ser_Native                 | Serialize   | 1000 |  90.202 μs | 14.2709 μs | 13.3490 μs |  1.02 |    0.21 |  3.7842 |      - |   16112 B |        1.00 |
| Ser_BclFast                | Serialize   | 1000 |  89.012 μs | 10.5833 μs |  8.8375 μs |  1.01 |    0.18 |  3.7842 |      - |   16112 B |        1.00 |
| Ser_String                 | Serialize   | 1000 | 134.885 μs | 13.6384 μs | 12.7574 μs |  1.53 |    0.27 | 13.1836 | 1.9531 |   56024 B |        3.48 |
| Ser_L300                   | Serialize   | 1000 | 177.805 μs | 22.1013 μs | 20.6736 μs |  2.01 |    0.38 |  3.6621 |      - |   16112 B |        1.00 |
| Ser_Utf8                   | Serialize   | 1000 | 110.529 μs | 12.5910 μs | 11.7777 μs |  1.25 |    0.23 |  3.6621 |      - |   16112 B |        1.00 |
| Ser_UnitsNanos             | Serialize   | 1000 | 120.211 μs | 13.8798 μs | 12.9832 μs |  1.36 |    0.25 |  3.7842 |      - |   16112 B |        1.00 |
| Ser_FixedDecimal           | Serialize   | 1000 |  34.841 μs |  4.3681 μs |  4.0860 μs |  0.39 |    0.07 |  1.8921 |      - |    8112 B |        0.50 |
| Ser_FixedDecimal_Packed    | Serialize   | 1000 |  32.846 μs |  3.3640 μs |  3.1466 μs |  0.37 |    0.07 |  1.9226 |      - |    8048 B |        0.50 |
| Ser_Decimal64              | Serialize   | 1000 |  23.334 μs |  3.7862 μs |  3.5416 μs |  0.26 |    0.06 |  1.9226 |      - |    8112 B |        0.50 |
| Ser_Decimal64_Packed       | Serialize   | 1000 |  15.653 μs |  0.2859 μs |  0.2674 μs |  0.18 |    0.03 |  1.9226 |      - |    8048 B |        0.50 |
| Ser_PackedDecimal64        | Serialize   | 1000 |  27.703 μs |  4.4588 μs |  4.1708 μs |  0.31 |    0.07 |  1.9226 |      - |    8112 B |        0.50 |
| Ser_PackedDecimal64_Packed | Serialize   | 1000 |  23.217 μs |  3.4308 μs |  3.2092 μs |  0.26 |    0.05 |  1.9226 |      - |    8048 B |        0.50 |
| Ser_Decimal128             | Serialize   | 1000 |  31.931 μs |  6.1270 μs |  5.7312 μs |  0.36 |    0.08 |  3.8452 |      - |   16112 B |        1.00 |
| Ser_Decimal128_Packed      | Serialize   | 1000 |  20.220 μs |  2.6885 μs |  2.2450 μs |  0.23 |    0.04 |  3.8147 |      - |   16048 B |        1.00 |
