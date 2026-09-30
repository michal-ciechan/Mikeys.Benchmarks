```

BenchmarkDotNet v0.15.8, Windows 10 (10.0.19045.6466/22H2/2022Update)
Intel Core i7-6700K CPU 4.00GHz (Skylake), 1 CPU, 8 logical and 4 physical cores
.NET SDK 10.0.300
  [Host]     : .NET 10.0.8 (10.0.8, 10.0.826.23019), X64 RyuJIT x86-64-v3
  Job-AGBPQC : .NET 10.0.8 (10.0.8, 10.0.826.23019), X64 RyuJIT x86-64-v3

IterationCount=15  WarmupCount=3  

```
| Method                  | Categories  | N    | Mean      | Error     | StdDev    | Ratio | RatioSD | Gen0    | Gen1   | Allocated | Alloc Ratio |
|------------------------ |------------ |----- |----------:|----------:|----------:|------:|--------:|--------:|-------:|----------:|------------:|
| Convert_String          | Convert     | 1000 | 192.10 μs | 13.029 μs | 11.550 μs |     ? |       ? | 11.2305 | 1.4648 |   47912 B |           ? |
| Convert_UnitsNanos      | Convert     | 1000 | 102.89 μs |  9.104 μs |  8.516 μs |     ? |       ? |       - |      - |         - |           ? |
| Convert_FixedDecimal    | Convert     | 1000 |  23.08 μs |  4.049 μs |  3.589 μs |     ? |       ? |       - |      - |         - |           ? |
| Convert_Decimal64       | Convert     | 1000 |  17.37 μs |  1.426 μs |  1.334 μs |     ? |       ? |       - |      - |         - |           ? |
| Convert_PackedDecimal64 | Convert     | 1000 |  11.12 μs |  1.533 μs |  1.359 μs |     ? |       ? |       - |      - |         - |           ? |
|                         |             |      |           |           |           |       |         |         |        |           |             |
| De_Native               | Deserialize | 1000 |  80.10 μs | 17.016 μs | 15.917 μs |  1.04 |    0.28 |  7.5684 |      - |   32136 B |        1.00 |
| De_String               | Deserialize | 1000 | 195.23 μs | 35.249 μs | 32.972 μs |  2.52 |    0.62 | 17.0898 |      - |   72048 B |        2.24 |
| De_L300                 | Deserialize | 1000 | 116.93 μs | 22.673 μs | 21.208 μs |  1.51 |    0.38 |  7.5684 |      - |   32136 B |        1.00 |
| De_UnitsNanos           | Deserialize | 1000 | 143.11 μs | 15.925 μs | 14.897 μs |  1.85 |    0.39 |  7.5684 |      - |   32136 B |        1.00 |
| De_FixedDecimal         | Deserialize | 1000 |  43.59 μs |  6.018 μs |  5.629 μs |  0.56 |    0.12 |  5.7373 |      - |   24136 B |        0.75 |
| De_Decimal64            | Deserialize | 1000 |  33.80 μs |  5.867 μs |  5.488 μs |  0.44 |    0.11 |  5.7373 |      - |   24136 B |        0.75 |
| De_PackedDecimal64      | Deserialize | 1000 |  43.76 μs |  8.804 μs |  8.235 μs |  0.57 |    0.15 |  5.7373 |      - |   24136 B |        0.75 |
|                         |             |      |           |           |           |       |         |         |        |           |             |
| Ser_Native              | Serialize   | 1000 |  78.71 μs | 12.053 μs | 10.685 μs |  1.02 |    0.19 |  3.7842 |      - |   16112 B |        1.00 |
| Ser_String              | Serialize   | 1000 | 125.61 μs | 24.547 μs | 22.961 μs |  1.62 |    0.36 | 13.3057 | 2.1973 |   56024 B |        3.48 |
| Ser_L300                | Serialize   | 1000 | 135.55 μs | 15.422 μs | 14.426 μs |  1.75 |    0.29 |  3.7842 |      - |   16112 B |        1.00 |
| Ser_UnitsNanos          | Serialize   | 1000 | 162.30 μs | 21.261 μs | 18.848 μs |  2.10 |    0.36 |  3.6621 |      - |   16112 B |        1.00 |
| Ser_FixedDecimal        | Serialize   | 1000 |  47.13 μs |  8.555 μs |  8.002 μs |  0.61 |    0.13 |  1.8921 |      - |    8112 B |        0.50 |
| Ser_Decimal64           | Serialize   | 1000 |  25.88 μs |  4.605 μs |  4.308 μs |  0.33 |    0.07 |  1.9226 |      - |    8112 B |        0.50 |
| Ser_PackedDecimal64     | Serialize   | 1000 |  31.14 μs |  4.011 μs |  3.752 μs |  0.40 |    0.07 |  1.8921 |      - |    8112 B |        0.50 |
